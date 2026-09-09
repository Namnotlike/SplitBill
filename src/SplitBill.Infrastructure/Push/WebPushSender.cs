using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SplitBill.Application.Common;
using SplitBill.Application.Notifications;
using WebPush;

// Namespace CỐ Ý là "SplitBill.Infrastructure.Push", KHÔNG phải "...WebPush" — trùng tên với
// namespace "WebPush" của thư viện NuGet sẽ khiến `using WebPush;` bên dưới bị namespace bao quanh
// che khuất (C# ưu tiên namespace lồng gần hơn `using`), buộc phải viết đủ "global::WebPush.X" ở mọi
// chỗ dùng WebPushClient/VapidDetails/PushSubscription của thư viện — đổi tên namespace tránh hẳn vấn
// đề này thay vì phải alias từng type.
namespace SplitBill.Infrastructure.Push;

/// <summary>Cài đặt CLAUDE.md mục 25.7 bằng thư viện WebPush (ký VAPID + mã hóa payload theo chuẩn
/// Web Push, RFC 8291/8292). <c>PushSubscription</c> ở đây là type CỦA THƯ VIỆN (namespace
/// <c>WebPush</c>), KHÁC HẲN <c>SplitBill.Domain.Entities.PushSubscription</c> — file này không bao
/// giờ tham chiếu tới Domain entity, chỉ nhận <see cref="PushSubscriptionTarget"/> (DTO thuần của
/// Application layer) làm tham số, giữ đúng ranh giới lớp.</summary>
public sealed class WebPushSender : IWebPushSender
{
    private readonly WebPushClient _client = new();
    private readonly WebPushOptions _options;

    public WebPushSender(IOptions<WebPushOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(PushSubscriptionTarget subscription, string title, string body, string? url, CancellationToken cancellationToken)
    {
        var libSubscription = new PushSubscription(subscription.Endpoint, subscription.P256dhKey, subscription.AuthKey);
        var vapidDetails = new VapidDetails(_options.VapidSubject, _options.VapidPublicKey, _options.VapidPrivateKey);
        var payload = JsonSerializer.Serialize(new { title, body, url });

        try
        {
            await _client.SendNotificationAsync(libSubscription, payload, vapidDetails, cancellationToken);
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // 404/410 nghĩa là push service (FCM/Mozilla...) xác nhận subscription này không còn tồn
            // tại — người dùng đã gỡ app/xóa dữ liệu trình duyệt/thu hồi quyền. Không phải lỗi thật,
            // chuyển thành exception riêng để NotificationService tự xóa khỏi DB (self-heal).
            throw new PushSubscriptionGoneException($"Push subscription {subscription.Endpoint} không còn hợp lệ ({(int)ex.StatusCode}).");
        }
    }
}
