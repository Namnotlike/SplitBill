using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class ExpenseSplitConfiguration : IEntityTypeConfiguration<ExpenseSplit>
{
    public void Configure(EntityTypeBuilder<ExpenseSplit> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Amount).HasColumnType("bigint");

        // Chặn trùng người chịu tiền trong cùng 1 expense ở tầng DB (CLAUDE.md mục 4.3).
        builder.HasIndex(s => new { s.ExpenseId, s.GroupMemberId }).IsUnique();
    }
}
