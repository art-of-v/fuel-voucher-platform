using FuelFlow.Features.Ledger.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Ledger.Configurations;

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("bank_accounts");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.OperatorLegalEntityId)
            .HasColumnName("operator_legal_entity_id")
            .IsRequired();

        builder.Property(e => e.BankCode)
            .HasColumnName("bank_code")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.AccountRef)
            .HasColumnName("account_ref")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(e => e.IsDefault)
            .HasColumnName("is_default")
            .IsRequired();

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasOne(e => e.OperatorLegalEntity)
            .WithMany(e => e.BankAccounts)
            .HasForeignKey(e => e.OperatorLegalEntityId)
            // RESTRICT, not CASCADE: a bank account is a fiscal document's issuer detail. Removing
            // the entity that issued historical receipts must not take those accounts with it.
            .OnDelete(DeleteBehavior.Restrict);

        // Foreign-key lookup. The default-account index below is filtered, so it does not cover a
        // plain "all accounts of this entity" read; both are needed.
        builder.HasIndex(e => e.OperatorLegalEntityId);

        builder.HasIndex(e => new { e.OperatorLegalEntityId, e.BankCode, e.AccountRef })
            .IsUnique();

        // ux_bank_accounts_single_default_per_entity is added by the migration as raw SQL, not here:
        // EF identifies an index by its property list, so a second HasIndex on OperatorLegalEntityId
        // would reconfigure this one instead of creating a second index, and the filtered index
        // would silently become the only one.

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_bank_accounts_currency", "currency = 'UAH'");
            t.HasCheckConstraint("ck_bank_accounts_bank_code_not_blank", "btrim(bank_code) <> ''");
            t.HasCheckConstraint("ck_bank_accounts_account_ref_not_blank", "btrim(account_ref) <> ''");
            t.HasCheckConstraint("ck_bank_accounts_bank_code_length", "length(bank_code) <= 20");
            t.HasCheckConstraint("ck_bank_accounts_account_ref_length", "length(account_ref) <= 100");
        });
    }
}
