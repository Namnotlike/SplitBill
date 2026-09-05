using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class RecurringExpenseTemplateConfiguration : IEntityTypeConfiguration<RecurringExpenseTemplate>
{
    public void Configure(EntityTypeBuilder<RecurringExpenseTemplate> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.TotalAmount).HasColumnType("bigint");
        builder.Property(t => t.ExtraFeeAmount).HasColumnType("bigint");
        builder.Property(t => t.Note).HasMaxLength(2000);
        builder.Property(t => t.PayersJson).IsRequired();

        // Runner quét theo (IsActive, NextRunAt) mỗi lượt — index composite giúp query GetDueTemplatesAsync
        // (CLAUDE.md mục 15.7) không phải quét toàn bảng khi số nhóm/mẫu lớn dần theo thời gian.
        builder.HasIndex(t => new { t.IsActive, t.NextRunAt });

        builder.HasOne(t => t.Group)
            .WithMany()
            .HasForeignKey(t => t.GroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
