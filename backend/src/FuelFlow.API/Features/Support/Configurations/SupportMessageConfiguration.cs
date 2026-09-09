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
            .IsRequired();

        builder.Property(e => e.Message)
            .HasColumnName("message")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(e => e.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(300);

        builder.Property(e => e.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(e => e.EmailSentAtUtc)
            .HasColumnName("email_sent_at_utc");

        builder.Property(e => e.SendError)
            .HasColumnName("send_error")
            .HasMaxLength(1000);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(e => e.CreatedAtUtc);
    }
}
