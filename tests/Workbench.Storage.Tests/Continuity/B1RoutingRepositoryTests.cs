using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.Storage.Tests.Continuity;

public sealed class B1RoutingRepositoryTests
{
    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-08-22T11:00:00.0000000+00:00");

    [Fact]
    public async Task Attempt_binds_current_unresolved_revision_immutably()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1RoutingRepository(fixture.Database);
        var attemptRef = new AttemptRef(Guid.NewGuid());

        var attempt = await repository.CreateAttemptAsync(CreateAttempt(fixture.Primary, attemptRef));
        await fixture.AddSuccessorRevisionAsync(fixture.Primary);

        Assert.Equal(fixture.Primary.AssignmentRef, attempt.AssignmentRef);
        Assert.Equal(fixture.Primary.RevisionRef, attempt.EffectiveRevisionRef);
        Assert.Equal(
            fixture.Primary.RevisionRef.Value.ToString(),
            await fixture.ScalarStringAsync(
                "SELECT effective_revision_id FROM b1_attempts WHERE id=$attempt;",
                ("$attempt", attemptRef.Value.ToString())));
        Assert.Equal(1L, await fixture.CountAsync("b1_attempt_routing", attemptRef.Value, "attempt_id"));
    }

    [Fact]
    public async Task Attempt_creation_rejects_replaced_assignment()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        await fixture.MarkAssignmentReplacedAsync(fixture.Primary);
        var repository = new B1RoutingRepository(fixture.Database);

        await Assert.ThrowsAsync<B1CommandException>(() =>
            repository.CreateAttemptAsync(CreateAttempt(
                fixture.Primary, new AttemptRef(Guid.NewGuid()))));
    }

    [Fact]
    public async Task Attempt_creation_rejects_dispositioned_or_stale_revision()
    {
        await using (var stale = await B1ContinuityStorageFixture.CreateAsync())
        {
            await stale.AddSuccessorRevisionAsync(stale.Primary);
            var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
                new B1RoutingRepository(stale.Database).CreateAttemptAsync(
                    CreateAttempt(stale.Primary, new AttemptRef(Guid.NewGuid()))));
            Assert.Equal(B1FailureCode.StaleRevision, exception.Code);
        }

        await using (var dispositioned = await B1ContinuityStorageFixture.CreateAsync())
        {
            await dispositioned.AddDispositionAsync(dispositioned.Primary, dispositioned.Primary.RevisionRef);
            var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
                new B1RoutingRepository(dispositioned.Database).CreateAttemptAsync(
                    CreateAttempt(dispositioned.Primary, new AttemptRef(Guid.NewGuid()))));
            Assert.Equal(B1FailureCode.AlreadyDispositioned, exception.Code);
        }
    }

    [Fact]
    public async Task SessionBinding_requires_attempt_assignee_actor()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1RoutingRepository(fixture.Database);
        var attemptRef = new AttemptRef(Guid.NewGuid());
        await repository.CreateAttemptAsync(CreateAttempt(fixture.Primary, attemptRef));
        var otherActor = await fixture.AddActorAsync(fixture.Primary);

        await Assert.ThrowsAsync<B1CommandException>(() => repository.CreateSessionBindingAsync(
            CreateBinding(fixture.Primary, attemptRef, otherActor, "external:wrong-actor")));
    }

    [Fact]
    public async Task Manual_attempt_allows_zero_bindings()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1RoutingRepository(fixture.Database);
        var attemptRef = new AttemptRef(Guid.NewGuid());

        await repository.CreateAttemptAsync(CreateAttempt(fixture.Primary, attemptRef));

        Assert.Equal(0L, await fixture.CountAsync("b1_session_bindings", attemptRef.Value, "attempt_id"));
        Assert.Null(await fixture.ScalarStringAsync(
            "SELECT selected_session_binding_id FROM b1_attempt_routing WHERE attempt_id=$attempt;",
            ("$attempt", attemptRef.Value.ToString())));
    }

    [Fact]
    public async Task Same_external_locator_can_have_multiple_bindings()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1RoutingRepository(fixture.Database);
        var attemptRef = new AttemptRef(Guid.NewGuid());
        await repository.CreateAttemptAsync(CreateAttempt(fixture.Primary, attemptRef));

        var first = await repository.CreateSessionBindingAsync(
            CreateBinding(fixture.Primary, attemptRef, fixture.Primary.AssigneeActorRef, "external:shared"));
        var second = await repository.CreateSessionBindingAsync(
            CreateBinding(fixture.Primary, attemptRef, fixture.Primary.AssigneeActorRef, "external:shared"));

        Assert.NotEqual(first.SessionBindingRef, second.SessionBindingRef);
        Assert.Equal(2L, Convert.ToInt64(await fixture.ScalarStringAsync(
            "SELECT COUNT(*) FROM b1_session_bindings WHERE external_session_ref='external:shared';")));
    }

    [Fact]
    public async Task Same_external_locator_can_bind_different_projects_without_merging()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var secondProject = await fixture.AddProjectAsync("second");
        var repository = new B1RoutingRepository(fixture.Database);
        var firstAttempt = new AttemptRef(Guid.NewGuid());
        var secondAttempt = new AttemptRef(Guid.NewGuid());
        await repository.CreateAttemptAsync(CreateAttempt(fixture.Primary, firstAttempt));
        await repository.CreateAttemptAsync(CreateAttempt(secondProject, secondAttempt));

        var first = await repository.CreateSessionBindingAsync(
            CreateBinding(fixture.Primary, firstAttempt, fixture.Primary.AssigneeActorRef, "external:cross-project"));
        var second = await repository.CreateSessionBindingAsync(
            CreateBinding(secondProject, secondAttempt, secondProject.AssigneeActorRef, "external:cross-project"));

        Assert.NotEqual(first.SessionBindingRef, second.SessionBindingRef);
        Assert.Equal(2L, Convert.ToInt64(await fixture.ScalarStringAsync(
            "SELECT COUNT(*) FROM b1_session_bindings WHERE external_session_ref='external:cross-project';")));
    }

    [Fact]
    public async Task Selection_uses_expected_stored_ref_not_effective_ref()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1RoutingRepository(fixture.Database);
        var attemptRef = new AttemptRef(Guid.NewGuid());
        await repository.CreateAttemptAndSelectAsync(
            CreateAttempt(fixture.Primary, attemptRef), expectedStored: null);
        await fixture.AddDispositionAsync(fixture.Primary, fixture.Primary.RevisionRef);

        var stale = await Assert.ThrowsAsync<B1CommandException>(() => repository.SelectCurrentAttemptAsync(
            new SelectCurrentAttemptCommand(
                fixture.Primary.ProjectRef,
                fixture.Primary.BootstrapPrincipalRef,
                fixture.Primary.AssignmentRef,
                ExpectedStoredAttemptRef: null,
                SelectedAttemptRef: null)));
        Assert.Equal(B1FailureCode.StaleRoutingSelection, stale.Code);

        await repository.SelectCurrentAttemptAsync(new SelectCurrentAttemptCommand(
            fixture.Primary.ProjectRef,
            fixture.Primary.BootstrapPrincipalRef,
            fixture.Primary.AssignmentRef,
            ExpectedStoredAttemptRef: attemptRef,
            SelectedAttemptRef: null));
        Assert.Null(await fixture.ScalarStringAsync(
            "SELECT selected_attempt_id FROM b1_assignment_routing WHERE assignment_id=$assignment;",
            ("$assignment", fixture.Primary.AssignmentRef.Value.ToString())));
    }

    [Fact]
    public async Task Concurrent_attempt_selection_has_one_winner()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1RoutingRepository(fixture.Database);
        var first = new AttemptRef(Guid.NewGuid());
        var second = new AttemptRef(Guid.NewGuid());
        await repository.CreateAttemptAsync(CreateAttempt(fixture.Primary, first));
        await repository.CreateAttemptAsync(CreateAttempt(fixture.Primary, second));

        var results = await Task.WhenAll(
            TrySelectAsync(repository, fixture.Primary, first),
            TrySelectAsync(repository, fixture.Primary, second));

        Assert.Single(results, result => result is null);
        var failure = Assert.Single(results, result => result is not null);
        Assert.Equal(B1FailureCode.StaleRoutingSelection, failure!.Code);
        Assert.Contains(
            await fixture.ScalarStringAsync(
                "SELECT selected_attempt_id FROM b1_assignment_routing WHERE assignment_id=$assignment;",
                ("$assignment", fixture.Primary.AssignmentRef.Value.ToString())),
            new[] { first.Value.ToString(), second.Value.ToString() });
    }

    [Fact]
    public async Task Clear_binding_does_not_mutate_binding()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1RoutingRepository(fixture.Database);
        var attemptRef = new AttemptRef(Guid.NewGuid());
        await repository.CreateAttemptAsync(CreateAttempt(fixture.Primary, attemptRef));
        var binding = await repository.CreateSessionBindingAsync(
            CreateBinding(fixture.Primary, attemptRef, fixture.Primary.AssigneeActorRef, "external:clear"));
        await repository.SelectCurrentSessionBindingAsync(new SelectCurrentSessionBindingCommand(
            fixture.Primary.ProjectRef,
            fixture.Primary.BootstrapPrincipalRef,
            attemptRef,
            ExpectedStoredSessionBindingRef: null,
            binding.SessionBindingRef));

        await repository.ClearCurrentSessionBindingAsync(new ClearCurrentSessionBindingCommand(
            fixture.Primary.ProjectRef,
            fixture.Primary.BootstrapPrincipalRef,
            attemptRef,
            ExpectedStoredSessionBindingRef: binding.SessionBindingRef));

        Assert.Equal(1L, await fixture.CountAsync(
            "b1_session_bindings", binding.SessionBindingRef.Value));
        Assert.Null(await fixture.ScalarStringAsync(
            "SELECT selected_session_binding_id FROM b1_attempt_routing WHERE attempt_id=$attempt;",
            ("$attempt", attemptRef.Value.ToString())));
    }

    [Fact]
    public async Task Create_and_select_rolls_back_both_on_stale_CAS()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var repository = new B1RoutingRepository(fixture.Database);
        var selected = new AttemptRef(Guid.NewGuid());
        await repository.CreateAttemptAndSelectAsync(
            CreateAttempt(fixture.Primary, selected), expectedStored: null);
        var rejected = new AttemptRef(Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<B1CommandException>(() =>
            repository.CreateAttemptAndSelectAsync(
                CreateAttempt(fixture.Primary, rejected), expectedStored: null));

        Assert.Equal(B1FailureCode.StaleRoutingSelection, exception.Code);
        Assert.Equal(0L, await fixture.CountAsync("b1_attempts", rejected.Value));
        Assert.Equal(0L, await fixture.CountAsync("b1_attempt_routing", rejected.Value, "attempt_id"));
        Assert.Equal(
            selected.Value.ToString(),
            await fixture.ScalarStringAsync(
                "SELECT selected_attempt_id FROM b1_assignment_routing WHERE assignment_id=$assignment;",
                ("$assignment", fixture.Primary.AssignmentRef.Value.ToString())));
    }

    private static CreateAttemptCommand CreateAttempt(B1ContinuitySeed seed, AttemptRef attemptRef) =>
        new(
            seed.ProjectRef,
            seed.BootstrapPrincipalRef,
            attemptRef,
            seed.AssignmentRef,
            seed.RevisionRef,
            At);

    private static CreateSessionBindingCommand CreateBinding(
        B1ContinuitySeed seed,
        AttemptRef attemptRef,
        LogicalActorRef actorRef,
        string externalRef) =>
        new(
            seed.ProjectRef,
            seed.BootstrapPrincipalRef,
            new SessionBinding(
                new SessionBindingRef(Guid.NewGuid()),
                attemptRef,
                actorRef,
                new ExternalSessionRef(externalRef),
                At));

    private static async Task<B1CommandException?> TrySelectAsync(
        B1RoutingRepository repository,
        B1ContinuitySeed seed,
        AttemptRef selected)
    {
        try
        {
            await repository.SelectCurrentAttemptAsync(new SelectCurrentAttemptCommand(
                seed.ProjectRef,
                seed.BootstrapPrincipalRef,
                seed.AssignmentRef,
                ExpectedStoredAttemptRef: null,
                SelectedAttemptRef: selected));
            return null;
        }
        catch (B1CommandException exception)
        {
            return exception;
        }
    }
}
