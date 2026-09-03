using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class ReceiptImageConfiguration : IEntityTypeConfiguration<ReceiptImage>
{
    public void Configure(EntityTypeBuilder<ReceiptImage> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Content).HasColumnType("varbinary(max)").IsRequired();
        builder.Property(r => r.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(r => r.FileName).HasMaxLength(255).IsRequired();

        // Mỗi Expense chỉ giữ 1 ảnh gần nhất (CLAUDE.md mục 4.1).
        builder.HasIndex(r => r.ExpenseId).IsUnique();

        builder.HasOne(r => r.Expense)
            .WithMany()
            .HasForeignKey(r => r.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
