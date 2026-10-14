using Banking.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;

namespace Banking.Api.Problems;

/// <summary>A <see cref="BusinessRuleException"/> thrown anywhere in a request becomes a 409 problem with its code.</summary>
internal sealed class BusinessRuleExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not BusinessRuleException rule)
        {
            return false;
        }

        await ApiProblems.WriteAsync(httpContext, StatusCodes.Status409Conflict, rule.ProblemCode).ConfigureAwait(false);
        return true;
    }
}
