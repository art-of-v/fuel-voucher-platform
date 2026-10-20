using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Ledger.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Ledger.Configurations;

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("journal_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.EntryNo).HasColumnName("entry_no").IsRequired();
        builder.Property(e => e.EntryDate).HasColumnName("entry_date").IsRequired();
        builder.Property(e => e.PeriodId).HasColumnName("period_id").IsRequired();
        builder.Property(e => e.SourceType)
            .HasColumnName("source_type").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(e => e.SourceEvent)
            .HasColumnName("source_event").HasMaxLength(40).IsRequired();
        builder.Property(e => e.SourceId).HasColumnName("source_id");
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(e => e.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(10)
            .HasDefaultValue(JournalEntryStatus.Draft).IsRequired();
        builder.Property(e => e.PostedAtUtc).HasColumnName("posted_at_utc");
        builder.Property(e => e.PostedByUserId).HasColumnName("posted_by_user_id");
        builder.Property(e => e.ReversedByEntryId).HasColumnName("reversed_by_entry_id");
        builder.Property(e => e.TamperHash).HasColumnName("tamper_hash").HasMaxLength(64);
        builder.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");

        // The entry number is what a bookkeeper cites, so it is unique inside the period and nowhere
        // else - two periods may each hold entry 1.
        builder.HasIndex(e => new { e.PeriodId, e.EntryNo }).IsUnique();

        builder.HasIndex(e => new { e.SourceType, e.SourceId });

        // Idempotency, enforced by the database rather than by a read-then-write check: two
        // concurrent replays of the same webhook must not both decide they are the first. The
        // event is part of the key because one source row legitimately produces several entries -
        // revenue, VAT and cost of sales are three postings of the same payment.
        builder.HasIndex(e => new { e.SourceEvent, e.SourceType, e.SourceId }).IsUnique();

        builder.HasOne(e => e.Period)
            .WithMany(e => e.Entries)
            .HasForeignKey(e => e.PeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<JournalEntry>()
            .WithMany()
            .HasForeignKey(e => e.ReversedByEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.PostedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_journal_entries_source_type", """
                source_type IN ('Payment','Refund','Fulfillment','Cost','Movement','Adjustment','Fiscalization','Reversal')
                """);
            t.HasCheckConstraint("ck_journal_entries_status", "status IN ('Draft','Posted','Reversed')");
            t.HasCheckConstraint("ck_journal_entries_description_not_blank", "btrim(description) <> ''");
            t.HasCheckConstraint("ck_journal_entries_source_event_not_blank", "btrim(source_event) <> ''");

            // Posting is a claim that someone stood behind it. A posted entry without both a moment
            // and a person is an entry nobody can be asked about.
            t.HasCheckConstraint("ck_journal_entries_posted_shape", """
                (status = 'Draft') OR (posted_at_utc IS NOT NULL AND posted_by_user_id IS NOT NULL)
                """);
            // A reversal points at the entry that undoes it; the undone entry is kept, never deleted.
            t.HasCheckConstraint("ck_journal_entries_reversed_shape",
                "status <> 'Reversed' OR reversed_by_entry_id IS NOT NULL");
            t.HasCheckConstraint("ck_journal_entries_not_own_reversal", "reversed_by_entry_id IS DISTINCT FROM id");
            t.HasCheckConstraint("ck_journal_entries_entry_no_positive", "entry_no > 0");
            t.HasCheckConstraint("ck_journal_entries_tamper_hash_length",
                "tamper_hash IS NULL OR tamper_hash ~ '^[0-9a-f]{64}$'");

            // "The entry's date falls inside its period" is NOT here, though it belongs here in spirit.
            // Expressing it means period_id IN (SELECT id FROM accounting_periods WHERE ...), and
            // PostgreSQL rejects a CHECK containing a subquery outright. It lives in
            // trg_journal_entry_posted_is_coherent instead, which checks it at the moment of posting -
            // the only moment it is true for, since a draft may legitimately be dated ahead of its
            // period while it is being composed.
        });

        // --- F-23: a reference column without an index ----------------------------------------------
        // Polymorphic: source_type says what kind of row this is, so the pair is what a lookup uses.
        // No foreign key here on purpose - an entry must not be the thing that blocks deleting its
        // subject - but the reference is still read, and without an index every read of it is a scan.
        builder.HasIndex(e => e.SourceId);
    }
}

internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        builder.ToTable("journal_lines");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.EntryId).HasColumnName("entry_id").IsRequired();
        builder.Property(e => e.LineNo).HasColumnName("line_no").IsRequired();
        builder.Property(e => e.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(e => e.PartyId).HasColumnName("party_id");
        // offer_id is gone with the offer (2026-10-19) - see the note where its foreign key was.
        builder.Property(e => e.LegalEntityId).HasColumnName("legal_entity_id");
        builder.Property(e => e.VoucherId).HasColumnName("voucher_id");
        builder.Property(e => e.QuantityLiters).HasColumnName("quantity_liters").HasPrecision(10, 2);
        builder.Property(e => e.DebitUah).HasColumnName("debit_uah").HasPrecision(14, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(e => e.CreditUah).HasColumnName("credit_uah").HasPrecision(14, 2).HasDefaultValue(0m).IsRequired();

        builder.HasIndex(e => new { e.EntryId, e.LineNo }).IsUnique();
        builder.HasIndex(e => e.AccountId);
        builder.HasIndex(e => e.PartyId);
        builder.HasIndex(e => e.VoucherId);

        builder.HasOne(e => e.Entry)
            .WithMany(e => e.Lines)
            .HasForeignKey(e => e.EntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Party>().WithMany().HasForeignKey(e => e.PartyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FuelVoucher>().WithMany().HasForeignKey(e => e.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // offer_id went with the offer on 2026-10-19. A journal line records what happened to a
        // voucher, and which grade it was is already on the line; the offer it was sold under
        // added a pointer that could only ever point at a row nothing wrote.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_journal_lines_quantity_positive",
                "quantity_liters IS NULL OR quantity_liters > 0");
            t.HasCheckConstraint("ck_journal_lines_amounts_non_negative",
                "debit_uah >= 0 AND credit_uah >= 0");
            t.HasCheckConstraint("ck_journal_lines_line_no_positive", "line_no > 0");

            // Exactly one side. A line that is both, or neither, is not a posting - it is a typo that
            // happens to balance against another typo.
            t.HasCheckConstraint("ck_journal_lines_one_side", """
                (debit_uah > 0 AND credit_uah = 0) OR (credit_uah > 0 AND debit_uah = 0)
                """);
        });
    }
}

internal sealed class LedgerMappingRuleConfiguration : IEntityTypeConfiguration<LedgerMappingRule>
{
    public void Configure(EntityTypeBuilder<LedgerMappingRule> builder)
    {
        builder.ToTable("ledger_mapping_rules");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.SourceType)
            .HasColumnName("source_type").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(e => e.SourceEvent).HasColumnName("source_event").HasMaxLength(40).IsRequired();
        builder.Property(e => e.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(e => e.Side)
            .HasColumnName("side").HasConversion<string>().HasMaxLength(6).IsRequired();

        // One account per side per event, which is what makes the mapping total rather than
        // a preference: an event that posts to two accounts on the same side is a bug in the data.
        builder.HasIndex(e => new { e.SourceEvent, e.Side }).IsUnique();

        builder.HasOne(e => e.Account).WithMany().HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_ledger_mapping_rules_source_type", """
                source_type IN ('Payment','Refund','Fulfillment','Cost','Movement','Adjustment','Fiscalization','Reversal')
                """);
            t.HasCheckConstraint("ck_ledger_mapping_rules_side", "side IN ('Debit','Credit')");
            t.HasCheckConstraint("ck_ledger_mapping_rules_event_not_blank", "btrim(source_event) <> ''");
        });

    }
}
