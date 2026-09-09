using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class GroupTemplateConfiguration : IEntityTypeConfiguration<GroupTemplate>
{
    public void Configure(EntityTypeBuilder<GroupTemplate> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Type).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(10).IsRequired();

        builder.HasIndex(t => new { t.CreatedByUserId, t.IsDeleted, t.CreatedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}
