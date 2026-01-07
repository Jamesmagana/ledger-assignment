using Ledger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Data.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");

        // Primary key
        builder.HasKey(e => e.Id);

        // Name: required, max length, case-insensitive unique
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        // Case-insensitive unique index on Name
        // This will be implemented in migration as: CREATE UNIQUE INDEX IX_Accounts_Name_Normalized ON "Accounts" (UPPER("Name"))
        builder.HasIndex(e => e.Name)
            .HasDatabaseName("IX_Accounts_Name_Normalized")
            .IsUnique();

        // Type: required, stored as integer
        builder.Property(e => e.Type)
            .IsRequired()
            .HasConversion<int>();

        // IsActive: required, default true
        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

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

