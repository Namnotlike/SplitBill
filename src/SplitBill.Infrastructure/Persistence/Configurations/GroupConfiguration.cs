using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(1000);
        builder.Property(g => g.Currency).HasMaxLength(3).IsRequired();
        builder.Property(g => g.ShareToken).HasMaxLength(22).IsRequired();

        builder.HasIndex(g => g.ShareToken).IsUnique();

        // Global query filter: nhóm đã soft-delete thì mặc định không xuất hiện.
        builder.HasQueryFilter(g => !g.IsDeleted);
    }
}
