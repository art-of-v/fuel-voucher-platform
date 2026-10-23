using Net.Codecrete.QrCodeGenerator;

namespace FuelFlow.Features.Vouchers.Import;

/// <summary>
/// Checks the <b>decode integrity</b> of a voucher's QR code: the module grid read from the PDF image is
/// compared with a QR code regenerated from the decoded payload and the parameters extracted beside it.
/// </summary>
/// <remarks>
/// <para>
/// A match proves exactly one thing: the printed matrix encodes the payload this importer stored, and the
/// two did not diverge while the image was decoded and re-encoded. It does <b>not</b> prove that the
/// supplier printed the voucher, that the payload was ever registered with the supplier, or that the
/// voucher is redeemable. A counterfeit that carries a well-formed payload produces a matching matrix by
/// construction. Supplier provenance is the operator's declaration at upload; no cryptographic or registry
/// check exists in the payload, so nothing here may be reported as authenticity.
/// </para>
/// <para>
/// The result is an import QA signal, not stock state. A warning or a mismatch is surfaced so the batch can
/// be reconciled with the supplier's delivery note and invoice; it is not a fraud verdict.
/// </para>
/// </remarks>
public static class QrDecodeIntegrityVerifier
{
    /// <summary>Up to this mismatch the regenerated matrix is treated as a match (image noise).</summary>
    public const double WarningThresholdPercent = 1.0;

    /// <summary>Above this mismatch the matrix is reported as a mismatch and the voucher stays out of stock.</summary>
    public const double MismatchThresholdPercent = 2.0;

    public enum DecodeIntegrityResult
    {
        /// <summary>The matrix agrees with the payload within <see cref="WarningThresholdPercent"/>.</summary>
        Matched,

        /// <summary>The matrix disagrees in more than 1% and up to 2% of modules - import QA warning.</summary>
        Warning,

        /// <summary>The matrix disagrees in more than 2% of modules, or its size cannot be the payload's.</summary>
        Mismatch,

        /// <summary>The original matrix or the QR parameters were not available, so nothing was compared.</summary>
        NotChecked
    }

    public sealed class DecodeIntegrityDetails
    {
        public DecodeIntegrityResult Result { get; init; }
        public double MatrixMismatchPercent { get; init; }
        public int MatrixMismatchedModules { get; init; }
        public int MatrixTotalModules { get; init; }
        public string? SkipReason { get; init; }
    }

    /// <summary>
    /// Regenerates a QR code using the stored parameters and compares its module grid against the original
    /// matrix extracted from the PDF image during decoding.
    /// </summary>
    public static DecodeIntegrityDetails Check(
        string payload,
        bool[,]? originalMatrix,
        string? eccLevel,
        int? version,
        string? encodingMode,
        int? maskPattern)
    {
        if (originalMatrix == null)
            return new DecodeIntegrityDetails { Result = DecodeIntegrityResult.NotChecked, SkipReason = "Original matrix not available" };

        if (version == null || maskPattern == null)
            return new DecodeIntegrityDetails { Result = DecodeIntegrityResult.NotChecked, SkipReason = "Version or mask pattern not extracted" };

        QrCode regenerated;
        try
        {
            regenerated = QrGeneratorV2.BuildQrCode(payload, eccLevel, version, encodingMode, maskPattern);
        }
        catch (Exception ex)
        {
            return new DecodeIntegrityDetails { Result = DecodeIntegrityResult.NotChecked, SkipReason = $"Failed to regenerate QR for comparison: {ex.Message}" };
        }

        return CompareModules(originalMatrix, regenerated);
    }

    private static DecodeIntegrityDetails CompareModules(bool[,] originalMatrix, QrCode regenerated)
    {
        int originalDim = originalMatrix.GetLength(0);

        if (originalDim != regenerated.Size)
            return new DecodeIntegrityDetails
            {
                Result = DecodeIntegrityResult.Mismatch,
                MatrixMismatchPercent = 100,
                MatrixMismatchedModules = originalDim * originalDim,
                MatrixTotalModules = originalDim * originalDim,
                SkipReason = $"Size mismatch: original={originalDim} regenerated={regenerated.Size}"
            };

        int mismatched = 0;
        int total = originalDim * originalDim;
        (int X, int Y) firstMismatch = (-1, -1);

        for (int x = 0; x < originalDim; x++)
            for (int y = 0; y < originalDim; y++)
                if (originalMatrix[x, y] != regenerated.GetModule(x, y))
                {
                    mismatched++;
                    if (firstMismatch.X == -1)
                        firstMismatch = (x, y);
                }

        double mismatchPercent = (double)mismatched / total * 100.0;

        var result = mismatchPercent <= WarningThresholdPercent
            ? DecodeIntegrityResult.Matched
            : mismatchPercent <= MismatchThresholdPercent
                ? DecodeIntegrityResult.Warning
                : DecodeIntegrityResult.Mismatch;

        if (mismatched > 0)
        {
            Console.WriteLine(
                $"[QrDecodeIntegrityVerifier] Version={regenerated.Size} " +
                $"Mismatches={mismatched}/{total} First mismatch at (x={firstMismatch.X}, y={firstMismatch.Y}) " +
                $"original={originalMatrix[firstMismatch.X, firstMismatch.Y]} regenerated={regenerated.GetModule(firstMismatch.X, firstMismatch.Y)}");
        }

        return new DecodeIntegrityDetails
        {
            Result = result,
            MatrixMismatchPercent = mismatchPercent,
            MatrixMismatchedModules = mismatched,
            MatrixTotalModules = total
        };
    }
}
