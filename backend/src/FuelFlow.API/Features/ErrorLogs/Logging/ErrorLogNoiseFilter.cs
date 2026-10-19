using FuelFlow.SharedKernel.Domain;
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

        // A fuel nobody has priced yet is a state of the catalogue, not an incident: the storefront
        // lists it as unavailable and the buy path refuses it, so every attempt repeats the same
        // row. Measured on production 2026-10-19: 24 rows in seventeen seconds, from a customer
        // browsing the catalogue - which is the same lesson as the below-cost import that used to
        // write 689 rows a day and hide the failures that mattered.
        if (exception is NoPriceAvailableException)
            return true;

        return false;
    }
}
