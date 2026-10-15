using Banking.Application.Errors;

namespace Banking.Api.Problems;

/// <summary>RFC 7807 responses whose <c>type</c> is a stable code from <see cref="ProblemCodes"/>.</summary>
internal static class ApiProblems
{
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        [ProblemCodes.Validation] = "Some fields need attention.",
        [ProblemCodes.NotFound] = "Not found.",
        [ProblemCodes.Unexpected] = "Something went wrong.",
        [ProblemCodes.InvalidCredentials] = "Staff code or password is incorrect.",
        [ProblemCodes.Locked] = "Too many failed attempts. Sign-in is locked for a while.",
        [ProblemCodes.Unauthenticated] = "Sign in to continue.",
        [ProblemCodes.SessionEnded] = "Your session has ended.",
        [ProblemCodes.SessionSuperseded] = "You signed in somewhere else.",
        [ProblemCodes.SessionTimedOut] = "You were signed out after a period of inactivity.",
        [ProblemCodes.SessionMaxExceeded] = "Your session reached its maximum length.",
        [ProblemCodes.SessionForced] = "An administrator ended your session.",
        [ProblemCodes.MustChangeSecretKey] = "Change your password to continue.",
        [ProblemCodes.SecretKeyRules] = "The new password does not meet the rules.",
        [ProblemCodes.CurrentSecretKeyWrong] = "The current password is incorrect.",
        [ProblemCodes.PermissionDenied] = "You do not have permission to do that.",
        [ProblemCodes.BranchNotAllowed] = "You cannot work in that branch.",
        [ProblemCodes.ActivityLogUnavailable] = "This record could not be opened safely.",
        [ProblemCodes.SessionNotOpen] = "That session is not open.",
        [ProblemCodes.RoleGroupCodeTaken] = "A role group with that code already exists.",
        [ProblemCodes.RoleGroupInactive] = "That role group is already inactive.",
        [ProblemCodes.SelfAssignment] = "You cannot change your own role groups.",
        [ProblemCodes.LastAdministrator] = "This change would leave nobody able to manage users, permissions and KYC.",
        [ProblemCodes.ChangePendingExists] = "A change to this record is already waiting for approval.",
        [ProblemCodes.ChangeNotPending] = "That change is not waiting for approval.",
        [ProblemCodes.LookupCodeTaken] = "That code is already in the list.",
        [ProblemCodes.LookupCodeImmutable] = "A code cannot be changed once created.",
        [ProblemCodes.LookupCodedRow] = "The system relies on this value, so it cannot be deactivated.",
        [ProblemCodes.StaffCodeTaken] = "That staff code is already in use.",
        [ProblemCodes.StaffSelfChange] = "You cannot change your own staff record here.",
        [ProblemCodes.ReferenceCodeTaken] = "That code is already in use.",
        [ProblemCodes.ReferenceCodeImmutable] = "A code cannot be changed once created.",
        [ProblemCodes.RecordOtherBranch] = "This record belongs to another branch. Switch to that branch to change it.",
        [ProblemCodes.LocalCurrencyRequired] = "There must always be one local currency. Make another currency local instead.",
        [ProblemCodes.HolidayExists] = "A holiday is already defined for that date.",
        [ProblemCodes.HolidayBusinessDayExists] = "A branch has already worked on that date, so its holiday status cannot change.",
        [ProblemCodes.BusinessDayNotOpen] = "There is no open business day for this branch.",
        [ProblemCodes.BusinessDayPreviousOpen] = "Close the current business day before opening the next one.",
        [ProblemCodes.BusinessDayDateNotAfter] = "The new business day must come after the last one.",
        [ProblemCodes.BusinessDayNonWorkingDay] = "That date is a weekend day or a holiday.",
        [ProblemCodes.LedgerFieldLocked] = "The type and currency cannot change once the account is approved.",
        [ProblemCodes.ConcurrencyConflict] = "Someone else changed this record. Reload it and try again.",
        [ProblemCodes.PartyDuplicateIdentification] = "A customer with this identification document already exists.",
        [Banking.Domain.Supervision.SupervisionProblems.PermissionDenied] = "You cannot approve this record.",
        [Banking.Domain.Supervision.SupervisionProblems.NotPending] = "This record is not waiting for approval.",
        [Banking.Domain.Supervision.SupervisionProblems.Pending] = "This record is waiting for approval and cannot be used yet.",
        [Banking.Domain.Supervision.SupervisionProblems.RejectNotAllowed] = "This record was approved before and cannot be rejected.",
        [Banking.Domain.Supervision.SupervisionProblems.HasDependents] = "Other records use this one, so it cannot be rejected.",
        [Banking.Domain.Supervision.SupervisionProblems.SelfApproval] = "You cannot approve your own change."
    };

    public static string TitleFor(string code) => Titles.TryGetValue(code, out var title) ? title : code;

    public static IResult Result(int status, string code, IDictionary<string, object?>? extensions = null) =>
        TypedResults.Problem(statusCode: status, type: code, title: TitleFor(code), extensions: extensions);

    public static IResult Validation(IDictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, type: ProblemCodes.Validation, title: TitleFor(ProblemCodes.Validation));

    public static Task WriteAsync(HttpContext http, int status, string code, IDictionary<string, object?>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        return Result(status, code, extensions).ExecuteAsync(http);
    }
}
