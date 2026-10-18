using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Pricing.Rules.SharedModels;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Pricing.Rules.Configurations;

internal sealed class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
{
    public void Configure(EntityTypeBuilder<PricingRule> builder)
    {
        builder.ToTable("pricing_rules");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.RuleKind)
            .HasColumnName("rule_kind")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.Scope)
            .HasColumnName("scope")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.OfferId)
            .HasColumnName("offer_id")
            .HasMaxLength(200);

        builder.Property(e => e.LegalEntityId).HasColumnName("legal_entity_id");

        builder.Property(e => e.ContractId).HasColumnName("contract_id");

        // 14,4 like the other rate columns: a per-litre figure with four decimals, and a percentage
        // with room for the same scale. Matches fuel_offers.cost_per_liter so the two are comparable
        // in a query rather than needing a cast.
        builder.Property(e => e.Value)
            .HasColumnName("value")
            .HasColumnType("numeric(14,4)")
            .IsRequired();

        builder.Property(e => e.ValidFrom)
            .HasColumnName("valid_from")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.ValidTo)
            .HasColumnName("valid_to")
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasDefaultValue(PricingRuleStatuses.Draft)
            .IsRequired();

        builder.Property(e => e.RequestedByUserId).HasColumnName("requested_by_user_id");

        builder.Property(e => e.RequestedAtUtc)
            .HasColumnName("requested_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.ApprovedByUserId).HasColumnName("approved_by_user_id");

        builder.Property(e => e.ApprovedAtUtc)
            .HasColumnName("approved_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasMaxLength(500);

        // Restrict on every target. A rule that outlived the thing it priced would keep re-pricing
        // the catalogue for an offer that no longer exists - which is the silent-mistake failure this
        // whole table exists to end.
        builder.HasOne(e => e.Offer)
            .WithMany()
            .HasForeignKey(e => e.OfferId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.LegalEntity)
            .WithMany()
            .HasForeignKey(e => e.LegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Contract)
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        // The requester is the audit anchor. NOT NULL, and Restrict like every other author column
        // in this schema: a rule whose author was deleted is a rule nobody can be shown to have asked
        // for.
        builder.HasOne(e => e.RequestedBy)
            .WithMany()
            .HasForeignKey(e => e.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ApprovedBy)
            .WithMany()
            .HasForeignKey(e => e.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            // The value sets PricingRuleKinds / PricingRuleScopes / PricingRuleStatuses document.
            t.HasCheckConstraint("ck_pricing_rules_kind",
                $"rule_kind IN ('{string.Join("','", PricingRuleKinds.All)}')");

            t.HasCheckConstraint("ck_pricing_rules_scope",
                $"scope IN ('{string.Join("','", PricingRuleScopes.All)}')");

            t.HasCheckConstraint("ck_pricing_rules_status",
                $"status IN ('{string.Join("','", PricingRuleStatuses.All)}')");

            // A negative price is not a discount. Stated here rather than only in C# because a psql
            // session writing a rule does not go through the handler.
            t.HasCheckConstraint("ck_pricing_rules_value_non_negative", "value >= 0");

            // Exactly one target, and the scope names which. Without this a rule could be "global"
            // and still carry an offer id, and nothing would say so — the row would simply never match
            // anything, which is the quietest possible failure for a pricing rule.
            t.HasCheckConstraint("ck_pricing_rules_exactly_one_scope_target", """
                (scope = 'global'        AND offer_id IS NULL AND legal_entity_id IS NULL AND contract_id IS NULL)
             OR (scope = 'offer'         AND offer_id IS NOT NULL AND legal_entity_id IS NULL AND contract_id IS NULL)
             OR (scope = 'legal_entity'  AND offer_id IS NULL AND legal_entity_id IS NOT NULL AND contract_id IS NULL)
             OR (scope = 'contract'      AND offer_id IS NULL AND legal_entity_id IS NULL AND contract_id IS NOT NULL)
            """);

            // An end before a start is not a closed period, it is a typo.
            t.HasCheckConstraint("ck_pricing_rules_valid_window",
                "valid_to IS NULL OR valid_to > valid_from");

            // The whole of four-eyes, expressed once: approved means somebody approved it.
            // Without this a rule could be marked approved with no approver and the approval column
            // would be decoration.
            t.HasCheckConstraint("ck_pricing_rules_approved_has_approver",
                "(status = 'approved') = (approved_by_user_id IS NOT NULL)");
        });

        // The query every price lookup will run: for this scope and kind, the most recent approved
        // rule that is in force. Partial on approved because draft and pending rows are not prices,
        // and indexing them would make the index bigger for no reader.
        builder.HasIndex(e => new { e.Scope, e.OfferId, e.LegalEntityId, e.RuleKind, e.ValidFrom })
            .IsDescending(false, false, false, false, true)
            .HasDatabaseName("ix_pricing_rules_active")
            .HasFilter($"status = '{PricingRuleStatuses.Approved}'");

        // Who asked, and when. Not a lookup path — this is the "what did this person change" answer,
        // and it is on a table an operator must be able to audit row by row.
        builder.HasIndex(e => new { e.RequestedByUserId, e.RequestedAtUtc })
            .HasDatabaseName("ix_pricing_rules_requester");
    }
}