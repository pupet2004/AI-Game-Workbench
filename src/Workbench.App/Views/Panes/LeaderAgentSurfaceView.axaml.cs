using System.Collections.Specialized;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Workbench.App.Services;
using Workbench.App.ViewModels.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.Runtime.Agents;

namespace Workbench.App.Views.Panes;

public partial class LeaderAgentSurfaceView : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private LeaderPaneViewModel? _pane;
    private bool _ready;
    private int _attachmentSequence;
    private readonly List<AgentInputPart> _pendingAttachments = [];
    private readonly NativeWebView _surface = new();

    public LeaderAgentSurfaceView()
    {
        InitializeComponent();
        SurfaceHost.Content = _surface;
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        _surface.NavigationCompleted += OnWebViewInitialized;
        _surface.WebMessageReceived += OnWebMessageReceived;
        _surface.NavigateToString(LoadSurfaceHtml(), new Uri("https://workbench.local/"));
    }

    private static string LoadSurfaceHtml()
    {
        return Html;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LocalizationService.Current.PropertyChanged -= OnLocalizationChanged;
        LocalizationService.Current.PropertyChanged += OnLocalizationChanged;
        Subscribe(DataContext as LeaderPaneViewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => Subscribe(DataContext as LeaderPaneViewModel);

    private void Subscribe(LeaderPaneViewModel? pane)
    {
        if (ReferenceEquals(_pane, pane))
        {
            PushState();
            return;
        }

        if (_pane is not null)
        {
            _pane.PropertyChanged -= OnPanePropertyChanged;
            _pane.Messages.CollectionChanged -= OnMessagesChanged;
            _pane.Activities.CollectionChanged -= OnMessagesChanged;
            _pane.ChangedFiles.CollectionChanged -= OnMessagesChanged;
        }

        _pane = pane;
        if (_pane is not null)
        {
            _pane.PropertyChanged += OnPanePropertyChanged;
            _pane.Messages.CollectionChanged += OnMessagesChanged;
            _pane.Activities.CollectionChanged += OnMessagesChanged;
            _pane.ChangedFiles.CollectionChanged += OnMessagesChanged;
        }

        PushState();
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(PushState);

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(PushState);

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(PushState);

    private void OnWebViewInitialized(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _ready = e.IsSuccess;
        PushState();
    }

    private async void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.Body ?? "{}");
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (type == "ready")
            {
                PushState();
                return;
            }

            if (type != "command" || _pane is null) return;
            var command = root.TryGetProperty("command", out var commandElement) ? commandElement.GetString() : null;
            switch (command)
            {
                case "send_turn":
                    _pane.DraftMessage = root.GetProperty("text").GetString() ?? string.Empty;
                    await _pane.SendAsync(_pendingAttachments.ToArray());
                    _pendingAttachments.Clear();
                    break;
                case "steer":
                    await _pane.SteerAsync(root.GetProperty("text").GetString() ?? string.Empty);
                    break;
                case "interrupt":
                    await _pane.StopAsync();
                    break;
                case "respond_approval":
                    await RespondToApprovalAsync(root);
                    break;
                case "respond_question":
                    await RespondToQuestionAsync(root);
                    break;
                case "attach":
                    IssueAttachmentToken(root);
                    break;
                case "reload_transcript":
                    PushState();
                    break;
            }
        }
        catch (Exception error)
        {
            Send(new { type = "event", eventType = "error", payload = new { message = error.Message } });
        }
    }

    private async Task RespondToApprovalAsync(JsonElement root)
    {
        if (_pane is null) return;
        var requestIdText = root.TryGetProperty("requestId", out var requestIdElement)
            ? requestIdElement.GetString()
            : null;
        if (!Guid.TryParse(requestIdText, out var requestId) ||
            _pane.PendingApproval is null ||
            _pane.PendingApproval.RequestId.Value != requestId)
        {
            throw new InvalidOperationException("The approval request is no longer current.");
        }

        var optionId = root.GetProperty("option").GetString();
        var option = _pane.ApprovalOptions.FirstOrDefault(item => string.Equals(item.Option.Id, optionId, StringComparison.Ordinal));
        if (option is not null)
        {
            await _pane.RespondToApprovalAsync(option);
        }
    }

    private async Task RespondToQuestionAsync(JsonElement root)
    {
        if (_pane is null) return;
        var requestId = root.TryGetProperty("requestId", out var requestIdElement) ? requestIdElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(requestId) || _pane.PendingQuestion is null)
        {
            throw new InvalidOperationException("The question request is no longer current.");
        }

        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("answers", out var answerElement) && answerElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in answerElement.EnumerateObject()) answers[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        await _pane.RespondToQuestionAsync(requestId, answers);
    }

    private void IssueAttachmentToken(JsonElement root)
    {
        var file = root.GetProperty("file");
        var name = file.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var mediaType = file.TryGetProperty("type", out var mediaTypeElement) ? mediaTypeElement.GetString() : null;
        var dataUrl = file.TryGetProperty("dataUrl", out var dataUrlElement) ? dataUrlElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mediaType) || string.IsNullOrWhiteSpace(dataUrl))
        {
            throw new InvalidOperationException("The attachment is incomplete.");
        }

        var marker = dataUrl.IndexOf(",", StringComparison.Ordinal);
        if (marker < 0)
        {
            throw new InvalidOperationException("The image attachment is malformed.");
        }

        var bytes = Convert.FromBase64String(dataUrl[(marker + 1)..]);
        if (bytes.Length > 20 * 1024 * 1024)
        {
            throw new InvalidOperationException("Attachments must be 20 MB or smaller.");
        }

        var token = $"workbench-attachment-{++_attachmentSequence:0000}";
        var isImage = mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        var extension = isImage ? mediaType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => Path.GetExtension(name)
        } : Path.GetExtension(name);
        extension = string.IsNullOrWhiteSpace(extension) || extension.Length > 12 ? string.Empty : extension;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI Game Workbench",
            "attachments");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        _pendingAttachments.Add(new AgentInputPart(isImage ? "image" : "file", path, name));
        Send(new
        {
            type = "event",
            eventType = "attachment_received",
            payload = new
            {
                token,
                name,
                size = bytes.Length,
                mediaType,
                isImage,
                queued = true
            }
        });
    }

    private void PushState()
    {
        if (!_ready || _pane is null) return;
        var messages = _pane.Messages.Select((message, index) => new
        {
            id = $"message-{index:0000}",
            role = message.Role switch
            {
                LeaderMessageRole.User => "user",
                LeaderMessageRole.Error => "error",
                _ => "assistant"
            },
            text = message.Text,
            streaming = message.IsStreaming
        }).ToArray();
        var approval = _pane.PendingApproval is null
            ? null
            : new
            {
                requestId = _pane.PendingApproval.RequestId.ToString(),
                description = _pane.PendingApproval.Summary,
                options = _pane.PendingApproval.Options.Select(option => new { id = option.Id, label = option.Label, description = option.Description }).ToArray()
            };
        Send(new
        {
            type = "state",
            session = new
            {
                projectId = _pane.ProjectLeaderId,
                role = "leader",
                assignmentId = (string?)null,
                attemptId = (string?)null,
                agentSessionId = _pane.Session?.Id.ToString(),
                provider = _pane.Session?.ProviderId.Value,
                model = _pane.Session?.ModelId,
                capabilities = new { streaming = true, approval = true, interrupt = true, steer = _pane.SupportsSteer, attachments = true, transcript = true }
            },
            ui = new
            {
                more = LocalizationService.Current["Surface.More"],
                reloadTranscript = LocalizationService.Current["Surface.ReloadTranscript"],
                you = LocalizationService.Current["Surface.You"],
                error = LocalizationService.Current["Surface.Error"],
                approvalRequested = LocalizationService.Current["Surface.ApprovalRequested"],
                agentNeedsInput = LocalizationService.Current["Surface.AgentNeedsInput"],
                changedFiles = LocalizationService.Current["Surface.ChangedFiles"],
                viewDiff = LocalizationService.Current["Surface.ViewDiff"],
                copy = LocalizationService.Current["Surface.Copy"],
                copied = LocalizationService.Current["Surface.Copied"],
                addAttachment = LocalizationService.Current["Surface.AddAttachment"],
                stop = LocalizationService.Current["Surface.Stop"],
                send = LocalizationService.Current["Surface.Send"],
                inputPlaceholder = LocalizationService.Current["Leader.MessagePlaceholder"],
                steerHint = LocalizationService.Current["Surface.SteerHint"],
                turnRunning = LocalizationService.Current["Surface.TurnRunning"],
                inspectAttachments = LocalizationService.Current["Surface.InspectAttachments"],
                agentActivity = LocalizationService.Current["Surface.AgentActivity"],
                ready = LocalizationService.Current["Surface.Ready"],
                sessionPending = LocalizationService.Current["Surface.SessionPending"]
            },
            messages,
            activities = _pane.Activities.Where(activity => IsRenderableActivity(activity.Title)).Select(activity => new
            {
                title = activity.Title,
                detail = activity.Detail,
                complete = activity.IsComplete
            }).ToArray(),
            changedFiles = _pane.ChangedFiles.Select(file => new
            {
                path = file.Path,
                added = file.Added,
                removed = file.Removed,
                diff = file.Diff
            }).ToArray(),
            attachments = _pendingAttachments.Select(attachment => new
            {
                type = attachment.Type,
                name = attachment.Name
            }).ToArray(),
            approval,
            question = _pane.PendingQuestion is null ? null : new
            {
                requestId = _pane.PendingQuestion.RequestId,
                prompt = _pane.PendingQuestion.Prompt,
                options = _pane.PendingQuestion.Options.Select(option => new { id = option.Id, label = option.Label, description = option.Description }).ToArray()
            },
            busy = _pane.IsBusy,
            runtimeStatus = _pane.RuntimeStatus
        });
    }

    private static bool IsRenderableActivity(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        return title.Trim().ToLowerInvariant() is not
            ("usermessage" or "assistantmessage" or "turnstarted" or "turncompleted" or "turn/started" or "turn/completed");
    }

    private void Send(object message)
    {
        if (!_ready) return;
        var json = JsonSerializer.Serialize(message, JsonOptions);
        _ = _surface.InvokeScript($"window.receiveFromHost?.({json});");
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_pane is not null)
        {
            _pane.PropertyChanged -= OnPanePropertyChanged;
            _pane.Messages.CollectionChanged -= OnMessagesChanged;
            _pane.Activities.CollectionChanged -= OnMessagesChanged;
            _pane.ChangedFiles.CollectionChanged -= OnMessagesChanged;
        }
        LocalizationService.Current.PropertyChanged -= OnLocalizationChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private const string Html = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <style>
        :root{color-scheme:dark;font-family:Segoe UI,system-ui,sans-serif}*{box-sizing:border-box;min-width:0}html,body{width:100%;height:100%;min-height:0}body{margin:0;background:#000;color:#fff;display:flex;flex-direction:column;overflow:hidden}#meta{flex:0 0 auto;padding:10px 14px;border-bottom:1px solid #333;background:#000;color:#fff;font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}#transcript{flex:1 1 auto;min-height:0;min-width:0;overflow-y:auto;overflow-x:hidden;padding:18px 14px;background:#000;user-select:text;scrollbar-width:thin;scrollbar-color:#999 transparent}#transcript::-webkit-scrollbar{width:8px;background:transparent}#transcript::-webkit-scrollbar-track{background:transparent}#transcript::-webkit-scrollbar-thumb{background:#999;border:2px solid transparent;border-radius:5px;background-clip:padding-box}#transcript::-webkit-scrollbar-button{display:none}.message{max-width:82ch;width:100%;min-width:0;margin:0 0 14px;padding:12px 0;color:#fff;white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word;line-height:1.5}.message.user{margin-left:auto;border:0;border-radius:16px;background:#252525;padding:10px 16px;max-width:78ch}.message.assistant{border:0;background:transparent;padding:8px 0;max-width:90ch}.message.error{border:1px solid #b84a4a;border-radius:7px;background:#321414;padding:12px 14px}.label{display:block;margin-bottom:5px;color:#888;font-size:11px;text-transform:uppercase}.message.user .label{color:#bdbdbd}.message.assistant .label{color:#666}.message.assistant h1,.message.assistant h2,.message.assistant h3{margin:10px 0 6px;overflow-wrap:anywhere}.message.assistant code{padding:2px 5px;border-radius:4px;background:#181818;color:#e7e7e7;overflow-wrap:anywhere;word-break:break-word}.codeBlock{position:relative;max-width:100%;min-width:0;overflow:hidden;margin:10px 0;padding:12px;border:1px solid #333;border-radius:7px;background:#101010}.codeBlock pre{margin:0;max-width:100%;white-space:pre-wrap;overflow-x:hidden;overflow-y:auto;overflow-wrap:anywhere;word-break:break-word;padding-right:58px;scrollbar-width:thin;scrollbar-color:#999 transparent}.copyCode{position:absolute;right:4px;top:4px;padding:2px 6px;margin:0;min-height:22px;font-size:11px;line-height:16px;border-radius:4px}.approval{flex:0 0 auto;border:1px solid #b07c32;background:#2a1f08;padding:12px 14px;margin:0 14px 12px;border-radius:7px}.approval button,button{border:1px solid #666;color:#fff;background:#202020;border-radius:6px;padding:7px 11px;cursor:pointer;margin:3px}.approval button:hover,button:hover{background:#333}.activity{margin:8px 0;color:#a9a9a9;overflow-wrap:anywhere}.activity summary{cursor:pointer;list-style:none;overflow-wrap:anywhere}.activityMark{display:inline-block;width:20px;color:#bdbdbd}.activity pre{margin:6px 0 0 20px;padding:8px;max-height:180px;overflow:auto;overflow-wrap:anywhere;word-break:break-word;background:#111;color:#aaa}.composer{flex:0 0 auto;display:grid;grid-template-columns:1fr;gap:8px;padding:8px 14px 0;background:#000;min-width:0}.composer.dragging{outline:1px dashed #888;outline-offset:-4px}#attachments{display:flex;gap:6px;flex-wrap:wrap;min-width:0}.attachment{display:inline-flex;max-width:240px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;padding:5px 9px;border-radius:12px;background:#252525;color:#ddd;font-size:12px}.composer textarea{width:100%;min-height:58px;resize:vertical;padding:9px;border:1px solid #666;border-radius:6px;background:#000;color:#fff}.composerBar{display:flex;gap:8px;align-items:center;min-width:0}.composerBar button{width:34px;height:30px;padding:0;font-size:18px}.composerBar #action{margin-left:auto}.composerBar #mode{color:#888;font-size:11px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.composerBar button:disabled{opacity:.4;cursor:default}input[type=file]{max-width:180px;color:#fff;font-size:11px}#tools{flex:0 0 auto;display:flex;gap:6px;align-items:center;min-height:0;padding:8px 14px 2px;background:#000;min-width:0}#menuItems{display:flex;gap:4px}#menuItems[hidden]{display:none}#status{color:#fff;font-size:11px;margin-left:auto;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .question{border:1px solid #555;background:#111;padding:12px 14px;margin:0 14px 12px;border-radius:7px}.question button{border:1px solid #666;color:#fff;background:#202020;border-radius:6px;padding:7px 11px;cursor:pointer;margin:3px}.changedFiles{margin:12px 0;border:1px solid #333;border-radius:7px;background:#0d0d0d;padding:10px}.changedFiles summary{cursor:pointer;color:#ddd}.changedFile{display:flex;justify-content:space-between;gap:12px;padding:5px 0;border-top:1px solid #222;font-size:12px}.changedFile button{margin:0;padding:3px 7px;font-size:11px}.diff{margin:8px 0;padding:10px;background:#050505;color:#bbb;white-space:pre;overflow:auto;max-height:280px;font:12px Consolas,monospace;scrollbar-width:thin;scrollbar-color:#999 transparent}
        </style></head><body>
        <div id="meta">Agent</div><div id="transcript"></div><div id="approval"></div><div id="question"></div>
        <div id="tools"><button id="menuButton" type="button" title="More">⋯</button><div id="menuItems" hidden><button id="reload" type="button">Reload transcript</button></div><span id="status"></span></div>
        <form class="composer" id="composer"><div id="attachments"></div><textarea id="input" placeholder="随心输入……"></textarea><div class="composerBar"><button id="attach" type="button" title="Add attachment">+</button><span id="mode"></span><button id="stop" type="button" title="Stop" hidden>■</button><button id="action" type="submit" title="Send">↑</button><input id="file" type="file" multiple hidden></div></form>
        <script>
        const state={messages:[],busy:false,approval:null,session:null,activities:[],attachments:[],question:null,changedFiles:[],ui:{}};const $=id=>document.getElementById(id);const send=m=>window.invokeCSharpAction?window.invokeCSharpAction(m):setTimeout(()=>send(m),25);const escapeHtml=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
        function markdown(text){let value=escapeHtml(text);const blocks=[];value=value.replace(/```(?:[a-zA-Z0-9_-]+)?\n?([\s\S]*?)```/g,(_,code)=>{const id=blocks.push(code)-1;return `@@CODE${id}@@`});value=value.replace(/^### (.*)$/gm,'<h3>$1</h3>').replace(/^## (.*)$/gm,'<h2>$1</h2>').replace(/^# (.*)$/gm,'<h1>$1</h1>').replace(/^[-*] (.*)$/gm,'<li>$1</li>').replace(/\*\*(.+?)\*\*/g,'<strong>$1</strong>').replace(/`([^`]+)`/g,'<code>$1</code>').replace(/\n/g,'<br>');return value.replace(/@@CODE(\d+)@@/g,(_,id)=>`<div class="codeBlock"><pre><code>${blocks[id]}</code></pre><button type="button" class="copyCode" data-code="${encodeURIComponent(blocks[id])}">${escapeHtml(state.ui.copy||'Copy')}</button></div>`)}
        function renderActivities(){const root=$('activities');if(!root)return;root.innerHTML='';for(const item of state.activities||[]){const normalized=String(item.title||'').trim().toLowerCase();if(!normalized||['usermessage','assistantmessage','turnstarted','turncompleted','turn/started','turn/completed'].includes(normalized))continue;const row=document.createElement('details');row.className='activity';const summary=document.createElement('summary');summary.innerHTML=`<span class="activityMark">${item.complete?'✓':'▸'}</span><span>${escapeHtml(item.title||state.ui.agentActivity||'Agent activity')}</span>`;row.append(summary);if(item.detail){const detail=document.createElement('pre');detail.textContent=item.detail;row.append(detail)}root.append(row)}}
        function positionActivities(){const host=$('activities');if(!host)return;const assistants=[...document.querySelectorAll('.message.assistant')];const target=assistants[assistants.length-1];if(target)target.before(host)}
        function renderChangedFiles(){const files=state.changedFiles||[];if(!files.length)return '';return `<details class="changedFiles"><summary>${escapeHtml(state.ui.changedFiles||'Changed files')} · ${files.length}</summary>${files.map((file,index)=>`<div class="changedFile"><span>${escapeHtml(file.path)} <small>+${file.added} -${file.removed}</small></span><button type="button" data-diff="${index}">${escapeHtml(state.ui.viewDiff||'View diff')}</button></div>`).join('')}</details>`}
        function renderQuestion(){const root=$('question');if(!state.question){root.replaceChildren();return}root.innerHTML='<div class="question"><strong></strong><p></p><div class="questionOptions"></div></div>';root.querySelector('strong').textContent=state.ui.agentNeedsInput||'Agent needs your input';root.querySelector('p').textContent=state.question.prompt||'';const options=root.querySelector('.questionOptions');for(const option of state.question.options||[]){const b=document.createElement('button');b.textContent=option.label;b.onclick=()=>send({type:'command',command:'respond_question',requestId:state.question.requestId,answers:{question:option.id}});options.append(b)}}
        function render(){const meta=state.session||{},ui=state.ui||{};$('meta').textContent=`${meta.provider||'agent'} · ${meta.model||ui.sessionPending||'session pending'}`;$('menuButton').title=ui.more||'More';$('reload').textContent=ui.reloadTranscript||'Reload transcript';$('attach').title=ui.addAttachment||'Add attachment';$('stop').title=ui.stop||'Stop';$('action').title=ui.send||'Send';$('input').placeholder=ui.inputPlaceholder||$('input').placeholder;$('transcript').innerHTML='';let activitiesRendered=false;for(const m of state.messages){if(m.role==='assistant'&&!activitiesRendered){const activityHost=document.createElement('div');activityHost.id='activities';$('transcript').append(activityHost);renderActivities();activitiesRendered=true}const a=document.createElement('article');a.className='message '+m.role;if(m.role==='assistant')a.innerHTML=markdown(m.text||'');else{a.innerHTML='<span class="label"></span><span class="body"></span>';a.querySelector('.label').textContent=m.role==='user'?(ui.you||'You'):(ui.error||'Error');a.querySelector('.body').textContent=m.text||''}$('transcript').append(a)}if(!activitiesRendered){const activityHost=document.createElement('div');activityHost.id='activities';$('transcript').append(activityHost);renderActivities()}positionActivities();const fileHost=document.createElement('div');fileHost.innerHTML=renderChangedFiles();$('transcript').append(fileHost);$('transcript').scrollTop=$('transcript').scrollHeight;const canSteer=!!state.busy&&!!meta.capabilities?.steer;$('stop').hidden=!state.busy;$('action').disabled=state.busy&&!canSteer||(!state.busy&&!$('input').value.trim()&&!state.attachments.length);$('mode').textContent=state.busy?(canSteer?'Working · type to steer':'Working'):(meta.model||ui.ready||'Ready');$('status').textContent=state.busy?(canSteer?(ui.steerHint||'Enter steers this turn'):(ui.turnRunning||'Turn running')):'';if(state.approval){const root=$('approval');root.innerHTML='<div class="approval"><strong></strong><p></p></div>';root.querySelector('strong').textContent=ui.approvalRequested||'Approval requested';root.querySelector('p').textContent=state.approval.description;for(const option of state.approval.options||[]){const b=document.createElement('button');b.textContent=option.label;b.onclick=()=>send({type:'command',command:'respond_approval',requestId:state.approval.requestId,option:option.id});root.firstChild.append(b)}}else $('approval').replaceChildren();const chips=$('attachments');chips.innerHTML='';for(const item of state.attachments||[]){const chip=document.createElement('span');chip.className='attachment';chip.textContent=item.name;chips.append(chip)}renderQuestion()}
        function submitMessage(){const text=$('input').value.trim();if(state.busy){if(text&&state.session?.capabilities?.steer){send({type:'command',command:'steer',text});$('input').value=''}return}if(!text&&!state.attachments.length)return;send({type:'command',command:'send_turn',text:text||state.ui.inspectAttachments||'Please inspect the attached files.'});$('input').value='';state.attachments=[];render()}
        function addFile(file){if(!file)return;const reader=new FileReader();reader.onload=()=>send({type:'command',command:'attach',file:{name:file.name,size:file.size,type:file.type||'application/octet-stream',dataUrl:String(reader.result)}});reader.readAsDataURL(file)}
        function receive(raw){const m=typeof raw==='string'?JSON.parse(raw):raw;if(m.type==='state'){state.session=m.session;state.ui=m.ui||{};state.messages=m.messages||[];state.busy=!!m.busy;state.approval=m.approval;state.activities=m.activities||[];state.attachments=m.attachments||[];state.question=m.question;state.changedFiles=m.changedFiles||[];render()}else if(m.type==='event'&&m.eventType==='attachment_received'){state.attachments=[...state.attachments,{name:m.payload.name||'attachment',token:m.payload.token}];render()}else if(m.type==='event'&&m.eventType==='error'){state.messages.push({role:'error',text:m.payload.message});render()}}
        window.receiveFromHost=receive;$('composer').onsubmit=e=>{e.preventDefault();submitMessage()};$('input').oninput=()=>render();$('input').onkeydown=e=>{if(e.key==='Enter'&&!e.shiftKey){e.preventDefault();submitMessage()}};$('stop').onclick=()=>send({type:'command',command:'interrupt'});$('attach').onclick=()=>$('file').click();$('file').onchange=e=>{for(const file of e.target.files||[])addFile(file);e.target.value=''};$('menuButton').onclick=()=>{$('menuItems').hidden=!$('menuItems').hidden};$('reload').onclick=()=>send({type:'command',command:'reload_transcript'});$('composer').ondragover=e=>{e.preventDefault();$('composer').classList.add('dragging')};$('composer').ondragleave=()=>$('composer').classList.remove('dragging');$('composer').ondrop=e=>{e.preventDefault();$('composer').classList.remove('dragging');for(const file of e.dataTransfer.files||[])addFile(file)};$('input').onpaste=e=>{for(const item of e.clipboardData.items||[]){if(item.type.startsWith('image/')){e.preventDefault();addFile(item.getAsFile())}}};$('transcript').onclick=e=>{const copy=e.target.closest('.copyCode');if(copy){navigator.clipboard?.writeText(decodeURIComponent(copy.dataset.code||''));copy.textContent=state.ui.copied||'Copied';setTimeout(()=>copy.textContent=state.ui.copy||'Copy',900);return}const diff=e.target.closest('[data-diff]');if(diff){const file=state.changedFiles[Number(diff.dataset.diff)];if(!file)return;const block=document.createElement('pre');block.className='diff';block.textContent=file.diff||'No diff available';diff.parentElement.append(block)}};send({type:'ready'});
        </script></body></html>
        """;
}
