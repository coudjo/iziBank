using Banking.Domain.Auditing;

namespace Banking.Domain.Supervision;

public enum SupervisionState
{
    NotRequired,
    Pending,
    Supervised
}

/// <summary>Stable problem codes returned when a supervision rule refuses an action.</summary>
public static class SupervisionProblems
{
    public const string PermissionDenied = "supervision.permission-denied";
    public const string SelfApproval = "supervision.self-approval";
    public const string NotPending = "supervision.not-pending";
    public const string Pending = "supervision.pending";
    public const string RejectNotAllowed = "supervision.reject-not-allowed";
    public const string HasDependents = "supervision.has-dependents";
}

public sealed record SupervisionCheck(bool Allowed, string? ProblemCode)
{
    public static readonly SupervisionCheck Ok = new(true, null);

    public static SupervisionCheck Fail(string problemCode) => new(false, problemCode);
}

/// <summary>
/// Maker-checker rules R1 to R5 (plan 5.4). Pure functions; callers supply permissions and history.
/// </summary>
public static class SupervisionRules
{
    public static SupervisionState StateOf(ISupervisable record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!record.IsSupervisionRequired)
        {
            return SupervisionState.NotRequired;
        }

        return record.SupervisedByUserId is null ? SupervisionState.Pending : SupervisionState.Supervised;
    }

    /// <summary>R1: a new record starts Pending when supervision is enabled.</summary>
    public static void StartNew(ISupervisable record, bool supervisionEnabled)
    {
        ArgumentNullException.ThrowIfNull(record);

        record.IsSupervisionRequired = supervisionEnabled;
        record.SupervisedByUserId = null;
        record.SupervisedOn = null;
    }

    /// <summary>
    /// R2: needs the supervise permission, a Pending record, and a supervisor who is neither the
    /// creator nor the last modifier (unless self-approval is allowed).
    /// </summary>
    public static SupervisionCheck CheckApprove<TRecord>(
        TRecord record,
        long supervisorId,
        bool hasSupervisePermission,
        bool allowSelfApproval)
        where TRecord : ISupervisable, IAuditable
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!hasSupervisePermission)
        {
            return SupervisionCheck.Fail(SupervisionProblems.PermissionDenied);
        }

        if (StateOf(record) != SupervisionState.Pending)
        {
            return SupervisionCheck.Fail(SupervisionProblems.NotPending);
        }

        if (!allowSelfApproval
            && (record.CreatedByUserId == supervisorId || record.ModifiedByUserId == supervisorId))
        {
            return SupervisionCheck.Fail(SupervisionProblems.SelfApproval);
        }

        return SupervisionCheck.Ok;
    }

    /// <summary>R2: records the approval. <c>IsSupervisionRequired</c> stays set as history.</summary>
    public static void Approve<TRecord>(TRecord record, long supervisorId, DateTime approvedOnUtc)
        where TRecord : ISupervisable, IAuditable
    {
        ArgumentNullException.ThrowIfNull(record);

        if (StateOf(record) != SupervisionState.Pending)
        {
            throw new InvalidOperationException("Only a pending record can be approved.");
        }

        record.SupervisedByUserId = supervisorId;
        record.SupervisedOn = approvedOnUtc;
    }

    /// <summary>R3: a Pending record cannot be used for new dependent operations.</summary>
    public static SupervisionCheck CheckUsable(ISupervisable record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return StateOf(record) == SupervisionState.Pending
            ? SupervisionCheck.Fail(SupervisionProblems.Pending)
            : SupervisionCheck.Ok;
    }

    /// <summary>R4: an edit to a controlled field puts the record back to Pending.</summary>
    public static void ResetAfterControlledEdit(ISupervisable record, bool supervisionEnabled)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!supervisionEnabled)
        {
            return;
        }

        record.IsSupervisionRequired = true;
        record.SupervisedByUserId = null;
        record.SupervisedOn = null;
    }

    /// <summary>
    /// R5: reject (hard delete) only a Pending record that was never supervised and has no dependents.
    /// </summary>
    public static SupervisionCheck CheckReject(
        ISupervisable record,
        bool hasRejectPermission,
        bool wasEverSupervised,
        bool hasDependents)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!hasRejectPermission)
        {
            return SupervisionCheck.Fail(SupervisionProblems.PermissionDenied);
        }

        if (StateOf(record) != SupervisionState.Pending)
        {
            return SupervisionCheck.Fail(SupervisionProblems.NotPending);
        }

        if (wasEverSupervised)
        {
            return SupervisionCheck.Fail(SupervisionProblems.RejectNotAllowed);
        }

        return hasDependents
            ? SupervisionCheck.Fail(SupervisionProblems.HasDependents)
            : SupervisionCheck.Ok;
    }
}
