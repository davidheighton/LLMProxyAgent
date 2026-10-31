namespace LLMSurvivingProxyAgent.Runtime;

public sealed record AutonomousMoveApprovalRequest(
    string ApprovalId,
    string AgentId,
    int CurrentSector,
    int TargetSector,
    int TurnCost,
    string Objective);

public interface IAutonomousMoveApprovalGate
{
    AutonomousMoveApprovalRequest? PendingRequest { get; }

    Task<bool> RequestApprovalAsync(
        AutonomousMoveApprovalRequest request,
        CancellationToken cancellationToken = default);

    bool Resolve(string approvalId, bool approved);
}

public sealed class AutonomousMoveApprovalGate : IAutonomousMoveApprovalGate
{
    private PendingApproval? _pending;

    public AutonomousMoveApprovalRequest? PendingRequest =>
        Volatile.Read(ref _pending)?.Request;

    public async Task<bool> RequestApprovalAsync(
        AutonomousMoveApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApprovalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Objective);
        if (request.CurrentSector <= 0 ||
            request.TargetSector <= 0 ||
            request.CurrentSector == request.TargetSector ||
            request.TurnCost != 1)
        {
            throw new ArgumentException("A movement approval request must describe one valid one-turn move.", nameof(request));
        }

        var pending = new PendingApproval(request);
        if (Interlocked.CompareExchange(ref _pending, pending, null) is not null)
        {
            throw new InvalidOperationException("Another autonomous movement is already awaiting approval.");
        }

        try
        {
            return await pending.Decision.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(ref _pending, null, pending);
        }
    }

    public bool Resolve(string approvalId, bool approved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        var pending = Volatile.Read(ref _pending);
        return pending is not null &&
               string.Equals(pending.Request.ApprovalId, approvalId, StringComparison.Ordinal) &&
               pending.Decision.TrySetResult(approved);
    }

    private sealed record PendingApproval(AutonomousMoveApprovalRequest Request)
    {
        public TaskCompletionSource<bool> Decision { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
