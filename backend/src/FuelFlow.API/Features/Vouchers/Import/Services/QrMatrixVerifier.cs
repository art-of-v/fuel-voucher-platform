using Net.Codecrete.QrCodeGenerator;

namespace FuelFlow.Features.Vouchers.Import;

public static class QrMatrixVerifier
{
    private const double WarningThresholdPercent = 1.0;
    private const double FailureThresholdPercent = 2.0;

    public enum VerificationResult
    {
        Passed,
        Warning,
        Failed,
        Skipped
    }

    public sealed class VerificationDetails
    {
        public VerificationResult Result { get; init; }
        public double MismatchPercent { get; init; }
        public int MismatchedModules { get; init; }
        public int TotalModules { get; init; }
        public string? SkipReason { get; init; }
    }

    /// <summary>
    /// Regenerates a QR code using the stored parameters and compares its module grid
    /// against the original matrix extracted from the PDF image during decoding.
    ///
    /// Returns:
    ///   Passed  — 0 % mismatch   → status: Imported
    ///   Warning — 0–2 % mismatch → status: VerifiedWithWarnings
    ///   Failed  —  >2 % mismatch → status: VerificationFailed
    ///   Skipped — original matrix unavailable (parameters could not be extracted)
    /// </summary>
    public static VerificationDetails Verify(
        string payload,
        bool[,]? originalMatrix,
        string? eccLevel,
        int? version,
        string? encodingMode,
        int? maskPattern)
    {
        if (originalMatrix == null)
            return new VerificationDetails { Result = VerificationResult.Skipped, SkipReason = "Original matrix not available" };

        if (version == null || maskPattern == null)
            return new VerificationDetails { Result = VerificationResult.Skipped, SkipReason = "Version or mask pattern not extracted" };

        QrCode regenerated;
        try
        {
            regenerated = QrGeneratorV2.BuildQrCode(payload, eccLevel, version, encodingMode, maskPattern);
        }
        catch
        {
            return new VerificationDetails { Result = VerificationResult.Skipped, SkipReason = "Failed to regenerate QR for comparison" };
        }

        return CompareModules(originalMatrix, regenerated);
    }

    private static VerificationDetails CompareModules(bool[,] originalMatrix, QrCode regenerated)
    {
        int originalDim = originalMatrix.GetLength(0);

        if (originalDim != regenerated.Size)
            return new VerificationDetails
            {
                Result = VerificationResult.Failed,
                MismatchPercent = 100,
                MismatchedModules = originalDim * originalDim,
                TotalModules = originalDim * originalDim,
                SkipReason = $"Size mismatch: original={originalDim} regenerated={regenerated.Size}"
            };

        int mismatched = 0;
        int total = originalDim * originalDim;

        for (int x = 0; x < originalDim; x++)
            for (int y = 0; y < originalDim; y++)
                if (originalMatrix[x, y] != regenerated.GetModule(x, y))
                    mismatched++;

        double mismatchPercent = (double)mismatched / total * 100.0;

        var result = mismatchPercent <= WarningThresholdPercent
            ? VerificationResult.Passed
            : mismatchPercent <= FailureThresholdPercent
                ? VerificationResult.Warning
                : VerificationResult.Failed;

        return new VerificationDetails
        {
            Result = result,
            MismatchPercent = mismatchPercent,
            MismatchedModules = mismatched,
            TotalModules = total
        };
    }
}
