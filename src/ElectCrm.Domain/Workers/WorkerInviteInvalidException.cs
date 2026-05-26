namespace ElectCrm.Domain.Workers;

// Thrown when a worker invite token is invalid for any reason (expired, consumed, revoked, or not found).
// Returns the same error regardless of failure reason — prevents token enumeration.
public sealed class WorkerInviteInvalidException : Exception
{
    public WorkerInviteInvalidException() : base("This invitation link is not valid or has expired.") { }
}
