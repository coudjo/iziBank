namespace Banking.Domain.Lookups;

public static class EventCodes
{
    public const string ApiRequest = "API_REQUEST";
    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string Logout = "LOGOUT";
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string SecretKeyChanged = "SECRET_KEY_CHANGED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string ViewRecord = "VIEW_RECORD";
    public const string Search = "SEARCH";
    public const string CreateRecord = "CREATE_RECORD";
    public const string UpdateRecord = "UPDATE_RECORD";
    public const string DeleteRecord = "DELETE_RECORD";
    public const string SuperviseApprove = "SUPERVISE_APPROVE";
    public const string SuperviseReject = "SUPERVISE_REJECT";
    public const string KycVerify = "KYC_VERIFY";
    public const string KycReject = "KYC_REJECT";
    public const string AccountOpen = "ACCOUNT_OPEN";
    public const string AccountClose = "ACCOUNT_CLOSE";
    public const string AccountFreeze = "ACCOUNT_FREEZE";
    public const string AccountUnfreeze = "ACCOUNT_UNFREEZE";
    public const string BalanceHold = "BALANCE_HOLD";
    public const string BalanceRelease = "BALANCE_RELEASE";
    public const string DormancyChange = "DORMANCY_CHANGE";
    public const string UserAdmin = "USER_ADMIN";
    public const string AuditView = "AUDIT_VIEW";
    public const string BusinessDayOpen = "BUSINESS_DAY_OPEN";
    public const string BusinessDayClose = "BUSINESS_DAY_CLOSE";
    public const string PostingRequest = "POSTING_REQUEST";
    public const string PostingPost = "POSTING_POST";
    public const string PostingApprove = "POSTING_APPROVE";
    public const string PostingReject = "POSTING_REJECT";
    public const string PostingCancel = "POSTING_CANCEL";
    public const string TillOpen = "TILL_OPEN";
    public const string TillClose = "TILL_CLOSE";

    public static readonly IReadOnlyList<string> All =
    [
        ApiRequest,
        Login,
        LoginFailed,
        Logout,
        SessionExpired,
        SecretKeyChanged,
        PermissionDenied,
        ViewRecord,
        Search,
        CreateRecord,
        UpdateRecord,
        DeleteRecord,
        SuperviseApprove,
        SuperviseReject,
        KycVerify,
        KycReject,
        AccountOpen,
        AccountClose,
        AccountFreeze,
        AccountUnfreeze,
        BalanceHold,
        BalanceRelease,
        DormancyChange,
        UserAdmin,
        AuditView,
        BusinessDayOpen,
        BusinessDayClose,
        PostingRequest,
        PostingPost,
        PostingApprove,
        PostingReject,
        PostingCancel,
        TillOpen,
        TillClose
    ];
}
