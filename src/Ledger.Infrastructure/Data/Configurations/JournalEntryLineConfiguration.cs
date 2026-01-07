using Ledger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Data.Configurations;

public class JournalEntryLineConfiguration : IEntityTypeConfiguration<JournalEntryLine>
{
    public void Configure(EntityTypeBuilder<JournalEntryLine> builder)
    {
        builder.ToTable("JournalEntryLines");

        // Primary key
        builder.HasKey(e => e.Id);

        // JournalEntryId: required, foreign key, no cascade delete
        builder.Property(e => e.JournalEntryId)
            .IsRequired();

        builder.HasOne<JournalEntry>()
            .WithMany()
            .HasForeignKey(e => e.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        // AccountId: required, foreign key, no cascade delete
        builder.Property(e => e.AccountId)
            .IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Direction: required, stored as integer
        builder.Property(e => e.Direction)
            .IsRequired()
            .HasConversion<int>();

        // Amount: required, numeric(20,4) precision, CHECK constraint > 0
        builder.Property(e => e.Amount)
            .IsRequired()
            .HasPrecision(20, 4)
            .HasColumnType("numeric(20,4)");

        // CHECK constraint: Amount must be greater than zero
        builder.ToTable(t => t.HasCheckConstraint("CK_JournalEntryLine_AmountPositive", "\"Amount\" > 0"));
    }
}

