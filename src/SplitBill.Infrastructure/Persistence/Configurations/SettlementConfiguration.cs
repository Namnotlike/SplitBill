using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Amount).HasColumnType("bigint");
        builder.Property(s => s.Note).HasMaxLength(500);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => new { s.GroupId, s.IsDeleted, s.Status });

        builder.HasOne(s => s.Group)
            .WithMany(g => g.Settlements)
            .HasForeignKey(s => s.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // Global query filter: settlement đã soft-delete mặc định không xuất hiện.
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
