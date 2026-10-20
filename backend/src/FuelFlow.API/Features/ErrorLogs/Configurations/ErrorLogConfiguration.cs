using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.ErrorLogs.Configurations;

internal sealed class ErrorLogConfiguration : IEntityTypeConfiguration<ErrorLog>
{
    public void Configure(EntityTypeBuilder<ErrorLog> builder)
    {
        builder.ToTable("error_logs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.LoggedAtUtc).HasColumnName("logged_at_utc").HasColumnType("timestamp with time zone").IsRequired();

        // Every column here was unbounded `text` on a table that grows by a row per logged error
        // and is pruned at 90 days. The widths below are the ones the sibling tables already use
        // (support_messages.message is varchar(4000), refunds.error_message is varchar(500)), so
        // this is consistency rather than invention. stack_trace stays text: it is genuinely
        // unbounded and it is the column an incident review actually needs.
        builder.Property(e => e.Level).HasColumnName("level").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Message).HasColumnName("message").HasMaxLength(2000).IsRequired();
        builder.Property(e => e.ExceptionType).HasColumnName("exception_type").HasMaxLength(500);
        builder.Property(e => e.ExceptionMessage).HasColumnName("exception_message").HasMaxLength(4000);
        builder.Property(e => e.StackTrace).HasColumnName("stack_trace").HasColumnType("text");
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(200);
        builder.Property(e => e.RequestPath).HasColumnName("request_path").HasMaxLength(500);
        builder.Property(e => e.RequestMethod).HasColumnName("request_method").HasMaxLength(10);
        builder.Property(e => e.UserName).HasColumnName("user_name").HasMaxLength(200);
        builder.Property(e => e.TraceId).HasColumnName("trace_id").HasMaxLength(100);
        builder.Property(e => e.ResolvedAtUtc).HasColumnName("resolved_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(e => e.ResolvedByUserId).HasColumnName("resolved_by_user_id").HasColumnType("uuid");

        // resolved_by_user_id stays a bare uuid: a diagnostic log must never fail to record
        // because a referenced row is missing. An error is worth writing whatever the id says.
        builder.Property(e => e.ResolvedByUserName).HasColumnName("resolved_by_user_name").HasMaxLength(200);
        builder.HasIndex(e => e.LoggedAtUtc);
        // Level is a handful of values; the index only earns its write cost on the admin filter.
        builder.HasIndex(e => e.Level);
        builder.HasIndex(e => e.Source);
        // Serves the "show only this incident" filter, which is an equality match.
        builder.HasIndex(e => e.TraceId);
        // The journal's default view is the outstanding errors, ordered newest first.
        builder.HasIndex(e => new { e.ResolvedAtUtc, e.LoggedAtUtc });
        // ResolvedByUserId is indexed by the FK convention above.

        // --- F-14: the foreign key this column was missing -------------------------------------------------
        // The reference is real and single-parent, so the database enforces it. ON DELETE is stated
        // explicitly rather than left to the default: a schema that says NO ACTION by accident is a
        // schema nobody has thought about.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.ResolvedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Postgres does not index a foreign key column on its own, and the parent's delete check and
        // every join down this column scan without one.
        builder.HasIndex(e => e.ResolvedByUserId);
    }
}
