namespace Workbench.Storage.Memory;

/// <summary>
/// Thrown when a candidate certification (Accept, Edit+Accept, or Reject) cannot proceed because
/// the candidate was already processed, is not owned by the requested project, or lost a
/// concurrent certification race. Carries no retry expectation: the candidate is no longer Active.
/// </summary>
public sealed class MemoryCertificationConflictException : Exception
{
    public MemoryCertificationConflictException(string message) : base(message)
    {
    }
}
