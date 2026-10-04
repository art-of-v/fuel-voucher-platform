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
        builder.Property(e => e.Level).HasColumnName("level").HasColumnType("text").IsRequired();
        builder.Property(e => e.Message).HasColumnName("message").HasColumnType("text").IsRequired();
        builder.Property(e => e.ExceptionType).HasColumnName("exception_type").HasColumnType("text");
        builder.Property(e => e.ExceptionMessage).HasColumnName("exception_message").HasColumnType("text");
        builder.Property(e => e.StackTrace).HasColumnName("stack_trace").HasColumnType("text");
        builder.Property(e => e.Source).HasColumnName("source").HasColumnType("text");
        builder.Property(e => e.RequestPath).HasColumnName("request_path").HasColumnType("text");
        builder.Property(e => e.RequestMethod).HasColumnName("request_method").HasColumnType("text");
        builder.Property(e => e.UserName).HasColumnName("user_name").HasColumnType("text");
        builder.Property(e => e.TraceId).HasColumnName("trace_id").HasColumnType("text");
        builder.Property(e => e.ResolvedAtUtc).HasColumnName("resolved_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(e => e.ResolvedByUserId).HasColumnName("resolved_by_user_id").HasColumnType("uuid");
        builder.Property(e => e.ResolvedByUserName).HasColumnName("resolved_by_user_name").HasColumnType("text");

        builder.HasIndex(e => e.LoggedAtUtc);
        builder.HasIndex(e => e.Level);
        builder.HasIndex(e => e.Source);
        // Serves the "show only this incident" filter, which is an equality match.
        builder.HasIndex(e => e.TraceId);
        // The journal's default view is the outstanding errors, ordered newest first.
        builder.HasIndex(e => new { e.ResolvedAtUtc, e.LoggedAtUtc });
    }
}
