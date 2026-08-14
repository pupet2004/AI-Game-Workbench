namespace Workbench.Storage.Memory;

public sealed class MemoryRevisionConflictException(string message) : InvalidOperationException(message);
