using System.ComponentModel.DataAnnotations.Schema;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Vouchers;

public class FuelVoucher
{
    public Guid Id { get; set; }
    public string Brand { get; set; } = null!;

    /// <summary>
    /// The brand's id - the key the text above is a copy of.
    /// </summary>
    /// <remarks>
    /// The brand is written twice on this row: as the name the application and the importers speak,
    /// and - from the key - as the id of the brand itself. The name is what a report groups by and
    /// what a parser reads; the id is what the database can check. A composite foreign key over
    /// (fuel type, brand) says the two copies are the same fact; a trigger can only ask.
    /// <para>
    /// The key is required, and the database says so: the composite foreign key
    /// <c>(fuel_type_id, brand_id) → fuel_types(id, brand_id)</c> is what refuses a row whose brand
    /// is not its fuel's brand. A nullable key would be a key the database does not check, because a
    /// foreign key with a NULL in it is not compared at all.
    /// </para>
    /// </remarks>
    public Guid BrandId { get; set; }
    public Guid FuelTypeId { get; set; }
    public FuelTypeEntity? FuelType { get; set; }
    public decimal Liters { get; set; }

    /// <summary>
    /// The real term of the supplier's voucher, read verbatim from the printed document at import.
    /// It is the hard ceiling on everything we may promise: it bounds how long stock stays sellable
    /// and how far a customer's renewal may extend. Set once at import and never moved afterwards -
    /// a supplier voucher cannot be extended by us.
    /// </summary>
    public DateOnly SupplierExpirationDate { get; set; }

    /// <summary>
    /// What the customer was actually sold, and the date their wallet shows. Normally equal to
    /// <see cref="SupplierExpirationDate"/>; it is a separate column because we sell short terms -
    /// a voucher with three months of real life may be sold with one week, and only this date moves
    /// when the customer pays to extend it.
    /// </summary>
    public DateOnly CustomerExpirationDate { get; set; }

    public string VoucherNumber { get; set; } = null!;

    /// <summary>
    /// The supplier's QR code, verbatim. Plaintext in memory only.
    /// </summary>
    /// <remarks>
    /// Persisted as AES-GCM ciphertext by a value converter, so every read and write in the
    /// application goes through this property unchanged and no caller needs to know. What sits in the
    /// column is nonce, tag and ciphertext concatenated - see <c>QrCipher</c>.
    /// </remarks>
    public string QrPayload { get; set; } = null!;

    /// <summary>
    /// SHA-256 of <see cref="QrPayload"/>, carrying the deduplication the ciphertext cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The uniqueness index used to sit on <see cref="QrPayload"/> itself, which only worked because
    /// the payload was plaintext. Encrypted bytes cannot be compared - AES-GCM produces a fresh
    /// nonce every time, so the same QR encrypts differently every time - so the index moved here.
    /// </para>
    /// <para>
    /// A hash is not reversible, so this gives up nothing the ciphertext was not already giving up.
    /// It is still a fingerprint of a redeemable code, so it is not something to log or expose.
    /// </para>
    /// </remarks>
    public byte[]? QrPayloadSha256 { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When this voucher was handed to a worker by a company owner. Set once, on issue; never moved
    /// afterwards.
    /// </summary>
    /// <remarks>
    /// A column of its own rather than a reading of <see cref="UpdatedAtUtc"/>, because every mutation
    /// - a block, a recall, a deactivate - clobbers <c>UpdatedAtUtc</c>. A worker usage report that
    /// answered "when did I receive this" off <c>UpdatedAtUtc</c> would move the date every time the
    /// owner touched it. See #150 decision 2.
    /// </remarks>
    public DateTime? IssuedAtUtc { get; set; }

    /// <summary>
    /// When this voucher was actually refuelled with. Set once, when it is marked used; never moved
    /// afterwards. Null while the voucher is still unused.
    /// </summary>
    public DateTime? UsedAtUtc { get; set; }

    public VoucherStatus Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public Guid? WorkerUserId { get; set; }
    public Guid? ImportJobId { get; set; }

    /// <summary>
    /// Who holds this voucher, derived by the database from the three columns above. Never written by
    /// the application - it is a STORED generated column.
    /// <para>
    /// The reason it is a column and not a helper method is that "who holds this voucher" was a
    /// convention that every handler re-derived, slightly differently, and the disagreements were not
    /// theoretical. The QR endpoints disagreed about who may redeem a voucher's payload: the wallet
    /// list matched the owner while the PNG endpoint matched the worker, so the owner who had just
    /// issued that fuel could still spend it. Expressing the answer once, in the database, is what
    /// stops the next pair of endpoints from disagreeing again.
    /// </para>
    /// </summary>
    public VoucherHolderKind HolderKind { get; private set; }

    /// <summary>
    /// The order this voucher belongs to. Set whenever the voucher is placed in someone's hands
    /// (purchase, renewal replacement, company issuance) and left null while it is operator stock,
    /// because stock has not been handed to anyone yet.
    /// </summary>
    public Guid? OrderId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Who issued THIS voucher. Distinct from <see cref="Brand"/>, which is the brand: the same brand is
    /// bought from several suppliers, and an exchange goes back to the supplier the voucher came from, so the
    /// link cannot be derived from the brand.
    /// </summary>
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>
    /// What this one voucher actually cost us, in UAH per liter — the rate stamped on it by its import batch.
    /// Per voucher rather than per batch because vouchers of one brand carry different prices: bought on
    /// different terms, and reduced when a customer pays to extend them. Null until the cost is entered, which
    /// is what the activation gate refuses to sell. Denomination-independent rate, never a package total.
    /// </summary>
    public decimal? CostPerLiter { get; set; }

    public Guid? QrParametersId { get; set; }
    public QrParameters? QrParameters { get; set; }

    public User? AssignedToUser { get; set; }
    public LegalEntity? LegalEntity { get; set; }
    public User? WorkerUser { get; set; }
    public VoucherImport? ImportJob { get; set; }

    /// <summary>
    /// Which pool this voucher belongs to: <see cref="DataKinds.Real"/> or
    /// <see cref="DataKinds.Qa"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="DataKinds.Qa"/> marks an emulated voucher rather than fuel the operator actually
    /// paid a supplier for. Never set on imported stock: an emulated voucher's code does not exist
    /// at the Brand, so one that reaches a customer is fuel nobody can redeem. The value exists so
    /// test runs can be found, cleaned up and kept out of the numbers a real batch produces.
    /// <para>
    /// Replaces the <c>is_test_data boolean</c> this used to be; see <see cref="DataKinds"/> for why
    /// a bare boolean on a money row was the wrong shape.
    /// </para>
    /// </remarks>
    public string DataKind { get; set; } = DataKinds.Real;

    /// <summary>
    /// Which supply route this voucher was acquired on: <see cref="AcquisitionSources.Direct"/> or
    /// <see cref="AcquisitionSources.Partner"/>. See <see cref="AcquisitionSources"/>.
    /// </summary>
    /// <remarks>
    /// Immutable, and enforced by a database trigger rather than left to convention. The cost of a
    /// voucher is a function of where it came from, so an edited value does not merely relabel a row
    /// - it moves real money between two reports that are supposed to be separate. There is no edit
    /// path in the admin for this, and the trigger is what makes that a property of the schema rather
    /// than of the current UI.
    /// <para>
    /// Defaults to <see cref="AcquisitionSources.Direct"/> because that is what every voucher
    /// acquired so far was, which is what lets the column be <c>NOT NULL</c> while the write paths
    /// that do not care about this axis stay silent.
    /// </para>
    /// </remarks>
    public string AcquisitionSource { get; set; } = AcquisitionSources.Default;

    /// <summary>
    /// Whether this voucher is emulated rehearsal data. A view over <see cref="DataKind"/> so the
    /// pool reads as a pool at the call site; assigning it still works and is how the admin write
    /// paths state their intent.
    /// </summary>
    [NotMapped]
    public bool IsTestData
    {
        get => DataKind == DataKinds.Qa;
        set => DataKind = value ? DataKinds.Qa : DataKinds.Real;
    }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// How much of the printed QR matrix did not match the payload the importer decoded, as a percentage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A decimal, not a double. Binary floating point cannot represent 0.1 exactly, and a percentage
    /// is compared against a threshold - a value that reads 10.0% and is stored as 0.1 in binary is a
    /// value that is not the number anyone wrote down.
    /// </para>
    /// <para>
    /// It was the only money-adjacent column in the schema that was a float. Every other one is
    /// <c>numeric</c> with an explicit scale.
    /// </para>
    /// <para>
    /// <b>This is decode integrity, not supplier authenticity.</b> A zero mismatch means the image and the
    /// payload agree; it does not mean the supplier issued the voucher. The payload has no supplier
    /// signature, serial or registry entry to check, and a counterfeit with a well-formed payload produces
    /// a matching matrix by construction.
    /// </para>
    /// </remarks>
    public decimal? MatrixMismatchPercent { get; set; }

    /// <summary>How many QR modules disagreed between the printed matrix and the regenerated one.</summary>
    public int? MatrixMismatchedModules { get; set; }

    /// <summary>How many QR modules were compared.</summary>
    public int? MatrixTotalModules { get; set; }
}
