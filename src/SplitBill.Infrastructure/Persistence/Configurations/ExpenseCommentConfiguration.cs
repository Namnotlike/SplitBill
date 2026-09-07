using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class ExpenseCommentConfiguration : IEntityTypeConfiguration<ExpenseComment>
{
    public void Configure(EntityTypeBuilder<ExpenseComment> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Content).HasMaxLength(2000).IsRequired();

        builder.HasIndex(c => new { c.ExpenseId, c.IsDeleted, c.CreatedAt });

        // DeleteBehavior.Restrict — cùng nguyên tắc mục 4.3: không cascade xóa dữ liệu liên quan tới
        // Expense/GroupMember, dù bình luận không phải dữ liệu tài chính.
        builder.HasOne(c => c.Expense)
            .WithMany()
            .HasForeignKey(c => c.ExpenseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.AuthorMember)
            .WithMany()
            .HasForeignKey(c => c.AuthorMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
