using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.BankAccountNumber).HasMaxLength(50);
        builder.Property(u => u.BankBin).HasMaxLength(6);
        builder.Property(u => u.GoogleId).HasMaxLength(64);
        builder.Property(u => u.TwoFactorSecretEncrypted).HasMaxLength(500);

        // Filtered unique index: nhiều User có thể có Email = NULL (không xảy ra thực tế vì
        // User luôn có email khi đăng ký, nhưng để nhất quán với việc khách vãng lai không có User record).
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL");

        // Đăng nhập bằng Google (CLAUDE.md mục 25.3) — filtered unique index cùng mẫu Email ở trên,
        // vì phần lớn User chưa từng liên kết Google nên GoogleId sẽ NULL.
        builder.HasIndex(u => u.GoogleId)
            .IsUnique()
            .HasFilter("[GoogleId] IS NOT NULL");
    }
}
