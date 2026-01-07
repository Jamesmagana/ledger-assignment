using Ledger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        // Primary key
        builder.HasKey(e => e.Id);

        // Email: required, max length, case-insensitive unique
        builder.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(200);

        // Case-insensitive unique index on Email
        // This will be implemented in migration as: CREATE UNIQUE INDEX IX_Users_Email_Normalized ON "Users" (UPPER("Email"))
        builder.HasIndex(e => e.Email)
            .HasDatabaseName("IX_Users_Email_Normalized")
            .IsUnique();

        // PasswordHash: required, max length 200 (BCrypt hashes are ~60 chars)
        builder.Property(e => e.PasswordHash)
            .IsRequired()
            .HasMaxLength(200);

        // IsEnabled: required, default true
        builder.Property(e => e.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        // FailedLoginAttempts: required, default 0
        builder.Property(e => e.FailedLoginAttempts)
            .IsRequired()
            .HasDefaultValue(0);

        // LastLoginAt: nullable, UTC timestamp
        builder.Property(e => e.LastLoginAt)
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

