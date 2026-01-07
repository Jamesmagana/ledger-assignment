using Ledger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Data.Configurations;

public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries");

        // Primary key
        builder.HasKey(e => e.Id);

        // ExternalId: nullable, unique partial index (WHERE ExternalId IS NOT NULL)
        builder.Property(e => e.ExternalId)
            .HasMaxLength(200);

        builder.HasIndex(e => e.ExternalId)
            .HasDatabaseName("IX_JournalEntries_ExternalId")
            .IsUnique()
            .HasFilter("\"ExternalId\" IS NOT NULL");

        // RequestHash: required, SHA-256 hex string (64 characters)
        builder.Property(e => e.RequestHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength();

        // PostedAt: required, UTC timestamp
        builder.Property(e => e.PostedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        // Timestamps: UTC, with defaults
        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(e => e.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}

