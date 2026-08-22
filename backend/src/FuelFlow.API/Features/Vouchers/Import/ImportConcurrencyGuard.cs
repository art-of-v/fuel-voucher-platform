namespace FuelFlow.Features.Vouchers.Import;

/// <summary>
/// Serialises voucher PDF imports across the whole process.
/// <para>
/// An import renders up to 200 pages to bitmaps inside the request, so peak memory is a function
/// of how many imports are in flight, not of any single request's size limit. Two admins (or one
/// admin with two browser tabs, or a retried request whose first attempt is still running) stack
/// that cost with nothing to stop them. One slot bounds it: a second import is refused rather than
/// queued, so the caller learns immediately instead of holding a request thread open.
/// </para>
/// </summary>
public sealed class ImportConcurrencyGuard : IDisposable
{
    private readonly SemaphoreSlim _slot = new(initialCount: 1, maxCount: 1);

    /// <summary>True when the caller acquired the slot and must call <see cref="Release"/>.</summary>
    public bool TryAcquire() => _slot.Wait(TimeSpan.Zero);

    public void Release() => _slot.Release();

    public void Dispose() => _slot.Dispose();
}
