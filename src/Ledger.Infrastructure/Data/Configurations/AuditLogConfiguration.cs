using Ledger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        // Primary key
        builder.HasKey(e => e.Id);

        // EntityName: required, max length 100
        builder.Property(e => e.EntityName)
            .IsRequired()
            .HasMaxLength(100);

        // EntityId: required, Guid
        builder.Property(e => e.EntityId)
            .IsRequired();

        // Action: required, max length 50
        builder.Property(e => e.Action)
            .IsRequired()
            .HasMaxLength(50);

        // OldValues: nullable, JSONB (PostgreSQL native JSON support)
        builder.Property(e => e.OldValues)
            .HasColumnType("jsonb");

        // NewValues: nullable, JSONB
        builder.Property(e => e.NewValues)
            .HasColumnType("jsonb");

        // PerformedBy: nullable, Guid
        builder.Property(e => e.PerformedBy)
            .IsRequired(false);

        // CorrelationId: required, max length 100
        builder.Property(e => e.CorrelationId)
            .IsRequired()
            .HasMaxLength(100);

        // Timestamp: required, UTC timestamp with default
        builder.Property(e => e.Timestamp)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Add comment to table indicating it's append-only
        builder.ToTable(t => t.HasComment("Append-only audit log table. Immutable."));
    }
}

