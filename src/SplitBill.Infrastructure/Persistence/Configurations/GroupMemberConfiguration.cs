using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.DisplayName).HasMaxLength(100).IsRequired();

        // Một User không được là 2 member cùng lúc trong 1 nhóm. Khách vãng lai (UserId == null)
        // không bị ràng buộc này (CLAUDE.md mục 4.3).
        builder.HasIndex(m => new { m.GroupId, m.UserId })
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL");

        builder.HasOne(m => m.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
