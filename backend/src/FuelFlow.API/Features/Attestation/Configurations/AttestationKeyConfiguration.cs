using FuelFlow.Features.Attestation.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Attestation.Configurations;

internal sealed class AttestationKeyConfiguration : IEntityTypeConfiguration<AttestationKey>
{
    public void Configure(EntityTypeBuilder<AttestationKey> builder)
    {
        builder.ToTable("attestation_keys");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.KeyId)
            .HasColumnName("key_id")
            .HasMaxLength(255)
            .IsRequired()
            .HasComment(
                "The Apple App Attest key id, as the client reports it. Unique: an attestation "
                + "registers exactly one server-side key.");

        builder.Property(e => e.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(255)
            .IsRequired()
            .HasComment(
                "The device that registered the key. Attestation is device-bound, not account-bound; "
                + "a logout revokes the session but does not delete this key.");

        builder.Property(e => e.PublicKey)
            .HasColumnName("public_key")
            .HasColumnType("text")
            .IsRequired()
            .HasComment(
                "The public key from the verified attestation, base64. Assertion signatures are "
                + "checked against this server-owned value, not against anything the client sends.");

        builder.Property(e => e.Counter)
            .HasColumnName("counter")
            .IsRequired()
            .HasComment(
                "The last accepted assertion counter. App Attest counters only move forward, so an "
                + "assertion at or below this value is a replay.");

        builder.Property(e => e.Environment)
            .HasColumnName("environment")
            .HasMaxLength(32)
            .IsRequired()
            .HasComment(
                "The App Attest environment the key was attested in: Production or Development.");

        builder.Property(e => e.Receipt)
            .HasColumnName("receipt")
            .HasColumnType("text")
            .HasComment("The attestation receipt, when the verifier supplies one. Audit data only.");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasComment("When the key was registered.");

        builder.Property(e => e.LastUsedAt)
            .HasColumnName("last_used_at")
            .IsRequired()
            .HasComment("When an assertion from this key was last accepted.");

        builder.Property(e => e.RevokedAt)
            .HasColumnName("revoked_at")
            .HasComment("When an operator revoked the key. A revoked key is never accepted again.");

        builder.HasIndex(e => e.KeyId)
            .IsUnique();

        builder.HasIndex(e => e.DeviceId);
    }
}
