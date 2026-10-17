using System.Security.Cryptography;
using System.Text;
using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace FuelFlow.Features.Vouchers.Security;

/// <summary>
/// Fills <see cref="FuelVoucher.QrPayloadSha256"/> from the payload at insert time.
/// </summary>
/// <remarks>
/// <para>
/// A value generator rather than something every writer has to remember, because there are two
/// failure modes and one of them is silent. Forgetting the hash outright trips the NOT NULL column
/// and the write fails loudly, which is fine. Forgetting it in only one of the paths would let
/// vouchers in without a fingerprint - and PostgreSQL treats NULLs as distinct inside a unique
/// index, so deduplication would quietly stop working with no error anywhere.
/// </para>
/// <para>
/// <b>No key is involved, and that is the point.</b> This is a fingerprint for equality, not a
/// secret: it hands back nothing, and it is indexed so it has to be computable without a cipher. That
/// is also why it can be a static generator resolved from EF's own provider rather than an injected
/// service - unlike <see cref="QrCipher"/>, it does not need anything configured to run.
/// </para>
/// <para>
/// Runs on insert only. <see cref="FuelVoucher.QrPayload"/> is the supplier's printed code, written
/// once at import and never edited afterwards, so the fingerprint cannot go stale. If that ever
/// stops being true this has to move to a SaveChanges interceptor - a modified payload with the
/// original hash would make a re-encoded voucher look like a different paper.
/// </para>
/// </remarks>
internal sealed class QrPayloadHashValueGenerator : ValueGenerator<byte[]>
{
    public override bool GeneratesTemporaryValues => false;

    public override byte[] Next(EntityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Entity is not FuelVoucher voucher)
        {
            throw new InvalidOperationException(
                $"{nameof(QrPayloadHashValueGenerator)} is registered on {nameof(FuelVoucher)} " +
                $"but was asked about a {entry.Entity.GetType().Name}.");
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(voucher.QrPayload));
    }
}