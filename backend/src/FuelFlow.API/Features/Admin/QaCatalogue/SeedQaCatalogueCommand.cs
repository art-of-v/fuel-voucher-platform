namespace FuelFlow.Features.Admin.QaCatalogue;

/// <summary>
/// Stocks the rehearsal catalogue: mirrors every live size into the <c>qa</c> pool and puts
/// <paramref name="VouchersPerPackage"/> sellable vouchers behind each of them.
/// </summary>
/// <remarks>
/// This exists because the App Store reviewer has to be able to browse and buy while real stock is
/// empty, and because the SQL seeds that used to do it cannot run any more: <c>qr_payload</c> is
/// AES-GCM ciphertext written through <c>QrCipher</c>, so a row built by hand has no readable code.
/// The seed therefore lives here, where the key already is.
/// </remarks>
public sealed record SeedQaCatalogueCommand(int? VouchersPerPackage);

/// <summary>What the run did, so the operator can tell a first seed from a no-op re-run.</summary>
public sealed record SeedQaCatalogueResult(
    int NominalsCreated,
    int VouchersCreated,
    int Packages,
    int VouchersPerPackage);
