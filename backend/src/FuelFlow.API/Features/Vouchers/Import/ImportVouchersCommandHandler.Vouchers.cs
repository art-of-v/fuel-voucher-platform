using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.Import;

public sealed partial class ImportVouchersCommandHandler
{
    private async Task ProcessParsedVoucherAsync(
        ParsedVoucher parsed,
        PageRender page,
        ImportRunState state)
    {
        var import = state.Import;
        var cancellationToken = state.CancellationToken;

        bool isValid = parsed.Confidence >= 80 &&
                       parsed.FuelTypeId.HasValue &&
                       parsed.Liters > 0 &&
                       parsed.ExpirationDate != default &&
                       !string.IsNullOrEmpty(parsed.VoucherNumber) &&
                       !string.IsNullOrEmpty(parsed.QrPayload);

        if (!isValid)
        {
            string reason;
            if (!parsed.FuelTypeId.HasValue)
            {
                // The QR product code (digits before the first '$') is a fuel-SKU
                // indicator, not redeemable bearer material, so it is safe to surface
                // for triage. When present but unresolved it means the code has no
                // matching fuel_types row for this Brand.
                var productCode = OkkoFuelClassifier.TryGetProductCode(parsed.QrPayload);
                reason = productCode is null
                    ? "Fuel type could not be determined from voucher text or QR code."
                    : $"Fuel type could not be determined: QR product code '{productCode}' has no matching fuel type for this Brand.";
            }
            else
            {
                reason = $"Confidence: {parsed.Confidence}. FuelTypeId: {parsed.FuelTypeId}, Liters: {parsed.Liters}, Expiry: {parsed.ExpirationDate}, Number: {parsed.VoucherNumber}, QR: {QrPayloadFingerprint.Describe(parsed.QrPayload)}";
            }
            var errMsg = $"Voucher failed validation. {reason}";
            _logger.LogWarning("Page {PageNumber}: {ErrorMessage}", page.PageNumber, errMsg);

            _context.VoucherImportErrors.Add(new VoucherImportError
            {
                Id = Guid.CreateVersion7(),
                ImportId = import.Id,
                PageNumber = page.PageNumber,
                VoucherNumber = string.IsNullOrEmpty(parsed.VoucherNumber) ? null : parsed.VoucherNumber,
                ErrorMessage = errMsg,
                RawText = PrepareRawTextForStorage(parsed.RawText, parsed.QrPayload),
                CreatedAtUtc = SystemClock.UtcNow
            });
            import.FailedCount++;
            return;
        }

        // Hard integrity check: the brand printed on the paper and the fuel it sells must
        // agree. `brand` is the brand and `fuel_types.brand_id` is the same fact, so a
        // mismatch means one of the two was mis-read — and it would otherwise be stored as
        // truth, because `brand` is free text with nothing in the schema to contradict
        // it. Such a voucher matches nothing on renewal (the two fields are compared
        // together) and no fuel-type pricing describes it, so it silently drops out of the
        // business rather than being visibly wrong.
        //
        // A fuel type with no row at all is left alone: that is an unresolved SKU, already
        // reported above, and the foreign key is what catches it.
        if (state.BrandSlugByFuelType.TryGetValue(parsed.FuelTypeId.GetValueOrDefault(), out var fuelBrand) &&
            !BrandFuelPairing.IsConsistent(parsed.Brand, fuelBrand))
        {
            var errMsg = $"Brand '{parsed.Brand}' does not sell fuel '{parsed.FuelTypeId}', " +
                         $"which belongs to brand '{fuelBrand}'. The paper says one brand and " +
                         "the fuel type says another; re-scan or pick the correct fuel type.";
            _logger.LogWarning("Page {PageNumber}: {ErrorMessage}", page.PageNumber, errMsg);

            _context.VoucherImportErrors.Add(new VoucherImportError
            {
                Id = Guid.CreateVersion7(),
                ImportId = import.Id,
                PageNumber = page.PageNumber,
                VoucherNumber = string.IsNullOrEmpty(parsed.VoucherNumber) ? null : parsed.VoucherNumber,
                ErrorMessage = errMsg,
                RawText = PrepareRawTextForStorage(parsed.RawText, parsed.QrPayload),
                CreatedAtUtc = SystemClock.UtcNow
            });
            import.FailedCount++;
            return;
        }

        // Hard integrity check: the QR payload must contain the voucher number.
        // A mismatch means the QR was decoded from a neighbour's cell (wrong pairing).
        // Reject immediately so the operator can re-import the affected vouchers.
        bool integrityPassed = parsed.QrPayload.Contains(parsed.VoucherNumber);
        if (!integrityPassed)
        {
            var errMsg = $"QR payload does not contain voucher number '{parsed.VoucherNumber}'. " +
                         $"Likely decoded from wrong region (payload {QrPayloadFingerprint.Describe(parsed.QrPayload)}). Re-import required.";
            _logger.LogWarning("Page {PageNumber}: {ErrorMessage}", page.PageNumber, errMsg);

            _context.VoucherImportErrors.Add(new VoucherImportError
            {
                Id = Guid.CreateVersion7(),
                ImportId = import.Id,
                PageNumber = page.PageNumber,
                VoucherNumber = parsed.VoucherNumber,
                ErrorMessage = errMsg,
                RawText = PrepareRawTextForStorage(parsed.RawText, parsed.QrPayload),
                CreatedAtUtc = SystemClock.UtcNow
            });
            import.FailedCount++;
            return;
        }

        // Match on the SHA-256, not on the payload. This used to be
        // `v.QrPayload == parsed.QrPayload`, which compared plaintext in SQL - possible only
        // because the column was stored in the clear, and it was the reason the same paper
        // could be looked up at all. Now that the payload is encrypted the comparison
        // would translate to decrypt-every-row-and-compare: correct, but a sequential scan
        // of the whole table on every voucher of every import, which is the cost this index
        // was supposed to avoid. The hash is indexed, and a collision would need a SHA-256
        // preimage.
        var payloadHash = _qrCipher.Sha256(parsed.QrPayload);

        bool existsInDb = await _context.FuelVouchers.AnyAsync(
            v => v.VoucherNumber == parsed.VoucherNumber || v.QrPayloadSha256 == payloadHash,
            cancellationToken);

        bool existsInBatch = state.AddedNumbers.Contains(parsed.VoucherNumber) || state.AddedPayloads.Contains(parsed.QrPayload);

        if (existsInDb || existsInBatch)
        {
            _logger.LogInformation("Voucher Number {VoucherNumber} (or QR payload) already exists. Skipping duplicate.", parsed.VoucherNumber);
            import.DuplicateCount++;
            return;
        }

        // ── QrParameters deduplication ──────────────────────────────────────
        var eccKey = parsed.QrEccLevel ?? string.Empty;
        var cacheKey = $"{eccKey}|{parsed.QrVersion?.ToString() ?? string.Empty}|{parsed.QrMaskPattern?.ToString() ?? string.Empty}|{parsed.QrEncodingMode ?? string.Empty}";

        if (!state.QrParamsCache.TryGetValue(cacheKey, out var qrParams))
        {
            // Match what is persisted, not a null. ecc_level is NOT NULL, so `p.EccLevel == null`
            // compiled to `ecc_level IS NULL`, could never match, and every import
            // containing one unreadable QR appended another ('', NULL, NULL, NULL)
            // row to qr_parameters. The unique index could not catch it either:
            // Postgres treats NULLs as distinct in a btree.
            var normalizedEcc = eccKey;
            qrParams = await _context.QrParameters
                .FirstOrDefaultAsync(
                    p => p.EccLevel == normalizedEcc &&
                         p.Version == parsed.QrVersion &&
                         p.MaskPattern == parsed.QrMaskPattern &&
                         p.EncodingMode == parsed.QrEncodingMode,
                    cancellationToken);

            if (qrParams == null)
            {
                qrParams = new QrParameters
                {
                    Id = Guid.CreateVersion7(),
                    EccLevel = normalizedEcc,
                    Version = parsed.QrVersion,
                    MaskPattern = parsed.QrMaskPattern,
                    EncodingMode = parsed.QrEncodingMode,
                    CreatedAtUtc = SystemClock.UtcNow
                };
                _context.QrParameters.Add(qrParams);
                _logger.LogInformation(
                    "Created new QrParameters row (ECC={EccLevel}, Version={Version}, Mask={MaskPattern}, Mode={EncodingMode}).",
                    qrParams.EccLevel, qrParams.Version, qrParams.MaskPattern, qrParams.EncodingMode);
            }

            state.QrParamsCache[cacheKey] = qrParams;
        }
        // ────────────────────────────────────────────────────────────────────

        var voucher = new FuelVoucher
        {
            Id = Guid.CreateVersion7(),
            Brand = parsed.Brand,
            // The brand's key beside its name. The pairing is checked below against
            // the fuel's own brand, so the two copies name the same network.
            BrandId = state.BrandIdByFuelType.GetValueOrDefault(parsed.FuelTypeId.GetValueOrDefault()),
            FuelTypeId = parsed.FuelTypeId.GetValueOrDefault(),
            Liters = parsed.Liters,
            // The date printed on the supplier's document is the real term. It is
            // both columns at import: nothing has been sold yet, so the customer is
            // getting the whole thing. Shortening happens later, at fulfilment, when
            // a term is actually chosen - never here, and never past this date.
            SupplierExpirationDate = parsed.ExpirationDate,
            CustomerExpirationDate = parsed.ExpirationDate,
            VoucherNumber = parsed.VoucherNumber,
            QrPayload = parsed.QrPayload,
            // Carries the deduplication the encrypted column cannot. Set from the
            // same value the duplicate check above matched on, so the two cannot
            // disagree about what this voucher is.
            QrPayloadSha256 = payloadHash,
            CreatedAtUtc = SystemClock.UtcNow,
            ImportJobId = import.Id,
            // Who issued this paper. One PDF is one supplier's delivery, so it is decided
            // once by the operator before upload rather than guessed per voucher.
            SupplierId = state.SupplierId,
            // Copied from the supplier above, not chosen here. It is immutable on the
            // voucher, so this is the moment it is decided: correcting a wrong route
            // afterwards means replacing the vouchers, not editing them.
            AcquisitionSource = state.AcquisitionSource,
            QrParametersId = qrParams.Id,
            UpdatedAtUtc = SystemClock.UtcNow
        };

        var verification = QrMatrixVerifier.Verify(
            parsed.QrPayload,
            parsed.OriginalQrMatrix,
            parsed.QrEccLevel,
            parsed.QrVersion,
            parsed.QrEncodingMode,
            parsed.QrMaskPattern);

        voucher.Status = verification.Result switch
        {
            QrMatrixVerifier.VerificationResult.Passed  => SharedModels.VoucherStatus.Imported,
            QrMatrixVerifier.VerificationResult.Skipped => SharedModels.VoucherStatus.Imported,
            QrMatrixVerifier.VerificationResult.Warning => SharedModels.VoucherStatus.VerifiedWithWarnings,
            QrMatrixVerifier.VerificationResult.Failed  => SharedModels.VoucherStatus.VerificationFailed,
            _ => SharedModels.VoucherStatus.Imported
        };

        if (verification.Result != QrMatrixVerifier.VerificationResult.Skipped)
        {
            // The verifier counts modules, and its answer is a double. The column is a
            // decimal, and the rounding happens on the decimal - a double rounded to
            // four places and then converted is a different number from a decimal
            // rounded to four places.
            voucher.VerificationMismatchPercent = Math.Round((decimal)verification.MismatchPercent, 4);
            voucher.VerificationMismatchedModules = verification.MismatchedModules;
            voucher.VerificationTotalModules = verification.TotalModules;
        }

        if (verification.Result == QrMatrixVerifier.VerificationResult.Warning)
        {
            import.VerifiedWithWarningsCount++;
            _logger.LogWarning(
                "QR verification warning for voucher {VoucherNumber}: {Mismatched}/{Total} modules mismatched ({Percent:F2}%)",
                parsed.VoucherNumber, verification.MismatchedModules, verification.TotalModules, verification.MismatchPercent);
        }
        else if (verification.Result == QrMatrixVerifier.VerificationResult.Failed)
        {
            import.VerificationFailedCount++;
            _logger.LogError(
                "QR verification failed for voucher {VoucherNumber}: {Mismatched}/{Total} modules mismatched ({Percent:F2}%). Reason: {Reason}",
                parsed.VoucherNumber, verification.MismatchedModules, verification.TotalModules, verification.MismatchPercent, verification.SkipReason);
        }
        else if (verification.Result == QrMatrixVerifier.VerificationResult.Skipped)
        {
            _logger.LogInformation(
                "QR verification skipped for voucher {VoucherNumber}: {Reason}",
                parsed.VoucherNumber, verification.SkipReason);
        }

        _context.FuelVouchers.Add(voucher);

        // D-13. The voucher's first entry into the world: paper arrived from a supplier
        // and is now ours, sitting in the pool. This is the one movement the ledger
        // cannot do without - every voucher's life starts here - and it is why
        // from_voucher_id could not stay NOT NULL in the first design.
        //
        // No batch_id: an import is a delivery, not a settlement, and `bulk_issuance`
        // is the other thing - releasing an already-imported batch to its vouchers, which
        // is a different event. `fuel_vouchers.import_job_id` already names the import, so
        // a second copy here would be a third thing to keep in step.
        //
        // No event either. A brand-new row has no status to have changed FROM, so there is
        // no transition to record; its starting status is the row itself.
        _context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.CreateVersion7(),
            Kind = VoucherMovementKinds.Deposit,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Supplier,
            ToLocation = VoucherLocations.Pool,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = state.Request.ActingUserId,
            OccurredAtUtc = voucher.CreatedAtUtc
        });

        state.AddedNumbers.Add(voucher.VoucherNumber);
        state.AddedPayloads.Add(voucher.QrPayload);
        import.ImportedCount++;

        state.ImportedVouchersByProvider[voucher.Brand] =
            state.ImportedVouchersByProvider.GetValueOrDefault(voucher.Brand) + 1;

        _logger.LogInformation("QR Decode Success: Decoded QR code for Voucher {VoucherNumber}.", parsed.VoucherNumber);
    }
}
