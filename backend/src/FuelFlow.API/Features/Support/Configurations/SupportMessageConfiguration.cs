using FuelFlow.Features.Support.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Support.Configurations;

internal sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> builder)
    {
        builder.ToTable("support_messages");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(254)
            .IsRequired()
            .HasComment(                "The reply address of the person who wrote in (validated at the edge).");

        builder.Property(e => e.Message)
            .HasColumnName("message")
            .HasMaxLength(4000)
            .IsRequired()
            .HasComment(                "What they wrote.");

        builder.Property(e => e.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(300)
            .HasComment(                "The free-form browser User-Agent, truncated. Diagnostic only.");

        builder.Property(e => e.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45)
            .HasComment(                "The client IP as seen by the API (after forwarded-header processing). Used to spot" +
                    "abuse; never shown anywhere.");

        builder.Property(e => e.EmailSentAtUtc)
            .HasColumnName("email_sent_at_utc")
            .HasComment(                "NULL until the message was successfully handed to the SMTP server. NULL is normal when" +
                    "SupportMail is not configured (e.g. dev).");

        builder.Property(e => e.SendError)
            .HasColumnName("send_error")
            .HasMaxLength(1000)
            .HasComment(                "The last SMTP failure reason, truncated. NULL when delivery succeeded or was never" +
                    "attempted.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired()
            .HasComment(                "When they wrote in.");

        builder.HasIndex(e => e.CreatedAtUtc);
    }
}
