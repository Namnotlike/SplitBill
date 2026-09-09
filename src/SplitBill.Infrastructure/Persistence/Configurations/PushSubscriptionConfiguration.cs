using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Configurations;

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Endpoint).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.P256dhKey).HasMaxLength(500).IsRequired();
        builder.Property(p => p.AuthKey).HasMaxLength(500).IsRequired();

        // Endpoint là URL do push service cấp, có thể dài hơn giới hạn mặc định của index SQL Server
        // (900 byte) — dùng chỉ mục không unique-constraint ở DB (unique thật sự được đảm bảo ở tầng
        // Application qua GetByEndpointAsync + upsert, xem NotificationService.SubscribeAsync).
        builder.HasIndex(p => p.UserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
