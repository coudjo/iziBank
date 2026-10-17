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
    public const string ReferenceCodeTaken = "reference.code-taken";
    public const string ReferenceCodeImmutable = "reference.code-immutable";
    public const string RecordOtherBranch = "record.other-branch";
    public const string LocalCurrencyRequired = "currency.local-required";
    public const string LocalCurrencyInUse = "currency.local-in-use";
    public const string HolidayExists = "holiday.exists";
    public const string HolidayBusinessDayExists = "holiday.business-day-exists";
    public const string BusinessDayNotOpen = "business-day.not-open";
    public const string BusinessDayPreviousOpen = "business-day.previous-open";
    public const string BusinessDayDateNotAfter = "business-day.date-not-after";
    public const string BusinessDayNonWorkingDay = "business-day.non-working-day";
    public const string LedgerFieldLocked = "ledger.field-locked";
    public const string ConcurrencyConflict = "record.changed";
    public const string PartyDuplicateIdentification = "party.duplicate-identification";
    public const string PartyDuplicateRegistration = "party.duplicate-registration";
    public const string GroupMemberInOtherGroup = "group.member-in-other-group";
    public const string GroupChairpersonExists = "group.chairperson-exists";
    public const string PartyPrimaryDocument = "party.primary-document";
    public const string UploadEmpty = "upload.empty";
    public const string UploadTooLarge = "upload.too-large";
    public const string UploadTypeNotAllowed = "upload.type-not-allowed";
    public const string UploadTypeMismatch = "upload.type-mismatch";
    public const string UploadDimensions = "upload.dimensions";
    public const string CreditOfficerExists = "credit-officer.exists";
    public const string KycChecklistIncomplete = "kyc.checklist-incomplete";
    public const string KycNotPending = "kyc.not-pending";
    public const string IdempotencyKeyInvalid = "idempotency.key-invalid";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
    public const string IdempotencyInProgress = "idempotency.in-progress";
    public const string ProductCodeTaken = "product.code-taken";
    public const string ProductFieldLocked = "product.field-locked";
    public const string ProductRateOverlap = "product.rate-overlap";
    public const string ProductRateStarted = "product.rate-started";
    public const string AccountProductNotOpenable = "account.product-not-openable";
    public const string AccountHolderNotReady = "account.holder-not-ready";
    public const string AccountSignatureMissing = "account.signature-missing";
    public const string AccountLimitReached = "account.limit-reached";
    public const string AccountClosed = "account.closed";
    public const string AccountAlreadyFrozen = "account.already-frozen";
    public const string AccountNotFrozen = "account.not-frozen";
    public const string AccountAlreadyDormant = "account.already-dormant";
    public const string AccountNotDormant = "account.not-dormant";
    public const string AccountCloseBlocked = "account.close-blocked";
    public const string AccountClassificationExists = "account.classification-exists";

    public static readonly IReadOnlyList<string> All =
    [
        Validation, NotFound, Unexpected,
        InvalidCredentials, Locked, Unauthenticated, SessionEnded, SessionSuperseded, SessionTimedOut,
        SessionMaxExceeded, SessionForced, MustChangeSecretKey, SecretKeyRules, CurrentSecretKeyWrong,
        PermissionDenied, BranchNotAllowed, ActivityLogUnavailable, SessionNotOpen,
        RoleGroupCodeTaken, RoleGroupInactive, SelfAssignment, LastAdministrator, ChangePendingExists, ChangeNotPending,
        LookupCodeTaken, LookupCodeImmutable, LookupCodedRow, StaffCodeTaken, StaffSelfChange,
        ReferenceCodeTaken, ReferenceCodeImmutable, RecordOtherBranch, LocalCurrencyRequired, LocalCurrencyInUse, HolidayExists,
        HolidayBusinessDayExists, BusinessDayNotOpen, BusinessDayPreviousOpen, BusinessDayDateNotAfter,
        BusinessDayNonWorkingDay, LedgerFieldLocked, ConcurrencyConflict, PartyDuplicateIdentification,
        PartyDuplicateRegistration, GroupMemberInOtherGroup, GroupChairpersonExists, PartyPrimaryDocument,
        UploadEmpty, UploadTooLarge, UploadTypeNotAllowed, UploadTypeMismatch, UploadDimensions,
        IdempotencyKeyInvalid, IdempotencyKeyReused, IdempotencyInProgress, CreditOfficerExists,
        KycChecklistIncomplete, KycNotPending, ProductCodeTaken, ProductFieldLocked, ProductRateOverlap, ProductRateStarted,
        AccountProductNotOpenable, AccountHolderNotReady, AccountSignatureMissing, AccountLimitReached,
        AccountClosed, AccountAlreadyFrozen, AccountNotFrozen, AccountAlreadyDormant, AccountNotDormant, AccountCloseBlocked,
        AccountClassificationExists
    ];
}
