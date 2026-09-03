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

        // Filtered unique index: nhiều User có thể có Email = NULL (không xảy ra thực tế vì
        // User luôn có email khi đăng ký, nhưng để nhất quán với việc khách vãng lai không có User record).
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL");
    }
}
