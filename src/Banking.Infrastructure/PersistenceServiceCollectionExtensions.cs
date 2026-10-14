using Banking.Application.Abstractions;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Persistence.Interceptors;
using Banking.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Banking.Infrastructure;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BankingDbContext"/> with the audit footer, data change audit and activity interceptors.
    /// The API replaces the default <see cref="ICurrentUser"/> with the signed-in user.
    /// </summary>
    public static IServiceCollection AddBankingPersistence(this IServiceCollection services, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser>(static _ => SystemCurrentUser.Instance);
        services.TryAddScoped<IActivityContext, ActivityContext>();
        services.TryAddSingleton<LookupIdCache>();
        services.TryAddScoped<ActivityLogWriter>();
        services.AddScoped<AuditFooterInterceptor>();
        services.AddScoped<DataChangeAuditInterceptor>();
        services.AddScoped<ActivityLogInterceptor>();

        services.AddDbContext<BankingDbContext>((provider, options) => options
            .UseSqlServer(connectionString)
            .AddInterceptors(
                provider.GetRequiredService<AuditFooterInterceptor>(),
                provider.GetRequiredService<ActivityLogInterceptor>(),
                provider.GetRequiredService<DataChangeAuditInterceptor>()));

        return services;
    }

    /// <summary>Authentication, sessions and permissions (A2-04, A2-05). Options are bound by the host.</summary>
    public static IServiceCollection AddBankingSecurity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IPasswordHasher<StaffUser>, PasswordHasher<StaffUser>>();
        services.TryAddSingleton<PermissionCacheVersion>();
        services.TryAddSingleton<SessionStore>();
        services.TryAddScoped<PermissionResolver>();
        services.TryAddScoped<AuthService>();
        services.TryAddScoped<SessionAdmin>();
        services.TryAddScoped<RoleGroupAdmin>();
        services.TryAddScoped<StaffAdmin>();
        services.TryAddScoped<AuditQueries>();
        services.TryAddScoped<Banking.Infrastructure.Reference.LookupAdmin>();

        return services;
    }
}
