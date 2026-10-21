using FuelFlow.Features.Ledger.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Ledger.Configurations;

internal sealed class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("document_sequences");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.Name)
            .HasColumnName("name").HasMaxLength(80).IsRequired().HasComment("The identity of the sequence, e.g. journal:<period id>. Unique - two rows sharing it are two" +
                    "counters racing for the same numbers, which is what this table exists to prevent.");

        builder.Property(s => s.LastValue)
            .HasColumnName("last_value").IsRequired().HasComment("The last number handed out. Starts at 0, so the first call returns 1.");

        // The name is the sequence. Two rows sharing it are two counters racing for the same
        // numbers, which is precisely what this table exists to prevent.
        builder.HasIndex(s => s.Name).IsUnique();

        builder.ToTable(t =>
        {
            // A counter that has gone negative would hand out entry number 0, which the journal's own
            // ck_journal_entries_entry_no_positive would reject - but at the far end of the call
            // stack, as a database error, instead of here where it is obviously a bug.
            t.HasCheckConstraint("ck_document_sequences_non_negative", "last_value >= 0");
        });
    }
}