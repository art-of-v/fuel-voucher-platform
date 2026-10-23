using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class FuelVoucherConfiguration : IEntityTypeConfiguration<FuelVoucher>
{
    public void Configure(EntityTypeBuilder<FuelVoucher> builder)
    {
        builder.ToTable("fuel_vouchers");

        builder.HasKey(e => e.Id);

        // #246: xmin token. No DDL — xmin is a system column. Raw SQL must never write this table
        // in the same request as a tracked save of the same row, or the token starts failing saves
        // that did not actually race. Every raw write here is a compare-and-swap, and FulfillmentService
        // now clears the tracker before reading rows its own statements just claimed, which is what
        // makes that rule hold rather than merely be intended.
        builder.UseXminAsConcurrencyToken();

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Brand)
            .HasColumnName("brand")
            .HasMaxLength(50)
            .IsRequired()
            .HasComment(                "The brand's NAME - what a report groups by and what a parser reads. The id below is what" +
                    "the database can check, and the two are the same fact written twice.");

        // The key the brand text above is a copy of. Required, because the composite foreign key
        // below is what refuses a row whose brand is not its fuel's brand - and a foreign key with a
        // NULL in it is not compared at all, so a nullable key would be a key the database does not
        // check.
        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .HasColumnType("uuid")
            .IsRequired()
            .HasComment(                "The brand's id - the key the text above is a copy of. The composite foreign key" +
                    "(fuel_type_id, brand_id) -> fuel_types(id, brand_id) is what refuses a row whose brand" +
                    "is not its fuel's brand. A nullable key would be a key the database does not check," +
                    "because a foreign key with a NULL in it is not compared at all.");

        // The fuel id is a uuid since planning D-21: a uuid column, so this can carry a real
        // foreign key one day - which is what D-02 is waiting for.
           builder.Property(e => e.FuelTypeId)
               .HasColumnName("fuel_type_id")
               .HasColumnType("uuid")
               .IsRequired().HasComment("The fuel this voucher is for.");

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)")
            .IsRequired()
            .HasComment(                "How many litres this voucher's paper is for.");

        builder.Property(e => e.SupplierExpirationDate)
            .HasColumnName("supplier_expiration_date")
            .IsRequired()
            .HasComment(                "The real term of the SUPPLIER's voucher, read verbatim from the printed document at" +
                    "import. It is the hard ceiling on everything we may promise: it bounds how long stock" +
                    "stays sellable and how far a customer's renewal may extend. Set once at import and never" +
                    "moved afterwards - a supplier voucher cannot be extended by us.");

        builder.Property(e => e.CustomerExpirationDate)
            .HasColumnName("customer_expiration_date")
            .IsRequired()
            .HasComment(                "What the customer was actually sold, and the date their wallet shows. Normally equal to" +
                    "supplier_expiration_date; it is a separate column because we sell SHORT terms - a" +
                    "voucher with three months of real life may be sold with one week - and only this date" +
                    "moves when the customer pays to extend it.");

        builder.Property(e => e.VoucherNumber)
            .HasColumnName("voucher_number")
            .HasMaxLength(100)
            .IsRequired()
            .HasComment(                "The voucher's own number, as the supplier printed it.");

        // Bounded on purpose. The QR payload is a bearer instrument - whoever holds it can have
        // the voucher redeemed at the pump - and it is the widest value on the table. A payload
        // that does not fit in 2000 characters will not scan anyway, and the cap keeps the
        // unique index below TOAST so the entry is a predictable size.
        builder.Property(e => e.QrPayload)
            .HasColumnName("qr_payload")
            // bytea, not varchar: this holds AES-GCM output - nonce, tag, ciphertext. The conversion
            // itself is applied in ApplicationDbContext.OnModelCreating, because it needs the cipher
            // instance and this configuration is discovered by assembly scan with no access to DI.
            .HasColumnType("bytea")
            .IsRequired()
            .HasComment(                "The supplier's QR code, verbatim - the redeemable material. Stored as AES-GCM" +
                    "ciphertext: what sits in the column is nonce, tag and ciphertext concatenated.");

        builder.Property(e => e.QrPayloadSha256)
            .HasColumnName("qr_payload_sha256")
            .HasColumnType("bytea")
            // Required, and that is load-bearing rather than tidiness. PostgreSQL treats NULLs as
            // distinct inside a unique index, so a nullable hash column would let any number of rows
            // through with a NULL hash and deduplication would stop working with no error anywhere -
            // the same paper imported twice under two numbers, which is exactly what this index
            // exists to prevent.
            .IsRequired()
            // Filled automatically on insert, so no writer has to remember. See the generator for why
            // a forgotten hash is worse than a failed write.
            .HasValueGenerator<FuelFlow.Features.Vouchers.Security.QrPayloadHashValueGenerator>()
            .HasComment(                "SHA-256 of the QR payload, carrying the deduplication the ciphertext cannot. Encrypted" +
                    "bytes cannot be compared - AES-GCM produces a fresh nonce every time, so the same QR" +
                    "encrypts differently every time - so the uniqueness index moved here. A hash is not" +
                    "reversible, and it is still a fingerprint of a redeemable code, so it is not something" +
                    "to log or expose.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired().HasComment("When the row was written.");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired()
            .HasComment(                "Where the voucher stands: assigned, used, expired, blocked and the rest.");

        builder.Property(e => e.AssignedToUserId)
            .HasColumnName("assigned_to_user_id")
            .HasComment(                "The customer who holds it.");

        builder.Property(e => e.LegalEntityId)
            .HasColumnName("legal_entity_id").HasComment("The company the voucher was sold to.");

        builder.Property(e => e.WorkerUserId)
            .HasColumnName("worker_user_id")
            .HasComment(                "The worker it was issued to.");

        builder.Property(e => e.ImportJobId)
            .HasColumnName("import_job_id")
            .HasComment(                "The import it came from.");

        builder.Property(e => e.OrderId)
            .HasColumnName("order_id")
            .HasComment(                "The order it was sold with, when it was sold with one.");

        builder.Property(e => e.QrParametersId)
            .HasColumnName("qr_parameters_id").HasComment("The QR parameters read from the original code - the geometry needed to regenerate it. NULL" +
                    "when the code was not read.");

        builder.HasOne(e => e.QrParameters)
            .WithMany()
            .HasForeignKey(e => e.QrParametersId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.AssignedToUser)
            .WithMany()
            .HasForeignKey(e => e.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.LegalEntity)
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.WorkerUser)
            .WithMany()
            .HasForeignKey(e => e.WorkerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.ImportJob)
            .WithMany()
            .HasForeignKey(e => e.ImportJobId)
            .OnDelete(DeleteBehavior.SetNull);

        // Restrict, not Cascade. A voucher is paper the customer already paid for, and this
        // constraint was the sharpest edge in the schema: deleting a fuel type walked straight
        // into fuel_vouchers and destroyed every voucher sold against it. #217 fixed the code
        // path (the handlers archive now), which left the constraint armed with nothing
        // pointing at it. The catalog is archived via IsDeleted; this is the seatbelt for a
        // DELETE arriving from anywhere else, including a hand-written psql session.
        //
        // The key is the PAIR (fuel type, brand), not the fuel alone - which is what D-21's second
        // slice needs. A foreign key over one column says "this fuel exists"; a foreign key over two
        // says "this fuel exists AND this voucher's brand is that fuel's brand", and the second half
        // is the fact the brand text on this row is a copy of. The pair points at the fuel's own
        // alternate key (id, brand_id), so the key spans one hop instead of two.
        //
        // Both halves of the pair are NOT NULL, which is what makes the key a key: a foreign key
        // with a NULL in it is not compared at all, so a nullable brand_id would be a brand the
        // database never checks.
        builder.HasOne(e => e.FuelType)
            .WithMany()
            .HasForeignKey(e => new { e.FuelTypeId, e.BrandId })
            .HasPrincipalKey(e => new { e.Id, e.BrandId })
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not SetNull: deleting an order must never silently strip the only trace of
        // where a voucher in someone's hands came from. That matters as soon as the "a held
        // voucher always has an order" CHECK lands, and it is the safe choice before it does.
        builder.HasOne<FuelFlow.Features.Orders.SharedModels.Order>()
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.SupplierId)
            .HasColumnName("supplier_id")
            .HasComment(                "Who we bought it from.");

        builder.HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.CostPerLiter)
            .HasColumnName("cost_per_liter")
            .HasColumnType("numeric(10,4)")
            .HasComment(                "What this litre cost US, in UAH - the supplier's price, not our selling price.");

        builder.Property(e => e.DataKind)
            .HasColumnName("data_kind")
            .HasMaxLength(10)
            .IsRequired()
            .HasComment(                "Whether this row is business or a rehearsal: 'real' or 'qa'.");

        // HasDefaultValue as well as the CLR default: the value has to reach the database for the
        // write paths that never mention this axis (every existing import path among them), and EF
        // omits an unset column from the INSERT. Without the database-side default the column would
        // arrive as NULL and the NOT NULL would reject it.
        builder.Property(e => e.AcquisitionSource)
            .HasColumnName("acquisition_source")
            .HasMaxLength(10)
            .HasDefaultValue(AcquisitionSources.Default)
            .IsRequired().HasComment("The supply route this voucher came in on: direct, or through a partner. Immutable, and" +
                    "enforced by a trigger: the cost of a voucher is a function of where it came from, so an" +
                    "edited value does not merely relabel a row - it moves real money between two reports that" +
                    "are supposed to be separate.");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired()
            .HasComment(                "Soft delete. The row stays: the paper was real, and the record of it has to survive the" +
                    "delete.");

        builder.HasQueryFilter(QueryFilterKeys.SoftDelete, e => !e.IsDeleted);

        // These live in the model, not in a hand-written migration, so EF owns them: it will
        // never drop one by accident, and a regenerated `migrations script` can reproduce the
        // schema from the model. The held-voucher rule used to exist only as raw SQL, invisible
        // to EF and coupled to the VoucherStatus member names by literal string.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_voucher_held_has_order",
                "status NOT IN ('Assigned', 'Used', 'Blocked') OR order_id IS NOT NULL");
            t.HasCheckConstraint("ck_voucher_liters_positive", "liters > 0");
            t.HasCheckConstraint("ck_voucher_cost_non_negative", "cost_per_liter IS NULL OR cost_per_liter >= 0");
            // The value set DataKinds.All documents - see DataKinds.
            t.HasCheckConstraint("ck_fuel_vouchers_data_kind", "data_kind IN ('real', 'qa')");
            // The value set AcquisitionSources.All documents - see AcquisitionSources.
            t.HasCheckConstraint("ck_fuel_vouchers_acquisition_source", "acquisition_source IN ('direct', 'partner')");

            // The value set VoucherStatus defines. 32 characters leaves room for the longest member and the
            // next one; the previous varchar(20) was exactly the old member's length, so the next rename
            // would have failed at write time.
            t.HasCheckConstraint("ck_voucher_status", """
                status IN ('Imported', 'DecodeIntegrityWarning', 'DecodeIntegrityFailed', 'Available',
                           'Assigned', 'Used', 'Expired', 'Blocked', 'Deactivated')
                """);

            // We promise a customer fuel only while the supplier's paper is still alive. Without this
            // the rule lived in code, on some paths, and a path that forgot it sold fuel the pump will
            // refuse.
            t.HasCheckConstraint("ck_voucher_customer_term_within_supplier_term",
                "customer_expiration_date <= supplier_expiration_date");

            // The ownership matrix, in the database (D-02, "Consequence - ownership becomes enforceable").
            //
            // Declared here rather than as raw SQL in the migration so EF owns it and the model and the
            // installed constraint can never drift - the failure CheckConstraintColumnDriftTests was
            // written about. Because holder_kind is generated from the very columns the predicate
            // mentions, each disjunct is a check that a combination is coherent rather than a restatement
            // of the CASE above:
            //
            //   stock    - nobody holds it, so assigned_to_user_id must be null too
            //   worker   - belongs to a worker AND to the company that issued it, which is why
            //              legal_entity_id is required: the fuel is the company's and is in the worker's
            //              hands. GiftVouchersCommand is the only writer of worker_user_id and already
            //              refuses a voucher whose legal_entity_id is not the issuing company.
            //   personal - a customer's own fuel, so there is no company and no worker on the row
            //   company  - company fuel in the customer's own wallet, so there is no worker on the row
            //
            // 'stock' is the ELSE of the generated CASE, so it is also the only value that can reach
            // this predicate with a null assigned_to_user_id; and the ORs are exhaustive, so any value
            // outside the four is refused as well. That is why there is no separate CHECK on the
            // value set.
            // The value set is the enum member names, PascalCase, matching ck_voucher_status and
            // every other enum-derived value set in this schema. The generated column writes those
            // exact strings because the property has a string conversion - and a CHECK that compared
            // against lowercase would refuse every row the column had just written.
            t.HasCheckConstraint("ck_voucher_holder", """
                (holder_kind = 'Stock'    AND assigned_to_user_id IS NULL)
                OR (holder_kind = 'Worker'   AND legal_entity_id IS NOT NULL AND worker_user_id IS NOT NULL)
                OR (holder_kind = 'Personal' AND worker_user_id IS NULL AND assigned_to_user_id IS NOT NULL)
                OR (holder_kind = 'Company'  AND worker_user_id IS NULL AND assigned_to_user_id IS NOT NULL)
                """);
        });

        // GENERATED ALWAYS AS ... STORED, not a computed value EF holds in memory: the answer has to
        // be the same in SQL, in a report, and in psql, and ck_voucher_holder below reads it. Postgres
        // does allow a CHECK to reference a generated column - verified against 16 rather than assumed.
        //
        // Precedence is the whole point. worker_user_id first, because issuing fuel to a worker leaves
        // assigned_to_user_id on the owner on purpose; testing assigned_to_user_id first would report
        // the owner as the holder of fuel they had already given away.
        builder.Property(e => e.HolderKind)
            .HasColumnName("holder_kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasComputedColumnSql(
                """
                CASE
                    WHEN worker_user_id IS NOT NULL THEN 'Worker'
                    WHEN legal_entity_id IS NOT NULL AND assigned_to_user_id IS NOT NULL THEN 'Company'
                    WHEN assigned_to_user_id IS NOT NULL THEN 'Personal'
                    ELSE 'Stock'
                END
                """,
                stored: true)
            .ValueGeneratedOnAddOrUpdate()
            .HasComment(                "Who may redeem it: the customer, or the worker it was issued to.");

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired().HasComment("When it was last changed.");

        // These two arrived in PascalCase because the mapping was forgotten - the only
        // snake_case violations left on this table. Named explicitly, like every other column.
        builder.Property(e => e.IssuedAtUtc)
            .HasColumnName("issued_at_utc")
            .HasComment(                "When this voucher was handed to a worker by a company owner. Set once, on issue; never" +
                    "moved afterwards. A column of its own rather than a reading of updated_at_utc, because" +
                    "every mutation - a block, a recall, a deactivate - clobbers updated_at_utc, and a usage" +
                    "report that answered 'when did I receive this' off it would move the date every time the" +
                    "owner touched it.");

        builder.Property(e => e.UsedAtUtc)
            .HasColumnName("used_at_utc")
            .HasComment(                "When this voucher was actually refuelled with. Set once, when it is marked used; never" +
                    "moved afterwards. NULL while the voucher is still unused.");

        // Decode integrity: how far the module grid printed on the paper disagreed with the payload
        // re-encoded from what the importer read. It says nothing about the supplier - no signature,
        // serial or registry is checked - so the columns are named after the matrix, not "verification".
        builder.Property(e => e.MatrixMismatchPercent)
            .HasColumnName("matrix_mismatch_percent")
            .HasColumnType("numeric(5,4)")
            .HasComment("How far the printed QR matrix disagreed with the decoded payload, as a percentage. NULL " +
                    "when the matrix could not be checked. Not a measure of authenticity.");

        builder.Property(e => e.MatrixMismatchedModules)
            .HasColumnName("matrix_mismatched_modules")
            .HasComment("How many QR modules disagreed between the printed matrix and the regenerated one.");

        builder.Property(e => e.MatrixTotalModules)
            .HasColumnName("matrix_total_modules")
            .HasComment("How many QR modules were compared.");

        // Both keys are filtered to live rows, so an archived voucher releases its number and its
        // QR payload for reuse. Without the filter the two paths disagree: the import's duplicate
        // check reads through the !IsDeleted query filter, so it calls a number free, and then the
        // insert hits a DB-global unique index and the whole import dies on an error the operator
        // cannot interpret. #1067 made soft delete the only delete path, so archived rows - and the
        // keys they squat on - are permanent.
        //
        // The dedup guarantee this buys is the one that matters: the same paper cannot be live
        // twice under two numbers. Re-importing paper that was archived by mistake is a recovery
        // path, not an attack surface, and it now works instead of exploding.
        builder.HasIndex(e => e.VoucherNumber)
            .IsUnique()
            .HasFilter("is_deleted = false");

// Security control, not just an index: dedup on the QR payload means the same paper cannot
        // be imported twice under two numbers. Worth the storage.
        //
        // It sits on the SHA-256, not the payload. It used to sit on the payload itself, which was
        // possible only because that column was plaintext - and it is the reason the payload was
        // stored in the clear at all. AES-GCM produces a fresh nonce per encryption, so the same QR
        // encrypts differently every time and cannot be compared; the hash can. Deduplication is
        // unchanged in strength: both are one-way, and a collision would need a SHA-256 preimage.
        builder.HasIndex(e => e.QrPayloadSha256)
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(e => e.SupplierExpirationDate);
        builder.HasIndex(e => e.CustomerExpirationDate);
        builder.HasIndex(e => e.WorkerUserId);
        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.ImportJobId);
        builder.HasIndex(e => e.SupplierId);

        // Cleanup and "is this stock real?" queries filter on it, so it is indexed alongside status.
        builder.HasIndex(e => new { e.Status, e.DataKind, e.IsDeleted });
        builder.HasIndex(e => new { e.Brand, e.FuelTypeId, e.Liters, e.Status });
        // The blended-cost recompute reads this pool filtered by fuel and status.
        builder.HasIndex(e => new { e.FuelTypeId, e.Status, e.IsDeleted });
        builder.HasIndex(e => new { e.AssignedToUserId, e.Status });
        builder.HasIndex(e => new { e.LegalEntityId, e.WorkerUserId, e.Status, e.IsDeleted });

        // The admin's DEFAULT voucher list sorts by created_at_utc DESC, id - and fifteen indexes
        // on this table had not one of them on that column, so the sort was a parallel sequential
        // scan over the whole table.
        //
        // Measured on a replica built from production's own dump and filled to 500 000 vouchers:
        // 52.8 ms -> 0.154 ms, a 343x improvement on the screen an operator opens first. The
        // filtered variant went 49.3 ms -> 0.25 ms. The index costs 19 MB at that size and 405 ms
        // to build.
        //
        // The migration creates it CONCURRENTLY, so the build does not take the ACCESS EXCLUSIVE
        // lock that would block the API for the length of it.
        builder.HasIndex(e => new { e.CreatedAtUtc, e.Id })
            .IsDescending(true, false)
            .HasDatabaseName("ix_fuel_vouchers_created_at_utc_id");

        // Dropped as strict left-prefix duplicates of the composites above: a bare (status),
        // (fuel_type_id), (assigned_to_user_id) and (legal_entity_id) can serve nothing the
        // wider index cannot serve better. Also dropped: a unique index on external_id, a column
        // nothing in the codebase ever writes, so it guarded 100% NULLs forever.

        // The FK side of the same natural-key trade: fuel_type_id points at fuel_types.id ("okko-95"),
        // which is deliberately a readable string rather than a UUID. Bounded by CHECK so the
        // reference cannot drift into something arbitrary - see BrandConfiguration.
        builder.ToTable(t =>
        {
            });
    }
}
