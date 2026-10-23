using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Auth.Configurations;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired()
            .HasComment(                "The account this device belongs to.");

        builder.Property(e => e.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(255)
            .IsRequired()
            .HasComment(                "The device's own id, as the app reports it.");

        builder.Property(e => e.PublicKey)
            .HasColumnName("public_key")
            .HasColumnType("text")
            .IsRequired()
            .HasComment(                "The public key the device signs with. The private half never leaves the device.");

        builder.Property(e => e.SignatureAlgorithm)
            .HasColumnName("signature_algorithm")
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasComment(                "The only signature scheme this device may use. NULL = legacy row, pinned to the scheme that first verifies.");

        builder.Property(e => e.SignaturePayloadVersion)
            .HasColumnName("signature_payload_version")
            .HasDefaultValue(1)
            .IsRequired()
            .HasComment(                "The shape of the signed bytes: 1 = raw legacy payload, 2 = domain-separated FuelFlow-DeviceSignature-v2.");

        builder.Property(e => e.DeviceModel)
            .HasColumnName("device_model")
            .HasMaxLength(255)
            .HasComment(                "The device model, as the app reports it.");

        builder.Property(e => e.OsVersion)
            .HasColumnName("os_version")
            .HasMaxLength(255)
            .HasComment(                "The operating system version, as the app reports it.");

        builder.Property(e => e.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(255)
            .HasComment(                "The app version, as the app reports it.");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired()
            .HasComment(                "Whether the device is trusted, blocked or pending.");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasComment(                "When the device was first seen.");

        builder.Property(e => e.LastSeenAt)
            .HasColumnName("last_seen_at")
            .IsRequired()
            .HasComment(                "When it was last seen.");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.DeviceId)
            .IsUnique();

        builder.HasIndex(e => e.UserId);

        // Retention prunes `(Status == Revoked || LastSeenAt < cutoff)`; this covers the second
        // half of that. It only helps because DataRetentionService splits the OR into two
        // statements - with the OR intact the planner keeps choosing a Seq Scan even with this
        // index present, and the combined delete measured 28.5 ms against 3.7 ms split.
        builder.HasIndex(e => e.LastSeenAt);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_device_status", "status IN ('Active', 'Revoked')");
            t.HasCheckConstraint(
                "ck_devices_signature_algorithm",
                "signature_algorithm IS NULL OR signature_algorithm IN "
                + "('RsaPkcs1Sha256', 'RsaPssSha256', 'EcdsaSha256Der', 'EcdsaSha256P1363')");
            t.HasCheckConstraint(
                "ck_devices_signature_payload_version",
                "signature_payload_version IN (1, 2)");
        });
    }
}
