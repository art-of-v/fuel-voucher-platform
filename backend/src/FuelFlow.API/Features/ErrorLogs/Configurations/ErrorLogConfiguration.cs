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
        builder.Property(e => e.LoggedAtUtc).HasColumnName("logged_at_utc").HasColumnType("timestamp with time zone").IsRequired()
            .HasComment(                "When the error was logged.");

        // Every column here was unbounded `text` on a table that grows by a row per logged error
        // and is pruned at 90 days. The widths below are the ones the sibling tables already use
        // (support_messages.message is varchar(4000), refunds.error_message is varchar(500)), so
        // this is consistency rather than invention. stack_trace stays text: it is genuinely
        // unbounded and it is the column an incident review actually needs.
        builder.Property(e => e.Level).HasColumnName("level").HasMaxLength(30).IsRequired()
            .HasComment(                "How bad it was - information, warning, error, critical.");
        builder.Property(e => e.Message).HasColumnName("message").HasMaxLength(2000).IsRequired()
            .HasComment(                "The log message, in one line.");
        builder.Property(e => e.ExceptionType).HasColumnName("exception_type").HasMaxLength(500)
            .HasComment(                "The exception's type, when there was one.");
        builder.Property(e => e.ExceptionMessage).HasColumnName("exception_message").HasMaxLength(4000)
            .HasComment(                "The exception's message, when there was one.");
        builder.Property(e => e.StackTrace).HasColumnName("stack_trace").HasColumnType("text")
            .HasComment(                "The stack, when there was one. The longest column in the table, and the one that fills" +
                    "it.");
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(200)
            .HasComment(                "Where the error came from - the logger's source, or the component that raised it.");
        builder.Property(e => e.RequestPath).HasColumnName("request_path").HasMaxLength(500)
            .HasComment(                "The path of the request that failed, when it was a request.");
        builder.Property(e => e.RequestMethod).HasColumnName("request_method").HasMaxLength(10)
            .HasComment(                "The method of that request.");
        builder.Property(e => e.UserName).HasColumnName("user_name").HasMaxLength(200)
            .HasComment(                "The user the request was made by, when we know who it was.");
        builder.Property(e => e.TraceId).HasColumnName("trace_id").HasMaxLength(100)
            .HasComment(                "The W3C trace id of the activity that was current when the record was logged. One failed" +
                    "request emits SEVERAL records - request logging, the global handler, the feature" +
                    "handler, EF Core - and this is what lets the admin log group them back into a single" +
                    "incident. NULL for records logged outside any activity: startup, shutdown, background" +
                    "jobs.");
        builder.Property(e => e.ResolvedAtUtc).HasColumnName("resolved_at_utc").HasColumnType("timestamp with time zone")
            .HasComment(                "When staff ACKNOWLEDGED this error as handled, or NULL while it is still outstanding. A" +
                    "fault is only 'resolved' once a human says so - the log cannot tell whether a deployment" +
                    "fixed the cause.");
        builder.Property(e => e.ResolvedByUserId).HasColumnName("resolved_by_user_id").HasColumnType("uuid")
            .HasComment(                "Who acknowledged it.");

        // resolved_by_user_id stays a bare uuid: a diagnostic log must never fail to record
        // because a referenced row is missing. An error is worth writing whatever the id says.
        builder.Property(e => e.ResolvedByUserName).HasColumnName("resolved_by_user_name").HasMaxLength(200)
            .HasComment(                "Their name at the time.");
        builder.HasIndex(e => e.LoggedAtUtc);
        // Level is a handful of values; the index only earns its write cost on the admin filter.
        builder.HasIndex(e => e.Level);
        builder.HasIndex(e => e.Source);
        // Serves the "show only this incident" filter, which is an equality match.
        builder.HasIndex(e => e.TraceId);
        // The journal's default view is the outstanding errors, ordered newest first.
        builder.HasIndex(e => new { e.ResolvedAtUtc, e.LoggedAtUtc });
        // resolved_by_user_id is written and never read: the journal does not filter incidents by
        // who resolved them. An index there would be the exact defect F-11 removed.
    }
}
