using Banking.Application.Errors;
using Banking.Domain.Supervision;
using Banking.Infrastructure.Security;

namespace Banking.Api.Problems;

/// <summary>Turns a failed service result into the HTTP problem for its code.</summary>
internal static class ResultProblems
{
    public static IResult From<T>(OperationResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Extensions is { Count: > 0 } extensions)
        {
            return ApiProblems.Result(
                StatusCodes.Status409Conflict,
                result.ProblemCode!,
                extensions.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
        }

        return From(result.ProblemCode!, result.Errors);
    }

    public static IResult From(string problemCode, IReadOnlyDictionary<string, string[]>? errors) => problemCode switch
    {
        ProblemCodes.Validation => ApiProblems.Validation(
            errors?.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, string[]>(StringComparer.Ordinal)),
        ProblemCodes.NotFound => ApiProblems.Result(StatusCodes.Status404NotFound, ProblemCodes.NotFound),
        ProblemCodes.LookupCodeImmutable or ProblemCodes.ReferenceCodeImmutable =>
            ApiProblems.Result(StatusCodes.Status400BadRequest, problemCode),
        SupervisionProblems.PermissionDenied => ApiProblems.Result(StatusCodes.Status403Forbidden, problemCode),
        _ => ApiProblems.Result(StatusCodes.Status409Conflict, problemCode)
    };
}
