using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class SplitPresetConfiguration : IEntityTypeConfiguration<SplitPreset>
{
    public void Configure(EntityTypeBuilder<SplitPreset> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.SplitMode).HasMaxLength(20).IsRequired();

        builder.HasIndex(p => new { p.GroupId, p.IsDeleted, p.CreatedAt });

        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GroupMember>()
            .WithMany()
            .HasForeignKey(p => p.CreatedByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
