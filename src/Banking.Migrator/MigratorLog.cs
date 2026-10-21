using Microsoft.Extensions.Logging;

namespace Banking.Migrator;

internal static partial class MigratorLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Schema and reference data applied.")]
    public static partial void Applied(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Migrator failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Sample data is for development databases only. Set DOTNET_ENVIRONMENT=Development to use '{Command}' (current: '{Environment}').")]
    public static partial void SampleRefused(ILogger logger, string command, string environment);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Unknown command '{Command}'. Use no argument, 'demo', or 'perf [parties] [accounts]'.")]
    public static partial void UnknownCommand(ILogger logger, string command);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Demo data skipped: the database already has customers.")]
    public static partial void DemoSkipped(ILogger logger);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Demo data added: {Parties} customers, {Accounts} accounts.")]
    public static partial void DemoApplied(ILogger logger, int parties, int accounts);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Performance data: {Parties} of {Total} customers, {Accounts} accounts.")]
    public static partial void PerformanceProgress(ILogger logger, int parties, int total, int accounts);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Performance data added: {Parties} customers, {Accounts} accounts.")]
    public static partial void PerformanceApplied(ILogger logger, int parties, int accounts);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "{Problem}")]
    public static partial void InvalidArguments(ILogger logger, string problem);
}
