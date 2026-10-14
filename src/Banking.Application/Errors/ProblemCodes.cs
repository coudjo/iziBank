namespace Banking.Application.Errors;

/// <summary>
/// Stable machine codes sent as the problem details <c>type</c>. Every code here must be listed in
/// <c>docs/api/problem-types.md</c> and in the portal's error catalog.
/// </summary>
public static class ProblemCodes
{
    public const string Validation = "validation";
    public const string NotFound = "not-found";
    public const string Unexpected = "unexpected";

    public const string InvalidCredentials = "auth.invalid-credentials";
    public const string Locked = "auth.locked";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string SessionEnded = "auth.session-ended";
    public const string SessionSuperseded = "auth.session-superseded";
    public const string SessionTimedOut = "auth.session-timed-out";
    public const string SessionMaxExceeded = "auth.session-max-exceeded";
    public const string SessionForced = "auth.session-forced";
    public const string MustChangeSecretKey = "auth.must-change-secret-key";
    public const string SecretKeyRules = "auth.secret-key-rules";
    public const string CurrentSecretKeyWrong = "auth.current-secret-key-wrong";
    public const string PermissionDenied = "auth.permission-denied";
    public const string BranchNotAllowed = "auth.branch-not-allowed";
    public const string ActivityLogUnavailable = "audit.activity-log-unavailable";
    public const string SessionNotOpen = "session.not-open";
    public const string RoleGroupCodeTaken = "rolegroup.code-taken";
    public const string RoleGroupInactive = "rolegroup.inactive";
    public const string SelfAssignment = "security.self-assignment";
    public const string LastAdministrator = "security.last-administrator";
    public const string ChangePendingExists = "change.pending-exists";
    public const string ChangeNotPending = "change.not-pending";
    public const string LookupCodeTaken = "lookup.code-taken";
    public const string LookupCodeImmutable = "lookup.code-immutable";
    public const string LookupCodedRow = "lookup.coded-row";
    public const string StaffCodeTaken = "staff.code-taken";
    public const string StaffSelfChange = "staff.self-change";

    public static readonly IReadOnlyList<string> All =
    [
        Validation, NotFound, Unexpected,
        InvalidCredentials, Locked, Unauthenticated, SessionEnded, SessionSuperseded, SessionTimedOut,
        SessionMaxExceeded, SessionForced, MustChangeSecretKey, SecretKeyRules, CurrentSecretKeyWrong,
        PermissionDenied, BranchNotAllowed, ActivityLogUnavailable, SessionNotOpen,
        RoleGroupCodeTaken, RoleGroupInactive, SelfAssignment, LastAdministrator, ChangePendingExists, ChangeNotPending,
        LookupCodeTaken, LookupCodeImmutable, LookupCodedRow, StaffCodeTaken, StaffSelfChange
    ];
}
