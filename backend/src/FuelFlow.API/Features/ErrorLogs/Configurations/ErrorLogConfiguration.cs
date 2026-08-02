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

        builder.HasIndex(e => e.LoggedAtUtc);
        builder.HasIndex(e => e.Level);
        builder.HasIndex(e => e.Source);
    }
}
