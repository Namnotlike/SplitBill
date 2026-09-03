using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class ExpensePayerConfiguration : IEntityTypeConfiguration<ExpensePayer>
{
    public void Configure(EntityTypeBuilder<ExpensePayer> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasColumnType("bigint");

        // Chặn trùng người ứng tiền trong cùng 1 expense ở tầng DB (CLAUDE.md mục 4.3).
        builder.HasIndex(p => new { p.ExpenseId, p.GroupMemberId }).IsUnique();
    }
}
