using Workbench.App.Services;
using Workbench.App.ViewModels.Leader;
using Workbench.Runtime.Agents;
using Workbench.Storage.Settings;

namespace Workbench.App.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizationSerialCollection
{
    public const string Name = "Localization serial";
}

[Collection(LocalizationSerialCollection.Name)]
public sealed class ApprovalOptionLocalizationTests
{
    [Fact]
    public async Task Standard_approval_options_follow_the_workbench_language()
    {
        await LocalizationService.Current.SetLanguageAsync(WorkbenchLanguage.SimplifiedChinese);
        try
        {
            var options = new[]
            {
                CreateOption("approve-once", "Approve once"),
                CreateOption("approve-session", "Approve for session"),
                CreateOption("decline", "Decline"),
                CreateOption("cancel", "Cancel turn")
            };

            Assert.Equal(
                ["批准一次", "本次会话始终批准", "拒绝", "取消任务"],
                options.Select(option => option.Label));
        }
        finally
        {
            await LocalizationService.Current.SetLanguageAsync(WorkbenchLanguage.English);
        }
    }

    private static LeaderApprovalOptionViewModel CreateOption(string id, string label) =>
        new(new AgentApprovalOption(id, label), _ => Task.CompletedTask);
}
