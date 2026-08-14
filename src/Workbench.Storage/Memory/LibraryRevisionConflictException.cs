namespace Workbench.Storage.Memory;

public sealed class LibraryRevisionConflictException(string message) : InvalidOperationException(message);
