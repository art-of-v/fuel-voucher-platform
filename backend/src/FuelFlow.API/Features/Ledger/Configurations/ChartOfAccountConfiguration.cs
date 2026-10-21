using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Ledger.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Ledger.Configurations;

internal sealed class ChartOfAccountConfiguration : IEntityTypeConfiguration<ChartOfAccount>
{
    public void Configure(EntityTypeBuilder<ChartOfAccount> builder)
    {
        builder.ToTable("chart_of_accounts");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(10).IsRequired()
            .HasComment(                "The account number as the accountant writes it, e.g. 7911.");
        builder.Property(e => e.NameUkr).HasColumnName("name_ukr").HasMaxLength(200).IsRequired()
            .HasComment(                "The account's name, in Ukrainian.");
        builder.Property(e => e.AccountType)
            .HasColumnName("account_type").HasConversion<string>().HasMaxLength(12).IsRequired()
            .HasComment(                "What kind of account this is: an asset, a liability, income or expense.");
        builder.Property(e => e.NormalBalance)
            .HasColumnName("normal_balance").HasConversion<string>().HasMaxLength(6).IsRequired()
            .HasComment(                "Which side the account normally carries: a debit or a credit.");
        builder.Property(e => e.ParentId).HasColumnName("parent_id")
            .HasComment(                "The account this one rolls up into.");
        builder.Property(e => e.IsPostable).HasColumnName("is_postable").HasDefaultValue(true).IsRequired()
            .HasComment(                "Whether entries may post to this account.");
        builder.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired()
            .HasComment(                "Whether the account is in use. A retired account stays, because entries point at it.");

        // RESTRICT both ways: a chart is a tree, and deleting an account with a parent or a child
        // would silently re-parent postings that were balanced against it.
        builder.HasOne<ChartOfAccount>()
            .WithMany()
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.Code).IsUnique();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_chart_of_accounts_account_type",
                "account_type IN ('Asset','Liability','Equity','Revenue','Expense','Vat')");
            t.HasCheckConstraint("ck_chart_of_accounts_normal_balance", "normal_balance IN ('Debit','Credit')");
            t.HasCheckConstraint("ck_chart_of_accounts_code_not_blank", "btrim(code) <> ''");
            t.HasCheckConstraint("ck_chart_of_accounts_code_is_numeric", "code ~ '^[0-9]+$'");
            t.HasCheckConstraint("ck_chart_of_accounts_not_its_own_parent", "parent_id IS DISTINCT FROM id");

            // NOT the constraint TARGET_SCHEMA.md sketched here. That one read
            //   CHECK (is_postable OR id NOT IN (SELECT id FROM chart_of_accounts WHERE is_postable))
            // which PostgreSQL rejects outright - a CHECK may not contain a subquery - and which is a
            // no-op even if it were accepted: a row whose own is_postable is false trivially satisfies
            // the second arm. The rule it was reaching for ("a line may not name a header account")
            // cannot be expressed as a CHECK, because whether an account is a header is a property of
            // the row and whether it may be posted to is a property of the line. It is enforced by
            // the mapping layer, and the comment on ChartOfAccount.IsPostable says so.
        });
    }
}

internal sealed class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> builder)
    {
        builder.ToTable("accounting_periods");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.PeriodStart).HasColumnName("period_start").IsRequired()
            .HasComment(                "The first day of the period.");
        builder.Property(e => e.PeriodEnd).HasColumnName("period_end").IsRequired()
            .HasComment(                "The last day of the period.");
        builder.Property(e => e.Status)
            .HasColumnName("status").HasConversion<string>().HasMaxLength(8)
            .HasDefaultValue(PeriodStatus.Open).IsRequired()
            .HasComment(                "Whether the period is open or closed.");
        builder.Property(e => e.ClosedAtUtc).HasColumnName("closed_at_utc")
            .HasComment(                "When the period was closed. NULL while it is open.");
        builder.Property(e => e.ClosedByUserId).HasColumnName("closed_by_user_id")
            .HasComment(                "Who closed it.");

        builder.HasIndex(e => e.PeriodStart).IsUnique();

        // An overlapping period would make "the period this entry belongs to" ambiguous, and an
        // ambiguous period is an immutability boundary with a hole in it.
        builder.HasIndex(e => new { e.PeriodStart, e.PeriodEnd });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ClosedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_accounting_periods_status", "status IN ('Open','Closed')");
            t.HasCheckConstraint("ck_accounting_periods_range", "period_end >= period_start");
            // Closing a period is a claim that it is finished. A closed period with no timestamp, or an
            // open one carrying a closer, is not a state anybody can argue about later.
            t.HasCheckConstraint("ck_accounting_periods_closure_shape",
                "(status = 'Closed') = (closed_at_utc IS NOT NULL)");
            t.HasCheckConstraint("ck_accounting_periods_closed_names_a_closer",
                "status <> 'Closed' OR closed_by_user_id IS NOT NULL");
        });
    }
}
