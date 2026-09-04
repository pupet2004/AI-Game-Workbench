using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Workbench.App.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}

public sealed class FolderPickerService : IFolderPickerService
{
    private readonly TopLevel _topLevel;

    public FolderPickerService(TopLevel topLevel)
    {
        _topLevel = topLevel;
    }

    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Current["Home.PickerTitle"],
            AllowMultiple = false
        });

        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
