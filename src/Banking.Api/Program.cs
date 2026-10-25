using System.Globalization;
using Banking.Api.Activity;
using Banking.Api.Endpoints;
using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Abstractions;
using Banking.Application.Accounts;
using Banking.Application.Parties;
using Banking.Application.Reference;
using Banking.Application.Security;
using Banking.Application.Supervision;
using Banking.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, _, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

    var supervision = builder.Configuration.GetSection(SupervisionOptions.SectionName).Get<SupervisionOptions>()
        ?? new SupervisionOptions();
    SupervisionOptions.EnsureSafeFor(supervision, builder.Environment.EnvironmentName);

    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    if (jwt.SigningKey.Length < 32)
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey must be supplied from the environment (Jwt__SigningKey) and be at least 32 characters.");
    }

    builder.Services.Configure<SupervisionOptions>(builder.Configuration.GetSection(SupervisionOptions.SectionName));
    builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
    builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
    builder.Services.Configure<CalendarOptions>(builder.Configuration.GetSection(CalendarOptions.SectionName));
    builder.Services.Configure<PartyNumberOptions>(builder.Configuration.GetSection(PartyNumberOptions.SectionName));
    builder.Services.Configure<KycOptions>(builder.Configuration.GetSection(KycOptions.SectionName));
    builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection(UploadOptions.SectionName));
    builder.Services.Configure<AccountNumberOptions>(builder.Configuration.GetSection(AccountNumberOptions.SectionName));
    builder.Services.Configure<AccountOptions>(builder.Configuration.GetSection(AccountOptions.SectionName));
    builder.Services.Configure<Banking.Application.Transactions.TransactionOptions>(builder.Configuration.GetSection(Banking.Application.Transactions.TransactionOptions.SectionName));
    builder.Services.Configure<Banking.Application.Charges.ChargeOptions>(builder.Configuration.GetSection(Banking.Application.Charges.ChargeOptions.SectionName));

    // Fail at start-up, not on the first business-day request, if the weekend setting is wrong.
    _ = (builder.Configuration.GetSection(CalendarOptions.SectionName).Get<CalendarOptions>() ?? new CalendarOptions()).ParseWeekendDays();

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddBankingPersistence(builder.Configuration.GetConnectionString("Banking"));
    builder.Services.AddBankingSecurity();
    builder.Services.AddBankingReference();
    builder.Services.AddBankingParties();
    builder.Services.AddSingleton<TokenIssuer>();
    builder.Services.AddBankingJwt(jwt);
    // Numbers are numbers on the wire; this also keeps OpenAPI from typing integers as "number | string".
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict);
    builder.Services.AddExceptionHandler<BusinessRuleExceptionHandler>();
    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi();

    // Behind nginx the client address arrives in X-Forwarded-For. Only the proxies listed in Network:TrustedProxies
    // (CIDR ranges) are believed, so a client cannot claim another address (A6-02). With none listed, the direct
    // connection address is used, as before.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        foreach (var range in builder.Configuration.GetSection("Network:TrustedProxies").Get<string[]>() ?? [])
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(range));
        }
    });

    if (builder.Configuration.GetValue("Sessions:SweeperEnabled", true))
    {
        builder.Services.AddHostedService<SessionSweeperService>();
    }

    var app = builder.Build();

    app.UseForwardedHeaders();

    // API responses carry personal data (customer details, photos, document images) and are never cacheable by the
    // browser or a proxy (A6-02).
    app.Use(static (http, next) =>
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return next(http);
    });

    app.UseExceptionHandler();
    app.UseRouting();
    app.UseAuthentication();
    app.UseMiddleware<BranchContextMiddleware>();
    app.UseMiddleware<ActivityLoggingMiddleware>();
    app.UseAuthorization();

    // The API description is served in Development, and when OpenApi:Expose is set (scripts/export-openapi.sh).
    // Production does not publish it (A6-02).
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue("OpenApi:Expose", false))
    {
        app.MapOpenApi();
    }

    var api = app.MapGroup("/api/v1")
        .RequireAuthorization()
        .AddEndpointFilter<AccessFilter>();

    api.MapAuthEndpoints();
    api.MapSessionEndpoints();
    api.MapRoleGroupEndpoints();
    api.MapAdminEndpoints();
    api.MapReferenceEndpoints();
    api.MapLedgerEndpoints();
    api.MapLedgerSettingsEndpoints();
    api.MapChargeEndpoints();
    api.MapTransactionEndpoints();
    api.MapProductEndpoints();
    api.MapAccountEndpoints();
    api.MapPartyEndpoints();
    api.MapPartyDocumentEndpoints();
    api.MapPartyProfileEndpoints();

    await app.RunAsync().ConfigureAwait(false);
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "Host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

#pragma warning disable CA1515
public partial class Program;
#pragma warning restore CA1515
