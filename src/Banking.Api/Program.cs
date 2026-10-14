using System.Globalization;
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
    builder.Services.Configure<SupervisionOptions>(builder.Configuration.GetSection(SupervisionOptions.SectionName));

    builder.Services.AddBankingPersistence(builder.Configuration.GetConnectionString("Banking"));
    builder.Services.AddOpenApi();

    var app = builder.Build();

    app.MapOpenApi();

    await app.RunAsync().ConfigureAwait(false);
}
catch (Exception exception)
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
