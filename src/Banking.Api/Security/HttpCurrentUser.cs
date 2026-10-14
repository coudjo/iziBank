using System.Globalization;
using Banking.Application.Abstractions;

namespace Banking.Api.Security;

/// <summary>The signed-in staff member, from the validated token. The branch can be overridden by <c>X-Branch-Id</c>.</summary>
internal sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public long? StaffUserId => ReadLong(ClaimNames.Subject);

    public long? SessionId => ReadLong(ClaimNames.SessionId);

    public int? BranchId
    {
        get
        {
            var http = _accessor.HttpContext;
            if (http?.Items[BranchContextMiddleware.ItemKey] is int switched)
            {
                return switched;
            }

            var value = http?.User.FindFirst(ClaimNames.Branch)?.Value;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var branchId) ? branchId : null;
        }
    }

    private long? ReadLong(string claim)
    {
        var http = _accessor.HttpContext;
        if (http?.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var value = http.User.FindFirst(claim)?.Value;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}
