using FuelFlow.Features.BusinessLeads.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.BusinessLeads.Configurations;

internal sealed class BusinessLeadConfiguration : IEntityTypeConfiguration<BusinessLead>
{
    public void Configure(EntityTypeBuilder<BusinessLead> builder)
    {
        builder.ToTable("business_leads", table =>
        {
            table.HasCheckConstraint(
                "ck_business_lead_status",
                "status IN ('New', 'Contacted', 'Qualified', 'Converted', 'Rejected')");
            table.HasCheckConstraint(
                "ck_business_lead_fleet_size",
                "fleet_size IS NULL OR fleet_size > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyName).HasColumnName("company_name").HasMaxLength(200).IsRequired()
            .HasComment("The company or fleet asking Fuel Flow to contact it.");
        builder.Property(e => e.ContactName).HasColumnName("contact_name").HasMaxLength(200).IsRequired()
            .HasComment("The person Fuel Flow should ask for when following up.");
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired()
            .HasComment("The contact phone in explicit E.164 form, validated at the public edge.");
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(254)
            .HasComment("Optional reply address supplied by the contact and validated at the public edge.");
        builder.Property(e => e.FleetSize).HasColumnName("fleet_size")
            .HasComment("Optional number of vehicles the contact says the company operates.");
        builder.Property(e => e.Message).HasColumnName("message").HasMaxLength(2000)
            .HasComment("Optional business need the contact wrote in their own words.");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasComment("Where the sales enquiry stands: New, Contacted, Qualified, Converted or Rejected.");
        builder.Property(e => e.UserAgent).HasColumnName("user_agent").HasMaxLength(300)
            .HasComment("The submitting browser User-Agent, truncated and retained only for abuse diagnostics.");
        builder.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45)
            .HasComment("The submitting client IP after trusted proxy processing; retained only for abuse diagnostics.");
        builder.Property(e => e.EmailSentAtUtc).HasColumnName("email_sent_at_utc")
            .HasComment("NULL until the sales notification was handed to SMTP successfully.");
        builder.Property(e => e.EmailSendError).HasColumnName("email_send_error").HasMaxLength(1000)
            .HasComment("The latest sales-notification failure, truncated; NULL after success or when delivery was not attempted.");
        builder.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired()
            .HasComment("When the public business enquiry was accepted.");
        builder.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired()
            .HasComment("When the lead last moved in the sales pipeline; terminal retention is measured from this time.");

        builder.HasIndex(e => new { e.Status, e.CreatedAtUtc, e.Id });
        builder.HasIndex(e => new { e.Status, e.UpdatedAtUtc });
    }
}
