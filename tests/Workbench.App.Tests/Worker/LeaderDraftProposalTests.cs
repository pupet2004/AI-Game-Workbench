using Workbench.App.Leader;
using Workbench.App.ViewModels;
using Workbench.App.ViewModels.Leader;
using Workbench.Core.Tasks;
using Workbench.Storage.Database;
using Workbench.Storage.Tasks;
using Workbench.Core.Projects;
using Workbench.Storage.Projects;
using CoreProject = Workbench.Core.Projects.Project;
using Workbench.App.Tests.Support;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Workbench.Storage.Memory;
using Workbench.Core.Continuity;

namespace Workbench.App.Tests.Worker;

public sealed class LeaderDraftProposalTests
{
    [Fact]
    public void Strict_leader_schema_requires_every_top_level_property_and_authority_confirmation_is_nullable()
    {
        using var document = JsonDocument.Parse(LeaderResponseSchema.Json);
        var root = document.RootElement;
        var properties = root.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var required = root.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);

        Assert.True(properties.SetEquals(required), $"Missing required properties: {string.Join(", ", properties.Except(required))}");

        var authority = root.GetProperty("properties").GetProperty("authority_confirmation");
        var branches = authority.GetProperty("anyOf").EnumerateArray().ToArray();
        Assert.Contains(branches, branch => branch.GetProperty("type").GetString() == "null");
    }

    [Fact]
    public void Normal_leader_result_explicitly_uses_null_authority_confirmation()
    {
        var json = "{\"response\":\"普通回答\",\"draft_proposal\":null,\"memory_commands\":null,\"authority_confirmation\":null,\"summary_deltas\":null,\"evolution_candidates\":[]}";

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));
        Assert.Null(parsed.AuthorityConfirmation);
    }

    [Fact]
    public void Authority_confirmation_parser_removes_pending_projection_language()
    {
        const string json = """
            {
              "response": "请确认。",
              "draft_proposal": null,
              "memory_commands": null,
              "authority_confirmation": {
                "title": "因果编号职责边界",
                "contributions": [{
                  "statement": "因果编号只负责标识和追踪因果链。该规则拟作为正式世界规则，待用户确认后才进入 Accepted Project State。"
                }]
              },
              "summary_deltas": null,
              "evolution_candidates": []
            }
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));
        Assert.Equal(
            "因果编号只负责标识和追踪因果链。",
            parsed.AuthorityConfirmation!.Statements.Single());
    }

    [Fact]
    public void Evolution_candidate_schema_is_closed_bounded_and_nullable_only_at_before_after()
    {
        using var document = JsonDocument.Parse(LeaderResponseSchema.Json);
        var candidates = document.RootElement.GetProperty("properties").GetProperty("evolution_candidates");

        Assert.Equal("array", candidates.GetProperty("type").GetString());
        Assert.Equal(3, candidates.GetProperty("maxItems").GetInt32());
        var item = candidates.GetProperty("items");
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["object", "object_kind", "change_type", "before", "after", "impact_class", "route_hint", "reason", "source_ref"],
            item.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains(item.GetProperty("properties").GetProperty("before").GetProperty("type").EnumerateArray(), value => value.GetString() == "null");
        Assert.Contains(item.GetProperty("properties").GetProperty("after").GetProperty("type").EnumerateArray(), value => value.GetString() == "null");
        Assert.Equal(
            ["WorldRule", "ProjectStructure", "CharacterOrObject", "Content", "Architecture", "Unclassified"],
            item.GetProperty("properties").GetProperty("impact_class").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            ["AuthorityConfirmation", "LibraryProposal", "NoGovernance", "Unclassified"],
            item.GetProperty("properties").GetProperty("route_hint").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void Evolution_candidate_parses_as_read_only_sidecar()
    {
        var json = """
            {
              "response": "已识别一项可能影响未来工作的变化。",
              "draft_proposal": null,
              "memory_commands": null,
              "authority_confirmation": null,
              "summary_deltas": null,
            "evolution_candidates": [{
                "object": "无限计算器",
                "object_kind": "WorldRule",
                "change_type": "ConstraintRevision",
                "before": "可能控制人的时间行为",
                "after": "只预测时空稳定性风险，不控制人的思想",
                "impact_class": "WorldRule",
                "route_hint": "AuthorityConfirmation",
                "reason": "用户明确改变了世界规则边界",
                "source_ref": "current_user_message"
              }]
            }
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));

        var candidate = Assert.Single(parsed.EvolutionCandidates);
        Assert.Equal("无限计算器", candidate.Object);
        Assert.Equal("WorldRule", candidate.ObjectKind);
        Assert.Equal("ConstraintRevision", candidate.ChangeType);
        Assert.Equal("可能控制人的时间行为", candidate.Before);
        Assert.Equal("只预测时空稳定性风险，不控制人的思想", candidate.After);
        Assert.Equal(LeaderEvolutionImpactClass.WorldRule, candidate.ImpactClass);
        Assert.Equal(LeaderEvolutionRouteHint.AuthorityConfirmation, candidate.RouteHint);
        Assert.Equal("current_user_message", candidate.SourceRef);
        Assert.Null(parsed.Proposal);
        Assert.Null(parsed.MemoryCommands);
        Assert.Null(parsed.AuthorityConfirmation);
    }

    [Fact]
    public void Parser_keeps_backward_compatibility_when_candidate_field_is_absent()
    {
        const string json = "{\"response\":\"legacy envelope\",\"draft_proposal\":null,\"memory_commands\":null,\"authority_confirmation\":null,\"summary_deltas\":null}";

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));
        Assert.Empty(parsed.EvolutionCandidates);
    }

    [Fact]
    public void More_than_three_evolution_candidates_is_rejected()
    {
        const string item = "{\"object\":\"x\",\"object_kind\":\"Rule\",\"change_type\":\"Revision\",\"before\":null,\"after\":\"y\",\"impact_class\":\"Content\",\"route_hint\":\"LibraryProposal\",\"reason\":\"explicit change\",\"source_ref\":\"current_user_message\"}";
        var json = $"{{\"response\":\"too many\",\"draft_proposal\":null,\"memory_commands\":null,\"authority_confirmation\":null,\"summary_deltas\":null,\"evolution_candidates\":[{item},{item},{item},{item}]}}";

        Assert.False(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out _));
    }

    [Fact]
    public void Unknown_evolution_impact_or_route_is_rejected()
    {
        const string json = "{\"response\":\"candidate\",\"draft_proposal\":null,\"memory_commands\":null,\"authority_confirmation\":null,\"summary_deltas\":null,\"evolution_candidates\":[{\"object\":\"x\",\"object_kind\":\"Rule\",\"change_type\":\"Revision\",\"before\":null,\"after\":\"y\",\"impact_class\":\"MaybeImportant\",\"route_hint\":\"Anything\",\"reason\":\"explicit change\",\"source_ref\":\"current_user_message\"}]}";

        Assert.False(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out _));
    }

    [Fact]
    public void Draft_proposal_and_authority_confirmation_cannot_both_be_present()
    {
        var json = """
            {
              "response": "不能同时出现。",
              "draft_proposal": {
                "title": "Worker task",
                "goal": "Do work",
                "scope": "bounded",
                "outOfScope": "authority",
                "acceptance": ["done"],
                "riskLevel": "Low",
                "recommendedExecutionProfile": {"providerHint": null, "modelHint": null, "runtimeHint": null}
              },
              "memory_commands": null,
              "authority_confirmation": {"title": "Authority", "contributions": [{"statement": "fact"}]},
              "summary_deltas": null
            }
            """;

        Assert.False(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out _));
    }

    [Fact]
    public void Leader_schema_closes_every_object_branch_for_codex_nullable_validation()
    {
        using var document = JsonDocument.Parse(LeaderResponseSchema.Json);
        var draft = document.RootElement.GetProperty("properties").GetProperty("draft_proposal");
        var objectBranch = draft.GetProperty("anyOf").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "object");
        Assert.False(objectBranch.GetProperty("additionalProperties").GetBoolean());
        var profile = objectBranch.GetProperty("properties").GetProperty("recommendedExecutionProfile");
        Assert.False(profile.GetProperty("additionalProperties").GetBoolean());
        var memory = document.RootElement.GetProperty("properties").GetProperty("memory_commands");
        var memoryObject = memory.GetProperty("anyOf").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "object");
        Assert.False(memoryObject.GetProperty("additionalProperties").GetBoolean());
        var library = memoryObject.GetProperty("properties").GetProperty("library_proposal");
        var libraryObject = library.GetProperty("anyOf").EnumerateArray().Single(item => item.GetProperty("type").GetString() == "object");
        Assert.False(libraryObject.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("occurred_at", libraryObject.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("memory_commands", document.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void Schema_requires_nullable_summary_deltas_under_existing_strict_convention()
    {
        using var document = JsonDocument.Parse(LeaderResponseSchema.Json);
        var root = document.RootElement;
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("summary_deltas", root.GetProperty("required").EnumerateArray().Select(item => item.GetString()));

        var summary = root.GetProperty("properties").GetProperty("summary_deltas");
        var branches = summary.GetProperty("anyOf").EnumerateArray().ToArray();
        Assert.Contains(branches, branch => branch.GetProperty("type").GetString() == "null");
        var array = Assert.Single(branches, branch => branch.GetProperty("type").GetString() == "array");
        var item = array.GetProperty("items");
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["occurred_at", "kind", "text", "source_refs"],
            item.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        var source = item.GetProperty("properties").GetProperty("source_refs").GetProperty("items");
        Assert.False(source.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["source_kind", "source_locator"],
            source.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void Valid_summary_sidecar_parses_without_polluting_visible_response()
    {
        var json = """
            {"response":"Visible answer only.","draft_proposal":null,"memory_commands":null,"summary_deltas":[{"occurred_at":"2026-08-20T10:15:30.0000000+00:00","kind":"Decision","text":"Keep the same cognition envelope.","source_refs":[{"source_kind":"LeaderMessage","source_locator":"message-42"}]}]}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));

        Assert.Equal("Visible answer only.", parsed.Response);
        var delta = Assert.Single(parsed.SummaryDeltas);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 10, 15, 30, TimeSpan.Zero), delta.OccurredAt);
        Assert.Equal(SummaryDeltaKind.Decision, delta.Kind);
        Assert.Equal("Keep the same cognition envelope.", delta.Text);
        var source = Assert.Single(delta.SourceRefs);
        Assert.Equal("LeaderMessage", source.SourceKind);
        Assert.Equal("message-42", source.SourceLocator);
        Assert.Null(parsed.SummaryDeltaError);
    }

    [Fact]
    public void Fenced_structured_response_is_unwrapped_before_parsing()
    {
        var json = """
            ```json
            {"response":"Visible.","draft_proposal":null,"memory_commands":null,"summary_deltas":null}
            ```
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));
        Assert.Equal("Visible.", parsed.Response);
        Assert.Empty(parsed.SummaryDeltas);
    }

    [Fact]
    public void Consecutive_structured_envelopes_use_the_last_complete_response()
    {
        var json = """
            {"response":"Intermediate audit progress.","draft_proposal":null,"memory_commands":null,"summary_deltas":null}{"response":"Final audit conclusion.","draft_proposal":null,"memory_commands":null,"summary_deltas":null}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));
        Assert.Equal("Final audit conclusion.", parsed.Response);
        Assert.Empty(parsed.SummaryDeltas);
    }

    [Fact]
    public void Non_protocol_text_between_envelopes_is_rejected()
    {
        var json = """
            {"response":"First.","draft_proposal":null,"memory_commands":null,"summary_deltas":null}
            progress text
            {"response":"Second.","draft_proposal":null,"memory_commands":null,"summary_deltas":null}
            """;

        Assert.False(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out _));
    }

    [Fact]
    public void Null_summary_sidecar_is_normal_no_summary()
    {
        const string json = "{\"response\":\"No durable rationale.\",\"draft_proposal\":null,\"memory_commands\":null,\"summary_deltas\":null}";

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));

        Assert.Empty(parsed.SummaryDeltas);
        Assert.Null(parsed.SummaryDeltaError);
    }

    [Theory]
    [InlineData("2026-08-20T10:15:30Z")]
    [InlineData("2026-08-20T10:15:30+00:00")]
    [InlineData("2026-08-20T10:15:30.123Z")]
    [InlineData("2026-08-20T10:15:30.1234567+00:00")]
    public void Iso_timestamp_variants_parse_with_an_explicit_offset(string occurredAt)
    {
        var json = $$"""
            {"response":"Visible.","draft_proposal":null,"memory_commands":null,"summary_deltas":[{"occurred_at":"{{occurredAt}}","kind":"Change","text":"Valid timestamp.","source_refs":[]}]}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));

        Assert.Single(parsed.SummaryDeltas);
        Assert.Null(parsed.SummaryDeltaError);
    }

    [Theory]
    [InlineData("Fact", "Valid text")]
    [InlineData("Decision", " ")]
    public void Invalid_kind_or_blank_text_discards_only_summary_sidecar(string kind, string text)
    {
        var json = $$"""
            {"response":"Core remains visible.","draft_proposal":null,"memory_commands":null,"summary_deltas":[{"occurred_at":"2026-08-20T10:15:30.0000000+00:00","kind":"{{kind}}","text":"{{text}}","source_refs":[]}]}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));

        Assert.Equal("Core remains visible.", parsed.Response);
        Assert.Empty(parsed.SummaryDeltas);
        Assert.NotNull(parsed.SummaryDeltaError);
    }

    [Theory]
    [InlineData(" ", "message-42")]
    [InlineData("LeaderMessage", " ")]
    public void Blank_summary_source_fields_discard_the_sidecar(string sourceKind, string sourceLocator)
    {
        var json = $$"""
            {"response":"Core remains visible.","draft_proposal":null,"memory_commands":null,"summary_deltas":[{"occurred_at":"2026-08-20T10:15:30.0000000+00:00","kind":"Decision","text":"Valid text","source_refs":[{"source_kind":"{{sourceKind}}","source_locator":"{{sourceLocator}}"}]}]}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));

        Assert.Equal("Core remains visible.", parsed.Response);
        Assert.Empty(parsed.SummaryDeltas);
        Assert.NotNull(parsed.SummaryDeltaError);
    }

    [Fact]
    public void Invalid_later_summary_item_discards_the_entire_sidecar()
    {
        var json = """
            {"response":"Core remains visible.","draft_proposal":null,"memory_commands":null,"summary_deltas":[{"occurred_at":"2026-08-20T10:15:30.0000000+00:00","kind":"Decision","text":"Valid first item","source_refs":[]},{"occurred_at":"2026-08-20T10:16:30.0000000+00:00","kind":"Fact","text":"Invalid later item","source_refs":[]}]}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));

        Assert.Equal("Core remains visible.", parsed.Response);
        Assert.Empty(parsed.SummaryDeltas);
        Assert.NotNull(parsed.SummaryDeltaError);
    }

    [Fact]
    public void Malformed_summary_item_keeps_core_response_and_proposal()
    {
        var projectId = Guid.NewGuid();
        var json = """
            {"response":"Draft remains available.","draft_proposal":{"title":"Title","goal":"goal","scope":"scope","outOfScope":"out","acceptance":["accept"],"riskLevel":"Low","recommendedExecutionProfile":{"providerHint":null,"modelHint":null,"runtimeHint":null}},"memory_commands":null,"summary_deltas":[{"occurred_at":"not-a-timestamp","kind":"Decision","text":"Bad timestamp","source_refs":[]}]}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, projectId, out var parsed));

        Assert.Equal("Draft remains available.", parsed.Response);
        Assert.Equal("Title", parsed.Proposal!.Title);
        Assert.Empty(parsed.SummaryDeltas);
        Assert.NotNull(parsed.SummaryDeltaError);
    }

    [Fact]
    public void Malformed_summary_item_keeps_valid_memory_command()
    {
        var json = """
            {"response":"Memory remains available.","draft_proposal":null,"memory_commands":{"daily_summary":null,"library_proposal":{"action":"CreateNode","target_object_id":null,"target_node_id":null,"expected_node_revision":null,"expected_overview_revision":0,"category":"Design","topic":"Relics","local_date":"2026-08-14","node_content":"Mechanism is implemented.","current_overview":null,"materials":[]}},"summary_deltas":[{"occurred_at":"not-a-timestamp","kind":"Decision","text":"Bad timestamp","source_refs":[]}]}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, Guid.NewGuid(), out var parsed));

        Assert.Equal("Memory remains available.", parsed.Response);
        Assert.Null(parsed.MemoryCommandError);
        Assert.Equal("Relics", parsed.MemoryCommands!.LibraryProposal!.Topic);
        Assert.Empty(parsed.SummaryDeltas);
        Assert.NotNull(parsed.SummaryDeltaError);
    }

    [Fact]
    public void Existing_structured_fields_keep_current_behavior()
    {
        var projectId = Guid.NewGuid();
        var json = """
            {"response":"Existing fields remain.","draft_proposal":{"title":"Title","goal":"goal","scope":"scope","outOfScope":"out","acceptance":["accept"],"riskLevel":"Medium","recommendedExecutionProfile":{"providerHint":"provider","modelHint":"model","runtimeHint":"runtime"}},"memory_commands":null,"summary_deltas":null}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, projectId, out var parsed));

        Assert.Equal("Existing fields remain.", parsed.Response);
        Assert.Equal(projectId, parsed.Proposal!.ProjectId);
        Assert.Equal(TaskRiskLevel.Medium, parsed.Proposal.RiskLevel);
        Assert.Null(parsed.MemoryCommands);
        Assert.Null(parsed.MemoryCommandError);
    }
    [Fact]
    public async Task Leader_turn_request_carries_output_schema_but_worker_request_does_not()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry(); registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "{\"response\":\"ok\",\"draft_proposal\":null,\"memory_commands\":null,\"summary_deltas\":null}", null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "ordinary";
        await workspace.LeaderPane.SendAsync();
        Assert.NotNull(runtime.SentRequests.Single().OutputSchema);
        Assert.Contains("draft_proposal", runtime.SentRequests.Single().OutputSchema!, StringComparison.Ordinal);
        Assert.Contains("memory_commands", runtime.SentRequests.Single().OutputSchema!, StringComparison.Ordinal);
        Assert.Contains("evolution_candidates", runtime.SentRequests.Single().OutputSchema!, StringComparison.Ordinal);

        Assert.DoesNotContain("outputSchema", new AgentRequest("worker").Text, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Ordinary_structured_response_creates_no_draft_or_execution_side_effect()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
                "{\"response\":\"The project currently has no active worker.\",\"draft_proposal\":null,\"memory_commands\":null,\"summary_deltas\":null}", null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "Explain the current project state.";

        await workspace.LeaderPane.SendAsync();

        Assert.Equal("The project currently has no active worker.", workspace.LeaderPane.Messages.Last().Text);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        await using var connection = context.Services.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM worker_executions WHERE project_id = $projectId";
        command.Parameters.AddWithValue("$projectId", workspace.Result.Project.Id.ToString());
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        Assert.Single(runtime.CreatedSessions);
    }

    [Fact]
    public async Task Evolution_candidate_is_exposed_without_task_library_authority_or_worker_side_effects()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
                """
                {
                  "response": "检测到一项变化候选。",
                  "draft_proposal": null,
                  "memory_commands": null,
                  "authority_confirmation": null,
                  "summary_deltas": null,
                  "evolution_candidates": [{
                    "object": "无限计算器",
                    "object_kind": "WorldRule",
                    "change_type": "ConstraintRevision",
                    "before": "可能控制人的时间行为",
                    "after": "只预测时空稳定性风险，不控制人的思想",
                    "impact_class": "WorldRule",
                    "route_hint": "AuthorityConfirmation",
                    "reason": "用户明确改变了世界规则边界",
                    "source_ref": "current_user_message"
                  }]
                }
                """, null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "以后无限计算器只预测风险，不控制思想。";

        await workspace.LeaderPane.SendAsync();

        var candidate = Assert.Single(workspace.LeaderPane.EvolutionCandidates);
        Assert.Equal("无限计算器", candidate.Object);
        Assert.True(workspace.LeaderPane.HasEvolutionCandidates);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Empty(await context.Services.WorkerExecutionRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        await using var connection = context.Services.Database.CreateConnection();
        await connection.OpenAsync();
        var authorityCount = connection.CreateCommand();
        authorityCount.CommandText = "SELECT COUNT(*) FROM b1_authority_decisions WHERE project_id = $projectId";
        authorityCount.Parameters.AddWithValue("$projectId", workspace.Result.Project.Id.ToString());
        Assert.Equal(0L, (long)(await authorityCount.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Evolution_candidate_turn_does_not_materialize_a_same_turn_authority_confirmation()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
                """
                {
                  "response": "检测到变化候选。",
                  "draft_proposal": null,
                  "memory_commands": null,
                  "authority_confirmation": {
                    "title": "误路由的 Authority 草稿",
                    "contributions": [{"statement": "林砚改为调查员。"}]
                  },
                  "summary_deltas": null,
                  "evolution_candidates": [{
                    "object": "林砚",
                    "object_kind": "Character",
                    "change_type": "RoleRevision",
                    "before": "审核员",
                    "after": "调查员",
                    "impact_class": "CharacterOrObject",
                    "route_hint": "AuthorityConfirmation",
                    "reason": "用户明确修改角色设定",
                    "source_ref": "current_user_message"
                  }]
                }
                """, null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "以后把林砚的职业从审核员改成时间异常调查员。";

        await workspace.LeaderPane.SendAsync();

        Assert.Single(workspace.LeaderPane.EvolutionCandidates);
        Assert.Null(workspace.LeaderPane.PendingAuthorityConfirmation);
        Assert.Contains("治理草稿需要在后续明确请求中单独生成", workspace.LeaderPane.MemoryCommandStatus, StringComparison.Ordinal);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        await using var connection = context.Services.Database.CreateConnection();
        await connection.OpenAsync();
        var authorityCount = connection.CreateCommand();
        authorityCount.CommandText = "SELECT COUNT(*) FROM b1_authority_decisions WHERE project_id = $projectId";
        authorityCount.Parameters.AddWithValue("$projectId", workspace.Result.Project.Id.ToString());
        Assert.Equal(0L, (long)(await authorityCount.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Evolution_candidate_route_suggestion_prepares_ephemeral_draft_without_persistence()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
                """
                {
                  "response": "变化已分类。",
                  "draft_proposal": null,
                  "memory_commands": null,
                  "authority_confirmation": null,
                  "summary_deltas": null,
                  "evolution_candidates": [{
                    "object": "无限计算器",
                    "object_kind": "WorldRule",
                    "change_type": "ConstraintRevision",
                    "before": "可能控制人的时间行为",
                    "after": "只预测时空稳定性风险，不控制人的思想",
                    "impact_class": "WorldRule",
                    "route_hint": "AuthorityConfirmation",
                    "reason": "用户明确改变了世界规则边界",
                    "source_ref": "current_user_message"
                  }]
                }
                """, null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "以后无限计算器只预测风险，不控制思想。";

        await workspace.LeaderPane.SendAsync();

        var suggestion = Assert.Single(workspace.LeaderPane.GovernanceSuggestions);
        Assert.True(suggestion.CanPrepareDraft);
        workspace.LeaderPane.PrepareGovernanceDraftCommand.Execute(suggestion);

        Assert.True(workspace.LeaderPane.HasPreparedGovernanceDraft);
        Assert.NotNull(workspace.LeaderPane.PreparedGovernanceDraft!.AuthorityConfirmation);
        Assert.Null(workspace.LeaderPane.PreparedGovernanceDraft.LibraryProposal);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Empty(await context.Services.WorkerExecutionRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
    }

    [Fact]
    public async Task Evolution_candidate_and_execution_proposal_in_one_turn_do_not_materialize_worker_draft()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
                """
                {
                  "response": "检测到变化并提出治理建议。",
                  "draft_proposal": {
                    "title": "扩充第一章",
                    "goal": "扩充时间礼仪课",
                    "scope": "chapter-01.md",
                    "outOfScope": "其他章节",
                    "acceptance": ["完成"],
                    "riskLevel": "Low",
                    "recommendedExecutionProfile": {"providerHint": null, "modelHint": null, "runtimeHint": null}
                  },
                  "memory_commands": null,
                  "authority_confirmation": null,
                  "summary_deltas": null,
                  "evolution_candidates": [{
                    "object": "时间礼仪课",
                    "object_kind": "Content",
                    "change_type": "ContentRevision",
                    "before": "内容偏少",
                    "after": "需要扩充",
                    "impact_class": "Content",
                    "route_hint": "LibraryProposal",
                    "reason": "用户指出内容不足",
                    "source_ref": "current_user_message"
                  }]
                }
                """, null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "第一章这里感觉时间礼仪课写得有点少。";

        await workspace.LeaderPane.SendAsync();

        Assert.Single(workspace.LeaderPane.EvolutionCandidates);
        Assert.Null(workspace.LeaderPane.DraftConfirmation);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
    }
    [Fact]
    public void Structured_envelope_parses_without_provider_specific_fields()
    {
        var projectId = Guid.NewGuid();
        var json = "{\"response\":\"I drafted this task.\",\"draft_proposal\":{\"title\":\"Title\",\"goal\":\"goal\",\"scope\":\"scope\",\"outOfScope\":\"out\",\"acceptance\":[\"accept\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerHint\":\"Codex\",\"modelHint\":\"GPT-5.6-Sol\",\"runtimeHint\":\"codex-app-server\"}},\"memory_commands\":null,\"summary_deltas\":null}";
        Assert.True(LeaderStructuredResponse.TryParse(json, projectId, out var parsed));
        Assert.Equal("I drafted this task.", parsed.Response);
        Assert.Equal(projectId, parsed.Proposal!.ProjectId);
        Assert.Equal("Codex", parsed.Proposal.Recommendation.ProviderHint);
    }

    [Fact]
    public void Structured_library_command_parses_as_provider_neutral_data()
    {
        var projectId = Guid.NewGuid();
        var json = """
            {"response":"Stage is closed.","draft_proposal":null,"memory_commands":{"daily_summary":null,"library_proposal":{"action":"CreateNode","target_object_id":null,"target_node_id":null,"expected_node_revision":null,"expected_overview_revision":0,"category":"Design","topic":"Relics","local_date":"2026-08-14","node_content":"Mechanism is implemented.","current_overview":"Current relic state.","materials":[{"kind":"GitCommit","reference":"abc123","label":"Implementation"}]}},"summary_deltas":null}
            """;

        Assert.True(LeaderStructuredResponse.TryParse(json, projectId, out var parsed));
        Assert.Equal("Stage is closed.", parsed.Response);
        Assert.Null(parsed.Proposal);
        Assert.Null(parsed.MemoryCommandError);
        var proposal = Assert.IsType<LeaderLibraryProposalCommand>(parsed.MemoryCommands!.LibraryProposal);
        Assert.Equal(LibraryProposalAction.CreateNode, proposal.Action);
        Assert.Equal(new DateOnly(2026, 8, 14), proposal.LocalDate);
        Assert.Equal("abc123", Assert.Single(proposal.Materials).Reference);
    }

    [Fact]
    public async Task Invalid_library_command_preserves_visible_response_and_uses_non_transcript_status()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            "{\"response\":\"The stage assessment is still visible.\",\"draft_proposal\":null,\"memory_commands\":{\"daily_summary\":null,\"library_proposal\":{\"action\":\"CreateNode\"}},\"summary_deltas\":null}", null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "close stage";

        await workspace.LeaderPane.SendAsync();

        Assert.Contains(workspace.LeaderPane.Messages, message => message.Text == "The stage assessment is still visible.");
        Assert.DoesNotContain(workspace.LeaderPane.Messages, message => message.Text.Contains("memory command", StringComparison.OrdinalIgnoreCase));
        Assert.True(workspace.LeaderPane.HasMemoryCommandStatus);
        Assert.Contains("could not be processed", workspace.LeaderPane.MemoryCommandStatus!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        var transcript = await context.Services.LeaderMessageRepository.GetAllAsync(workspace.LeaderPane.SessionEpochId!.Value);
        Assert.Equal("The stage assessment is still visible.", transcript.Last().Text);
    }
    [Fact]
    public async Task Valid_provider_neutral_proposal_persists_one_project_scoped_draft_and_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = new LeaderDraftProposal(
            fixture.ProjectId,
            "Ship onboarding",
            "Create a guided onboarding flow",
            "Onboarding screens and validation",
            "Billing and account recovery",
            ["A new user can complete onboarding"],
            TaskRiskLevel.Medium,
            new LeaderExecutionRecommendation("Provider", "model", "runtime"));

        var profile = ExecutionProfile.Create("provider", "account", "model", "runtime");
        var result = await fixture.Builder.CreateDraftAsync(proposal, profile);

        Assert.True(result.Succeeded);
        Assert.NotEqual(Guid.Empty, result.TaskId);
        var stored = await fixture.Tasks.GetAsync(fixture.ProjectId, result.TaskId!.Value);
        Assert.NotNull(stored);
        Assert.Equal("Ship onboarding", stored!.Title);
        Assert.Equal(TaskLifecycleStatus.Draft, stored.Status);
        var revision = Assert.Single(await fixture.Revisions.ListAsync(fixture.ProjectId, result.TaskId.Value));
        Assert.Equal(proposal.Goal, revision.Goal);
        Assert.Equal(proposal.Scope, revision.Scope);
        Assert.Equal(proposal.OutOfScope, revision.OutOfScope);
        Assert.Equal(proposal.Acceptance, revision.Acceptance);
        Assert.Equal(proposal.RiskLevel, revision.RiskLevel);
        Assert.Equal(profile, revision.RecommendedExecutionProfile);
    }

    [Fact]
    public async Task Invalid_proposal_creates_no_draft_or_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = new LeaderDraftProposal(
            fixture.ProjectId, "", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
            new LeaderExecutionRecommendation(null, null, null));

        var result = await fixture.Builder.CreateDraftAsync(proposal, ExecutionProfile.Create("provider", "account", "model", "runtime"));

        Assert.False(result.Succeeded);
        Assert.Empty(await fixture.Tasks.ListAsync(fixture.ProjectId));
    }

    [Fact]
    public async Task Proposal_for_another_project_is_rejected_without_side_effect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = new LeaderDraftProposal(
            Guid.NewGuid(), "Title", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
            new LeaderExecutionRecommendation(null, null, null));

        var result = await fixture.Builder.CreateDraftAsync(proposal, ExecutionProfile.Create("provider", "account", "model", "runtime"));

        Assert.False(result.Succeeded);
        Assert.Empty(await fixture.Tasks.ListAsync(fixture.ProjectId));
    }

    [Fact]
    public async Task Revision_insert_failure_rolls_back_the_task_insert()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RejectRevisionInsertsAsync();
        var proposal = new LeaderDraftProposal(
            fixture.ProjectId, "Title", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
            new LeaderExecutionRecommendation(null, null, null));

        var result = await fixture.Builder.CreateDraftAsync(proposal, ExecutionProfile.Create("provider", "account", "model", "runtime"));

        Assert.False(result.Succeeded);
        Assert.Empty(await fixture.Tasks.ListAsync(fixture.ProjectId));
        Assert.Equal(0, await fixture.CountRevisionsAsync());
    }

    [Fact]
    public async Task Leader_turn_persists_proposal_but_keeps_structured_envelope_out_of_transcript()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
                "{\"response\":\"Draft ready.\",\"draft_proposal\":{\"title\":\"Title\",\"goal\":\"goal\",\"scope\":\"scope\",\"outOfScope\":\"out\",\"acceptance\":[\"accept\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerHint\":\"fake-provider\",\"modelHint\":\"model-a\",\"runtimeHint\":\"fake-runtime\"}},\"memory_commands\":null,\"summary_deltas\":null}", null),
            DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.DraftMessage = "Plan onboarding";

        await workspace.LeaderPane.SendAsync();

        Assert.Equal("Draft ready.", workspace.LeaderPane.Messages.Last().Text);
        Assert.Single(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        var transcript = await context.Services.LeaderMessageRepository.GetAllAsync(workspace.LeaderPane.SessionEpochId!.Value);
        Assert.Equal("Draft ready.", transcript.Last().Text);
        Assert.DoesNotContain("draft_proposal", transcript.Last().Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Draft_confirmation_binds_candidate_creates_immutable_override_and_starts_one_worker()
    {
        var provider = new Workbench.Runtime.Providers.ProviderId("fake-provider");
        var runtime = new FakeAgentRuntime("Fake Provider", "Fake Account", null,
            new Workbench.Runtime.Providers.ModelProfile(provider, "model-a", "Model A", Workbench.Runtime.Agents.AgentCapability.StructuredEvents),
            new Workbench.Runtime.Providers.ModelProfile(provider, "model-b", "Model B", Workbench.Runtime.Agents.AgentCapability.StructuredEvents));
        var registry = new AgentRuntimeRegistry(); registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            "{\"response\":\"Draft ready.\",\"draft_proposal\":{\"title\":\"Title\",\"goal\":\"goal\",\"scope\":\"scope\",\"outOfScope\":\"out\",\"acceptance\":[\"accept\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerHint\":\"fake-provider\",\"modelHint\":\"model-a\",\"runtimeHint\":\"fake-runtime\"}},\"memory_commands\":null,\"summary_deltas\":null}", null), DateTimeOffset.UtcNow));
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "Worker complete", null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync();
        workspace.LeaderPane.SelectedModel = workspace.LeaderPane.AvailableModels[0];
        workspace.LeaderPane.DraftMessage = "delegate";

        await workspace.LeaderPane.SendAsync();

        Assert.Equal("Draft ready.", workspace.LeaderPane.Messages.Last().Text);
        Assert.True(workspace.LeaderPane.HasDraftConfirmation);
        Assert.Single(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Single(runtime.CreateRequests);
        var p2 = workspace.LeaderPane.WorkerResources.Single(item => item.ModelProfileId == "model-b");
        await workspace.LeaderPane.ChangeWorkerResourceAsync(p2);
        var task = (await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id)).Single();
        var revisions = await context.Services.TaskRevisionRepository.ListAsync(workspace.Result.Project.Id, task.TaskId);
        Assert.Equal(2, revisions.Count);
        Assert.Equal("model-a", revisions[0].RecommendedExecutionProfile.ModelProfileId);
        Assert.Equal("model-b", revisions[1].RecommendedExecutionProfile.ModelProfileId);

        await workspace.LeaderPane.ConfirmDraftAsync();

        Assert.Equal(2, runtime.CreateRequests.Count);
        Assert.Equal("model-b", runtime.CreateRequests.Last().ModelId);
    }

    [Fact]
    public async Task Persisted_draft_rebuilds_actionable_confirmation_after_leader_reconstruction()
    {
        var runtime = new FakeAgentRuntime("Fake Provider", "Fake Account", null,
            new Workbench.Runtime.Providers.ModelProfile(
                new Workbench.Runtime.Providers.ProviderId("fake-provider"),
                "model-a", "Model A", Workbench.Runtime.Agents.AgentCapability.StructuredEvents));
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var projectPath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"draft-recovery-{Guid.NewGuid():N}")).FullName;
        var project = await context.Services.ProjectOpenService.OpenAsync(projectPath);
        var proposal = new LeaderDraftProposal(
            project.Project.Id,
            "Recoverable draft",
            "Create the final acceptance file",
            "acceptance/result-final.txt",
            "All other files",
            ["ORBIT", "STRICT", "PHASE_3_FINAL_OK"],
            TaskRiskLevel.Low,
            new LeaderExecutionRecommendation("fake-provider", "model-a", "fake-runtime"));
        var profile = ExecutionProfile.Create(
            "fake-provider", runtime.Account.Id.Value.ToString(), "model-a", "fake-runtime");
        var created = await new LeaderDraftProposalBuilder(project.Project.Id, context.Services.TaskRepository)
            .CreateDraftAsync(proposal, profile);
        Assert.True(created.Succeeded);

        var reopenedSessions = new ProjectLeaderSessionManager(
            context.Services.ProjectLeaderRepository,
            context.Services.LeaderSessionEpochRepository,
            context.Services.LeaderMessageRepository,
            context.Time);
        var reopened = new WorkspaceViewModel(
            project,
            context.Services.ProjectLayoutRepository,
            () => Task.CompletedTask,
            context.Time,
            runtimeRegistry: context.Services.RuntimeRegistry,
            leaderSessionManager: reopenedSessions,
            taskRepository: context.Services.TaskRepository,
            taskRevisionRepository: context.Services.TaskRevisionRepository,
            workerSessionRouter: context.Services.WorkerSessionRouter,
            workerExecutionRepository: context.Services.WorkerExecutionRepository);

        await reopened.LeaderPane.InitializeAsync();

        Assert.True(reopened.LeaderPane.HasDraftConfirmation);
        Assert.Equal(created.TaskId, reopened.LeaderPane.DraftConfirmation!.TaskId);
        Assert.Equal(
            (await context.Services.TaskRevisionRepository.ListAsync(project.Project.Id, created.TaskId!.Value)).Single().Id,
            reopened.LeaderPane.DraftConfirmation.Revision.Id);
    }

    [Fact]
    public async Task Invalid_structured_response_is_not_shown_as_raw_json()
    {
        var runtime = new FakeAgentRuntime(); var registry = new AgentRuntimeRegistry(); registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "{\"draft_proposal\":true}", null), DateTimeOffset.UtcNow));
        await workspace.LeaderPane.InitializeAsync(); workspace.LeaderPane.DraftMessage = "delegate";

        await workspace.LeaderPane.SendAsync();

        Assert.DoesNotContain(workspace.LeaderPane.Messages, message => message.Text.Contains("draft_proposal", StringComparison.Ordinal));
        Assert.Contains(workspace.LeaderPane.Messages, message => message.Text == "Leader response could not be processed.");
        Assert.False(workspace.LeaderPane.HasDraftConfirmation);
        Assert.Empty(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path;
        private Fixture(string path, WorkbenchDatabase database, Guid projectId)
        {
            _path = path;
            ProjectId = projectId;
            Tasks = new TaskRepository(database);
            Revisions = new TaskRevisionRepository(database);
            Builder = new LeaderDraftProposalBuilder(projectId, Tasks);
        }

        public Guid ProjectId { get; }
        public TaskRepository Tasks { get; }
        public TaskRevisionRepository Revisions { get; }
        public LeaderDraftProposalBuilder Builder { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"leader-proposal-{Guid.NewGuid():N}.db");
            var database = new WorkbenchDatabase(path);
            await database.InitializeAsync();
            var projectId = Guid.NewGuid();
            await new ProjectRepository(database).UpsertAsync(new CoreProject(projectId, "Test", Path.GetTempPath(), ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            return new Fixture(path, database, projectId);
        }

        public async Task RejectRevisionInsertsAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={_path};Foreign Keys=True;Pooling=False");
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_task_revision BEFORE INSERT ON task_revisions BEGIN SELECT RAISE(ABORT, 'revision rejected'); END;";
            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> CountRevisionsAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={_path};Foreign Keys=True;Pooling=False");
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM task_revisions";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public ValueTask DisposeAsync()
        {
            try { File.Delete(_path); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
