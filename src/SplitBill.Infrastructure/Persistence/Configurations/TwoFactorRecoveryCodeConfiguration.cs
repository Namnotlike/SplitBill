using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class TwoFactorRecoveryCodeConfiguration : IEntityTypeConfiguration<TwoFactorRecoveryCode>
{
    public void Configure(EntityTypeBuilder<TwoFactorRecoveryCode> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CodeHash).HasMaxLength(128).IsRequired();

        builder.HasIndex(c => c.CodeHash).IsUnique();
        builder.HasIndex(c => new { c.UserId, c.UsedAt });

        builder.HasOne(c => c.User)
            .WithMany(u => u.TwoFactorRecoveryCodes)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
