using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.Storage.Settings;

namespace Workbench.App.Services;

public sealed class LocalizationService(WorkbenchSettingsRepository? settings = null) : ObservableObject
{
    private readonly WorkbenchSettingsRepository? _settings = settings;
    private WorkbenchLanguage _language = WorkbenchLanguage.English;

    public WorkbenchLanguage Language => _language;

    public string this[string key] => GetText(_language, key);

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
        _ => key
    };
}
