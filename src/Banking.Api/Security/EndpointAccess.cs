using Banking.Domain.Security;

namespace Banking.Api.Security;

/// <summary>Every endpoint under <c>/api/v1</c> carries exactly one access rule: this, <see cref="AuthenticatedOnlyAttribute"/>, or AllowAnonymous.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
internal sealed class RequirePermissionAttribute : Attribute
{
    public RequirePermissionAttribute(string moduleCode, string? subModuleCode, PermissionAction action)
    {
        ModuleCode = moduleCode;
        SubModuleCode = subModuleCode;
        Action = action;
    }

    public string ModuleCode { get; }

    public string? SubModuleCode { get; }

    public PermissionAction Action { get; }
}

/// <summary>Any signed-in staff member (own profile, own session).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
internal sealed class AuthenticatedOnlyAttribute : Attribute;

/// <summary>Reachable while the user must change their secret key (plan 5.5 step 3).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
internal sealed class AllowedWhileMustChangeAttribute : Attribute;

/// <summary>Sensitive read (plan 5.6): if the activity row cannot be written, the request fails with 500.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
internal sealed class SensitiveReadAttribute : Attribute;

internal static class EndpointAccessExtensions
{
    public static TBuilder RequirePermission<TBuilder>(
        this TBuilder builder,
        string moduleCode,
        string? subModuleCode,
        PermissionAction action)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RequirePermissionAttribute(moduleCode, subModuleCode, action));

    public static TBuilder AuthenticatedOnly<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AuthenticatedOnlyAttribute());

    public static TBuilder AllowedWhileMustChange<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowedWhileMustChangeAttribute());

    public static TBuilder SensitiveRead<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SensitiveReadAttribute());
}
