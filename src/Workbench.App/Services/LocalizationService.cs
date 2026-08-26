using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.Storage.Settings;

namespace Workbench.App.Services;

public sealed class LocalizationService(WorkbenchSettingsRepository? settings = null) : ObservableObject
{
    public static LocalizationService Current { get; } = new();

    private readonly WorkbenchSettingsRepository? _settings = settings;
    private WorkbenchLanguage _language = WorkbenchLanguage.English;

    public WorkbenchLanguage Language => _language;

    public string this[string key] => GetText(_language, key);

    public void AdoptAsCurrent()
    {
        Current._language = _language;
        Current.OnPropertyChanged(nameof(Language));
        Current.OnPropertyChanged("Item[]");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_settings is not null)
        {
            _language = await _settings.GetWorkbenchLanguageAsync(cancellationToken);
        }
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged("Item[]");
    }

    public async Task SetLanguageAsync(WorkbenchLanguage language, CancellationToken cancellationToken = default)
    {
        if (_settings is not null)
        {
            await _settings.SaveWorkbenchLanguageAsync(language, cancellationToken);
        }
        if (_language == language)
        {
            return;
        }

        _language = language;
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged("Item[]");
        AdoptAsCurrent();
    }

    private static string GetText(WorkbenchLanguage language, string key) => (language, key) switch
    {
        (WorkbenchLanguage.SimplifiedChinese, "Home.Title") => "项目主页",
        (WorkbenchLanguage.SimplifiedChinese, "Home.Description") => "打开本地项目，查看当前状态、继续手动工作，并审阅已记录的决策。",
        (WorkbenchLanguage.SimplifiedChinese, "Home.RecentProjects") => "最近项目",
        (WorkbenchLanguage.SimplifiedChinese, "Home.Settings") => "设置",
        (WorkbenchLanguage.SimplifiedChinese, "Home.Empty") => "还没有项目。打开一个本地项目文件夹即可开始。",
        (WorkbenchLanguage.SimplifiedChinese, "Home.Unavailable") => "不可用",
        (WorkbenchLanguage.SimplifiedChinese, "Home.Open") => "打开本地项目文件夹",
        (WorkbenchLanguage.SimplifiedChinese, "Home.Create") => "创建项目",
        (WorkbenchLanguage.SimplifiedChinese, "Home.Opening") => "正在打开项目...",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.Title") => "设置",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.Projects") => "项目",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.Language") => "语言",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.LanguageDescription") => "选择 AI Game Workbench 的显示语言。",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.ApplyLanguage") => "应用语言",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.Rotation") => "Leader 会话轮换",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.RotationDescription") => "选择新的工作日后，何时考虑启用新的 Leader 会话。",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.Automatic") => "自动",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.AutomaticDescription") => "新的工作日后自动开始新的 Leader 会话",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.AskFirst") => "先询问",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.AskFirstDescription") => "开始新会话前先询问",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.ManualOnly") => "仅手动",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.ManualOnlyDescription") => "仅在你选择时开始新会话",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.Agents") => "Agent 运行时",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.AgentsDescription") => "手动工作不会启动 Agent。只启用你希望 Workbench 使用的 Agent。",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.EnableCodex") => "启用 Codex",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.CodexPath") => "Codex 可执行文件路径（可选）",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.EnableOpenCode") => "启用 OpenCode",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.OpenCodePath") => "OpenCode 可执行文件路径（可选）",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.AgentDescription") => "API Key、Provider 路由、模型和 Provider 权限保留在所选 Agent 自己的账户或配置中。Workbench 的项目状态不依赖这些凭据。",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.SaveAgents") => "保存 Agent 设置",
        (WorkbenchLanguage.SimplifiedChinese, "Settings.AgentSaved") => "已保存。只有项目需要时才会连接已启用的 Agent。",
        (WorkbenchLanguage.SimplifiedChinese, "Workspace.ResetLayout") => "重置布局",
        (WorkbenchLanguage.SimplifiedChinese, "Workspace.Projects") => "项目",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.Title") => "手动工作",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.SignedIn") => "登录身份：",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.Context") => "工作上下文",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.ActingAs") => "当前角色：",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.RecordHandoff") => "记录交接",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.Review") => "审核工作结果",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.RecordHeading") => "记录交接",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.RecordDescription") => "记录工作结果供审核。这不会发布项目变更。",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.PrimaryResult") => "主要结果",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.PrimaryPlaceholder") => "这次工作尝试产生了什么？",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.Validations") => "验证（每行一条）",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.Unresolved") => "未解决问题（每行一条）",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.ProposedChanges") => "拟议项目变更（每行一条）",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.ProposedRevision") => "拟议任务修订（可选）",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.Evidence") => "证据引用（每行一条）",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.BeginNote") => "开始工作只记录一个继续点，不会启动外部工具、发布项目事实或改变当前项目状态。",
        (WorkbenchLanguage.SimplifiedChinese, "Manual.Back") => "返回项目世界",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.Title") => "审核工作结果",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.SubmittedAs") => "提交身份：",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.DecidingAs") => "决策身份：",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.Primary") => "主要结果",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.ProposedChange") => "拟议项目变更",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.Decision") => "决策",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.Contribution") => "拟议贡献处理",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.Preview") => "预览将发生的变更",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.Confirm") => "确认决策",
        (WorkbenchLanguage.SimplifiedChinese, "Decision.Back") => "返回手动工作",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.Title") => "设置此项目",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.Step1") => "步骤 1 · 确认项目归属",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.Confirm") => "确认项目设置",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.Step2") => "步骤 2 · 描述第一项职责",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.SignedIn") => "登录身份",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.Role") => "当前角色（仅描述）",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.Responsibility") => "职责",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.Expected") => "预期结果",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.FirstAssignment") => "第一项任务",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.Preview") => "预览将记录的内容",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.ConfirmOpen") => "确认并打开项目",
        (WorkbenchLanguage.SimplifiedChinese, "ProjectSetup.Back") => "返回项目主页",
        (WorkbenchLanguage.SimplifiedChinese, "Library.Title") => "项目库",
        (WorkbenchLanguage.SimplifiedChinese, "Library.Overview") => "概览",
        (WorkbenchLanguage.SimplifiedChinese, "Library.Category") => "分类",
        (WorkbenchLanguage.SimplifiedChinese, "Library.Time") => "时间",
        (WorkbenchLanguage.SimplifiedChinese, "Library.Project") => "项目",
        (WorkbenchLanguage.SimplifiedChinese, "Library.Focus") => "聚焦",
        (WorkbenchLanguage.SimplifiedChinese, "Library.Accept") => "接受",
        (WorkbenchLanguage.SimplifiedChinese, "Library.Reject") => "拒绝",
        (WorkbenchLanguage.SimplifiedChinese, "Library.EditAccept") => "编辑并接受",
        (WorkbenchLanguage.SimplifiedChinese, "Library.NoEntries") => "项目库中还没有整理好的条目。",
        (WorkbenchLanguage.SimplifiedChinese, "Library.Decisions") => "没有对象映射的项目决策",
        (_, "Home.Title") => "Project Home",
        (_, "Home.Description") => "Open a local project to see its current state, continue manual work, and review recorded decisions.",
        (_, "Home.RecentProjects") => "Recent projects",
        (_, "Home.Settings") => "Settings",
        (_, "Home.Empty") => "No projects yet. Open a local project folder to begin.",
        (_, "Home.Unavailable") => "Unavailable",
        (_, "Home.Open") => "Open local project folder",
        (_, "Home.Create") => "Create Project",
        (_, "Home.Opening") => "Opening project...",
        (_, "Settings.Title") => "Settings",
        (_, "Settings.Projects") => "Projects",
        (_, "Settings.Language") => "Language",
        (_, "Settings.LanguageDescription") => "Choose the display language for AI Game Workbench.",
        (_, "Settings.ApplyLanguage") => "Apply Language",
        (_, "Settings.Rotation") => "Leader Session Rotation",
        (_, "Settings.RotationDescription") => "Choose when a fresh Leader session should be considered after a new workday.",
        (_, "Settings.Automatic") => "Automatic",
        (_, "Settings.AutomaticDescription") => "Automatically start a fresh Leader session after a new workday",
        (_, "Settings.AskFirst") => "Ask first",
        (_, "Settings.AskFirstDescription") => "Ask before starting a fresh session",
        (_, "Settings.ManualOnly") => "Manual only",
        (_, "Settings.ManualOnlyDescription") => "Only start a fresh session when you choose to",
        (_, "Settings.Agents") => "Agent Runtimes",
        (_, "Settings.AgentsDescription") => "Manual work does not start an Agent. Enable only the Agents you want Workbench to use.",
        (_, "Settings.EnableCodex") => "Enable Codex",
        (_, "Settings.CodexPath") => "Codex executable path (optional)",
        (_, "Settings.EnableOpenCode") => "Enable OpenCode",
        (_, "Settings.OpenCodePath") => "OpenCode executable path (optional)",
        (_, "Settings.AgentDescription") => "API keys, provider routing, models, and provider permissions remain in the selected Agent's native account/configuration. Workbench keeps project state independent of those credentials.",
        (_, "Settings.SaveAgents") => "Save Agent Settings",
        (_, "Settings.AgentSaved") => "Saved. Enabled Agents connect only when a project needs them.",
        (_, "Workspace.ResetLayout") => "Reset Layout",
        (_, "Workspace.Projects") => "Projects",
        (_, "Manual.Title") => "MANUAL WORK",
        (_, "Manual.SignedIn") => "Signed in as:",
        (_, "Manual.Context") => "WORK CONTEXT",
        (_, "Manual.ActingAs") => "Acting as:",
        (_, "Manual.RecordHandoff") => "Record Handoff",
        (_, "Manual.Review") => "Review Work Result",
        (_, "Manual.RecordHeading") => "RECORD HANDOFF",
        (_, "Manual.RecordDescription") => "This records a work result for review. It does not publish a project change.",
        (_, "Manual.PrimaryResult") => "Primary Result",
        (_, "Manual.PrimaryPlaceholder") => "What did this work attempt produce?",
        (_, "Manual.Validations") => "Validations · one per line",
        (_, "Manual.Unresolved") => "Unresolved Issues · one per line",
        (_, "Manual.ProposedChanges") => "Proposed Project Changes · one per line",
        (_, "Manual.ProposedRevision") => "Proposed Assignment Revision · optional",
        (_, "Manual.Evidence") => "Evidence References · one per line",
        (_, "Manual.BeginNote") => "Beginning work records a continuation point only. It does not start an external tool, publish project facts, or change the current project state.",
        (_, "Manual.Back") => "Back to Project World",
        (_, "Decision.Title") => "REVIEW WORK RESULT",
        (_, "Decision.SubmittedAs") => "Submitted as:",
        (_, "Decision.DecidingAs") => "Deciding as:",
        (_, "Decision.Primary") => "PRIMARY RESULT",
        (_, "Decision.ProposedChange") => "PROPOSED PROJECT CHANGE",
        (_, "Decision.Decision") => "Decision",
        (_, "Decision.Contribution") => "Proposed contribution handling",
        (_, "Decision.Preview") => "Preview what will change",
        (_, "Decision.Confirm") => "Confirm Decision",
        (_, "Decision.Back") => "Back to Manual Work",
        (_, "ProjectSetup.Title") => "Set up this project",
        (_, "ProjectSetup.Step1") => "Step 1 · Confirm project ownership",
        (_, "ProjectSetup.Confirm") => "Confirm project setup",
        (_, "ProjectSetup.Step2") => "Step 2 · Describe the first responsibility",
        (_, "ProjectSetup.SignedIn") => "Signed in as",
        (_, "ProjectSetup.Role") => "Acting role (descriptive only)",
        (_, "ProjectSetup.Responsibility") => "Responsibility",
        (_, "ProjectSetup.Expected") => "Expected outcome",
        (_, "ProjectSetup.FirstAssignment") => "First assignment",
        (_, "ProjectSetup.Preview") => "Preview what will be recorded",
        (_, "ProjectSetup.ConfirmOpen") => "Confirm and open project",
        (_, "ProjectSetup.Back") => "Back to Project Home",
        (_, "Library.Title") => "PROJECT LIBRARY",
        (_, "Library.Overview") => "Overview",
        (_, "Library.Category") => "Category",
        (_, "Library.Time") => "Time",
        (_, "Library.Project") => "Project",
        (_, "Library.Focus") => "Focus",
        (_, "Library.Accept") => "Accept",
        (_, "Library.Reject") => "Reject",
        (_, "Library.EditAccept") => "Edit + Accept",
        (_, "Library.NoEntries") => "Library has no organized entries yet.",
        (_, "Library.Decisions") => "PROJECT DECISIONS WITHOUT OBJECT MAPPING",
        _ => key
    };
}
