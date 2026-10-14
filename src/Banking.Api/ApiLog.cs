namespace Banking.Api;

internal static partial class ApiLog
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Error, Message = "Activity log row could not be written.")]
    public static partial void ActivityWriteFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 101, Level = LogLevel.Error, Message = "Session sweep failed.")]
    public static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information, Message = "Session sweep closed {Count} sessions.")]
    public static partial void SweepClosed(ILogger logger, int count);
}
