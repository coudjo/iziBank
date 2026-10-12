using Microsoft.Extensions.Logging;

namespace Banking.Migrator;

internal static partial class MigratorLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Schema and reference data applied.")]
    public static partial void Applied(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Migrator failed.")]
    public static partial void Failed(ILogger logger, Exception exception);
}
