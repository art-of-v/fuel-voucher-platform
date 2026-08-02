using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.ErrorLogs.Logging;

/// <summary>
/// Classifies log records that are expected/actionable-noise and should not be persisted.
/// </summary>
public static class ErrorLogNoiseFilter
{
    /// <summary>
    /// Returns true when the record should be dropped from the error log.
    /// </summary>
    public static bool IsNoise(LogLevel level, Exception? exception)
    {
        // Client disconnects / request aborts (e.g. HTTP 499) surface as
        // OperationCanceledException across the pipeline and are not actionable.
        if (exception is OperationCanceledException)
            return true;

        return false;
    }
}
