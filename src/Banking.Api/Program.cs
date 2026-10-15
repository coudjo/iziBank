using System.Globalization;
using Banking.Api.Activity;
using Banking.Api.Endpoints;
using Banking.Api.Problems;
using Banking.Api.Security;
using Banking.Application.Abstractions;
using Banking.Application.Parties;
using Banking.Application.Reference;
using Banking.Application.Security;
using Banking.Application.Supervision;
using Banking.Infrastructure;
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

    if (builder.Configuration.GetValue("Sessions:SweeperEnabled", true))
    {
        builder.Services.AddHostedService<SessionSweeperService>();
    }

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseRouting();
    app.UseAuthentication();
    app.UseMiddleware<BranchContextMiddleware>();
    app.UseMiddleware<ActivityLoggingMiddleware>();
    app.UseAuthorization();

    app.MapOpenApi();

    var api = app.MapGroup("/api/v1")
        .RequireAuthorization()
        .AddEndpointFilter<AccessFilter>();

    api.MapAuthEndpoints();
    api.MapSessionEndpoints();
    api.MapRoleGroupEndpoints();
    api.MapAdminEndpoints();
    api.MapReferenceEndpoints();
    api.MapLedgerEndpoints();
    api.MapPartyEndpoints();

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
