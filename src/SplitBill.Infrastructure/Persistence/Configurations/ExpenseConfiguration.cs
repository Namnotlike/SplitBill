using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.TotalAmount).HasColumnType("bigint");
        builder.Property(e => e.ExtraFeeAmount).HasColumnType("bigint");
        builder.Property(e => e.Note).HasMaxLength(2000);
        builder.Property(e => e.ReceiptImageUrl).HasMaxLength(1000);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => new { e.GroupId, e.IsDeleted, e.OccurredAt });

        builder.HasOne(e => e.Group)
            .WithMany(g => g.Expenses)
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Payers)
            .WithOne(p => p.Expense)
            .HasForeignKey(p => p.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Splits)
            .WithOne(s => s.Expense)
            .HasForeignKey(s => s.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Global query filter: expense đã soft-delete mặc định không xuất hiện.
        // Dùng .IgnoreQueryFilters() khi cần xem lịch sử.
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
