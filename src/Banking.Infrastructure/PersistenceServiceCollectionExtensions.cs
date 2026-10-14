using Banking.Application.Abstractions;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Banking.Infrastructure;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BankingDbContext"/> with the audit footer and data change audit interceptors.
    /// The API replaces the default <see cref="ICurrentUser"/> with the signed-in user (A2-04).
    /// </summary>
    public static IServiceCollection AddBankingPersistence(this IServiceCollection services, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser>(static _ => SystemCurrentUser.Instance);
        services.AddScoped<AuditFooterInterceptor>();
        services.AddScoped<DataChangeAuditInterceptor>();

        services.AddDbContext<BankingDbContext>((provider, options) => options
            .UseSqlServer(connectionString)
            .AddInterceptors(
                provider.GetRequiredService<AuditFooterInterceptor>(),
                provider.GetRequiredService<DataChangeAuditInterceptor>()));

        return services;
    }
}
