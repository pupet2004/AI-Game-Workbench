using Workbench.Project.Detection;
using Workbench.Project.Git;
using Workbench.Project.Tests.Support;

namespace Workbench.Project.Tests.Git;

public sealed class GitCliInspectorTests
{
    [Fact]
    public async Task Detects_git_repository()
    {
        await using var repository = await TemporaryGitRepository.CreateAsync();

        var snapshot = await new GitCliInspector().InspectAsync(repository.Path);

        Assert.True(snapshot.GitInstalled);
        Assert.True(snapshot.IsRepository);
    }

    [Fact]
    public async Task Returns_repository_root()
    {
        await using var repository = await TemporaryGitRepository.CreateAsync();

        var snapshot = await new GitCliInspector().InspectAsync(repository.Path);

        Assert.Equal(Path.GetFullPath(repository.Path), snapshot.RepositoryRoot);
    }

    [Fact]
    public async Task Returns_full_head_commit()
    {
        await using var repository = await TemporaryGitRepository.CreateAsync();
        var expectedHead = (await repository.RunGitAsync("rev-parse", "HEAD")).Trim();

        var snapshot = await new GitCliInspector().InspectAsync(repository.Path);

        Assert.Equal(expectedHead, snapshot.HeadCommit);
        Assert.Equal(40, snapshot.HeadCommit!.Length);
    }

    [Fact]
    public async Task Detects_current_branch()
    {
        await using var repository = await TemporaryGitRepository.CreateAsync();

        var snapshot = await new GitCliInspector().InspectAsync(repository.Path);

        Assert.Equal("test-main", snapshot.BranchName);
        Assert.False(snapshot.IsDetachedHead);
    }

    [Fact]
    public async Task Detects_clean_repository()
    {
        await using var repository = await TemporaryGitRepository.CreateAsync();

        var snapshot = await new GitCliInspector().InspectAsync(repository.Path);

        Assert.False(snapshot.IsDirty);
    }

    [Fact]
    public async Task Detects_dirty_repository()
    {
        await using var repository = await TemporaryGitRepository.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "dirty.txt"), "dirty");

        var snapshot = await new GitCliInspector().InspectAsync(repository.Path);

        Assert.True(snapshot.IsDirty);
    }

    [Fact]
    public async Task Detects_detached_head()
    {
        await using var repository = await TemporaryGitRepository.CreateAsync();
        var head = (await repository.RunGitAsync("rev-parse", "HEAD")).Trim();
        await repository.RunGitAsync("checkout", "--detach", head);

        var snapshot = await new GitCliInspector().InspectAsync(repository.Path);

        Assert.True(snapshot.IsDetachedHead);
        Assert.Null(snapshot.BranchName);
        Assert.Equal(head, snapshot.HeadCommit);
    }

    [Fact]
    public async Task Inspecting_subdirectory_returns_repository_root()
    {
        await using var repository = await TemporaryGitRepository.CreateAsync();
        var subdirectory = Path.Combine(repository.Path, "src", "feature");
        Directory.CreateDirectory(subdirectory);

        var snapshot = await new GitCliInspector().InspectAsync(subdirectory);

        Assert.Equal(Path.GetFullPath(repository.Path), snapshot.RepositoryRoot);
    }

    [Fact]
    public async Task Non_git_folder_is_not_repository()
    {
        using var directory = new TemporaryDirectory();

        var snapshot = await new GitCliInspector().InspectAsync(directory.Path);

        Assert.True(snapshot.GitInstalled);
        Assert.False(snapshot.IsRepository);
        Assert.Null(snapshot.RepositoryRoot);
        Assert.False(snapshot.IsDirty);
    }

    [Fact]
    public async Task Git_repository_under_unicode_path_is_inspected()
    {
        await using var repository = await TemporaryGitRepository.CreateAsync("玉牌劫测试");

        var detection = new ProjectDetector().Detect(repository.Path);
        var snapshot = await new GitCliInspector().InspectAsync(repository.Path);

        Assert.Equal("玉牌劫测试", detection.SuggestedName);
        Assert.True(snapshot.IsRepository);
        Assert.Equal(Path.GetFullPath(repository.Path), snapshot.RepositoryRoot);
    }
}
