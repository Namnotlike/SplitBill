using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SplitBill.Application.Auth;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Notifications;
using SplitBill.Application.RecurringExpenses;
using SplitBill.Application.Settlements;
using SplitBill.Application.Users;

namespace SplitBill.Web.Services;

/// <summary>
/// Client HTTP gọi SplitBill.Api. Tái dùng thẳng DTO từ SplitBill.Application để không phải khai
/// báo lại request/response shape (Web tham chiếu Application chỉ để lấy DTO, không gọi service
/// trong-process — mọi nghiệp vụ vẫn đi qua API thật, đúng ranh giới CLAUDE.md mục 3).
/// </summary>
public sealed class SplitBillApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _googleAuthInternalSecret;

    public SplitBillApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        // Bí mật dùng chung bảo vệ POST /auth/google (CLAUDE.md mục 25.3) — phải khớp
        // GoogleAuth:InternalSecret bên SplitBill.Api, đọc thẳng qua IConfiguration (không cần
        // strongly-typed options riêng cho 1 giá trị đơn lẻ dùng ở đúng 1 chỗ).
        _googleAuthInternalSecret = configuration["GoogleAuth:InternalSecret"] ?? string.Empty;
    }

    // ===== Auth =====
    public Task<AuthTokens> RegisterAsync(RegisterRequest request, CancellationToken ct) =>
        PostAsync<RegisterRequest, AuthTokens>("auth/register", request, ct);

    public Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken ct) =>
        PostAsync<LoginRequest, AuthTokens>("auth/login", request, ct);

    public Task LogoutAsync(string refreshToken, CancellationToken ct) =>
        PostNoContentAsync("auth/logout", new { refreshToken }, ct);

    // CLAUDE.md mục 16 — Quên mật khẩu (bổ sung 2026-09-07).
    public Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct) =>
        PostNoContentAsync("auth/forgot-password", request, ct);

    public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct) =>
        PostNoContentAsync("auth/reset-password", request, ct);

    /// <summary>Đăng nhập bằng Google (CLAUDE.md mục 25.3) — khác các call auth/* khác ở chỗ cần đính
    /// kèm header X-Internal-Secret nên không dùng được helper PostAsync chung.</summary>
    public async Task<AuthTokens> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "auth/google")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        message.Headers.Add("X-Internal-Secret", _googleAuthInternalSecret);

        var response = await _httpClient.SendAsync(message, ct);
        return await ReadOrThrowAsync<AuthTokens>(response, ct);
    }

    // ===== Users =====
    public Task<UserProfileDto> GetMeAsync(CancellationToken ct) => GetAsync<UserProfileDto>("users/me", ct);

    public Task<UserProfileDto> UpdateMeAsync(UpdateProfileRequest request, CancellationToken ct) =>
        PatchAsync<UpdateProfileRequest, UserProfileDto>("users/me", request, ct);

    /// <summary>Bảng tổng quan cá nhân ở trang chủ (CLAUDE.md mục 15.4).</summary>
    public Task<IReadOnlyList<PersonalGroupBalanceDto>> GetMyBalancesOverviewAsync(CancellationToken ct) =>
        GetAsync<IReadOnlyList<PersonalGroupBalanceDto>>("users/me/balances-overview", ct);

    /// <summary>"Ai đang nợ tôi" xuyên nhóm, gộp theo từng người (CLAUDE.md mục 20).</summary>
    public Task<IReadOnlyList<CounterpartyBalanceDto>> GetMyCounterpartyBalancesAsync(CancellationToken ct) =>
        GetAsync<IReadOnlyList<CounterpartyBalanceDto>>("users/me/counterparty-balances", ct);

    /// <summary>Dashboard cá nhân nâng cao (CLAUDE.md mục 25.5).</summary>
    public Task<PersonalDashboardDto> GetMyDashboardAsync(CancellationToken ct) =>
        GetAsync<PersonalDashboardDto>("users/me/dashboard", ct);

    // ===== Groups =====
    public Task<IReadOnlyList<GroupSummaryDto>> GetMyGroupsAsync(CancellationToken ct) =>
        GetAsync<IReadOnlyList<GroupSummaryDto>>("groups", ct);

    public Task<GroupDto> GetGroupAsync(Guid id, CancellationToken ct) => GetAsync<GroupDto>($"groups/{id}", ct);

    public Task<GroupDto> CreateGroupAsync(CreateGroupRequest request, CancellationToken ct) =>
        PostAsync<CreateGroupRequest, GroupDto>("groups", request, ct);

    /// <summary>Nhân bản nhóm (CLAUDE.md mục 18).</summary>
    public Task<GroupDto> DuplicateGroupAsync(Guid id, DuplicateGroupRequest request, CancellationToken ct) =>
        PostAsync<DuplicateGroupRequest, GroupDto>($"groups/{id}/duplicate", request, ct);

    public Task<GroupDto> UpdateGroupAsync(Guid id, UpdateGroupRequest request, CancellationToken ct) =>
        PatchAsync<UpdateGroupRequest, GroupDto>($"groups/{id}", request, ct);

    public Task DeleteGroupAsync(Guid id, CancellationToken ct) => DeleteAsync($"groups/{id}", ct);

    public Task<GroupDto> GetSharedGroupAsync(string shareToken, CancellationToken ct) =>
        GetAsync<GroupDto>($"groups/shared/{shareToken}", ct);

    /// <summary>Tham gia nhóm qua link chia sẻ — yêu cầu đăng nhập (CLAUDE.md mục 15.6).</summary>
    public Task<GroupMemberDto> JoinGroupAsync(string shareToken, CancellationToken ct) =>
        PostAsync<object?, GroupMemberDto>($"groups/shared/{shareToken}/join", null, ct);

    public async Task<string> RotateShareTokenAsync(Guid id, CancellationToken ct)
    {
        var result = await PostAsync<object?, Dictionary<string, string>>($"groups/{id}/share-token/rotate", null, ct);
        return result["shareToken"];
    }

    public Task<GroupMemberDto> AddMemberAsync(Guid groupId, AddMemberRequest request, CancellationToken ct) =>
        PostAsync<AddMemberRequest, GroupMemberDto>($"groups/{groupId}/members", request, ct);

    public Task<GroupMemberDto> UpdateMemberAsync(Guid groupId, Guid memberId, UpdateMemberRequest request, CancellationToken ct) =>
        PatchAsync<UpdateMemberRequest, GroupMemberDto>($"groups/{groupId}/members/{memberId}", request, ct);

    public Task RemoveMemberAsync(Guid groupId, Guid memberId, CancellationToken ct) =>
        DeleteAsync($"groups/{groupId}/members/{memberId}", ct);

    // ===== Expenses =====
    /// <summary><paramref name="filter"/> theo CLAUDE.md mục 15.2 — mọi field null bị bỏ qua khỏi
    /// query string, giữ nguyên hành vi "xem tất cả" khi gọi với ExpenseFilter.Empty.</summary>
    public Task<PagedResult<ExpenseDto>> GetExpensesAsync(Guid groupId, int page, int pageSize, ExpenseFilter filter, CancellationToken ct)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(filter.Title))
        {
            query.Add($"title={Uri.EscapeDataString(filter.Title)}");
        }
        if (filter.PayerMemberId is { } payerMemberId)
        {
            query.Add($"payerMemberId={payerMemberId}");
        }
        if (filter.FromDate is { } fromDate)
        {
            query.Add($"fromDate={Uri.EscapeDataString(fromDate.ToString("O"))}");
        }
        if (filter.ToDate is { } toDate)
        {
            query.Add($"toDate={Uri.EscapeDataString(toDate.ToString("O"))}");
        }
        if (filter.MinAmount is { } minAmount)
        {
            query.Add($"minAmount={minAmount}");
        }
        if (filter.MaxAmount is { } maxAmount)
        {
            query.Add($"maxAmount={maxAmount}");
        }
        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query.Add($"category={Uri.EscapeDataString(filter.Category)}");
        }

        return GetAsync<PagedResult<ExpenseDto>>($"groups/{groupId}/expenses?{string.Join("&", query)}", ct);
    }

    public Task<ExpenseDto> GetExpenseAsync(Guid expenseId, CancellationToken ct) => GetAsync<ExpenseDto>($"expenses/{expenseId}", ct);

    public Task<ExpenseResult> CreateExpenseAsync(Guid groupId, CreateExpenseRequest request, CancellationToken ct) =>
        PostAsync<CreateExpenseRequest, ExpenseResult>($"groups/{groupId}/expenses", request, ct);

    public Task<ExpenseResult> UpdateExpenseAsync(Guid expenseId, UpdateExpenseRequest request, CancellationToken ct) =>
        PutAsync<UpdateExpenseRequest, ExpenseResult>($"expenses/{expenseId}", request, ct);

    public Task DeleteExpenseAsync(Guid expenseId, CancellationToken ct) => DeleteAsync($"expenses/{expenseId}", ct);

    // ===== Khôi phục khoản chi/thanh toán đã xóa (CLAUDE.md mục 24) =====
    public Task<IReadOnlyList<ExpenseDto>> GetDeletedExpensesAsync(Guid groupId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<ExpenseDto>>($"groups/{groupId}/deleted-expenses", ct);

    public Task<ExpenseDto> RestoreExpenseAsync(Guid expenseId, CancellationToken ct) =>
        PostAsync<object?, ExpenseDto>($"expenses/{expenseId}/restore", null, ct);

    // ===== Preset cách chia hay dùng (CLAUDE.md mục 21) =====
    public Task<IReadOnlyList<SplitPresetDto>> GetSplitPresetsAsync(Guid groupId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<SplitPresetDto>>($"groups/{groupId}/split-presets", ct);

    public Task<SplitPresetDto> CreateSplitPresetAsync(Guid groupId, CreateSplitPresetRequest request, CancellationToken ct) =>
        PostAsync<CreateSplitPresetRequest, SplitPresetDto>($"groups/{groupId}/split-presets", request, ct);

    public Task DeleteSplitPresetAsync(Guid presetId, CancellationToken ct) =>
        DeleteAsync($"split-presets/{presetId}", ct);

    public Task<PreviewSplitResult> PreviewSplitAsync(PreviewSplitRequest request, CancellationToken ct) =>
        PostAsync<PreviewSplitRequest, PreviewSplitResult>("expenses/preview-split", request, ct);

    // ===== Mẫu nhóm tái sử dụng (CLAUDE.md mục 25.6) =====
    public Task<IReadOnlyList<GroupTemplateDto>> GetMyGroupTemplatesAsync(CancellationToken ct) =>
        GetAsync<IReadOnlyList<GroupTemplateDto>>("group-templates", ct);

    public Task<GroupTemplateDto> CreateGroupTemplateAsync(CreateGroupTemplateRequest request, CancellationToken ct) =>
        PostAsync<CreateGroupTemplateRequest, GroupTemplateDto>("group-templates", request, ct);

    public Task DeleteGroupTemplateAsync(Guid templateId, CancellationToken ct) =>
        DeleteAsync($"group-templates/{templateId}", ct);

    public Task<GroupDto> CreateGroupFromTemplateAsync(Guid templateId, CreateGroupFromTemplateRequest request, CancellationToken ct) =>
        PostAsync<CreateGroupFromTemplateRequest, GroupDto>($"group-templates/{templateId}/create-group", request, ct);

    // ===== Khoản chi định kỳ (CLAUDE.md mục 15.7) =====
    public Task<RecurringExpenseTemplateDto> CreateRecurringExpenseAsync(Guid groupId, CreateRecurringExpenseRequest request, CancellationToken ct) =>
        PostAsync<CreateRecurringExpenseRequest, RecurringExpenseTemplateDto>($"groups/{groupId}/recurring-expenses", request, ct);

    public Task<IReadOnlyList<RecurringExpenseTemplateDto>> GetRecurringExpensesAsync(Guid groupId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<RecurringExpenseTemplateDto>>($"groups/{groupId}/recurring-expenses", ct);

    public Task DeactivateRecurringExpenseAsync(Guid templateId, CancellationToken ct) =>
        PostNoContentAsync($"recurring-expenses/{templateId}/deactivate", new { }, ct);

    // Ảnh hóa đơn lưu trong SQL Server, phục vụ qua Api có [Authorize] + kiểm tra thành viên nhóm
    // (CLAUDE.md mục 8) — Web phải proxy qua đây (kèm sẵn Bearer token qua BearerTokenHandler),
    // không bao giờ để trình duyệt gọi thẳng Api (sẽ thiếu token, 401).
    public async Task UploadReceiptImageAsync(Guid expenseId, Stream content, string fileName, string contentType, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "file", fileName);

        var response = await _httpClient.PostAsync($"expenses/{expenseId}/receipt-image", form, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<ReceiptImageContentDto> GetReceiptImageAsync(Guid expenseId, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"expenses/{expenseId}/receipt-image", ct);
        await EnsureSuccessAsync(response, ct);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"')
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? "receipt";

        return new ReceiptImageContentDto(bytes, contentType, fileName);
    }

    // ===== Bình luận khoản chi (CLAUDE.md mục 19) =====
    public Task<IReadOnlyList<ExpenseCommentDto>> GetExpenseCommentsAsync(Guid expenseId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<ExpenseCommentDto>>($"expenses/{expenseId}/comments", ct);

    public Task<ExpenseCommentDto> AddExpenseCommentAsync(Guid expenseId, CreateExpenseCommentRequest request, CancellationToken ct) =>
        PostAsync<CreateExpenseCommentRequest, ExpenseCommentDto>($"expenses/{expenseId}/comments", request, ct);

    public Task DeleteExpenseCommentAsync(Guid commentId, CancellationToken ct) =>
        DeleteAsync($"expense-comments/{commentId}", ct);

    // ===== Balances / Settlement =====
    public Task<IReadOnlyList<MemberBalanceDto>> GetBalancesAsync(Guid groupId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<MemberBalanceDto>>($"groups/{groupId}/balances", ct);

    public Task<SettlementPlanDto> GetSettlementPlanAsync(Guid groupId, CancellationToken ct) =>
        GetAsync<SettlementPlanDto>($"groups/{groupId}/settlement-plan", ct);

    public Task<IReadOnlyList<SettlementDto>> GetSettlementsAsync(Guid groupId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<SettlementDto>>($"groups/{groupId}/settlements", ct);

    // ===== Export =====
    public Task<byte[]> ExportExpensesCsvAsync(Guid groupId, CancellationToken ct) =>
        GetBytesAsync($"groups/{groupId}/export/expenses.csv", ct);

    public Task<byte[]> ExportBalancesCsvAsync(Guid groupId, CancellationToken ct) =>
        GetBytesAsync($"groups/{groupId}/export/balances.csv", ct);

    /// <summary>Xuất/backup toàn bộ dữ liệu tài chính cốt lõi của nhóm dạng JSON (CLAUDE.md mục 25.4).</summary>
    public Task<byte[]> ExportGroupBackupJsonAsync(Guid groupId, CancellationToken ct) =>
        GetBytesAsync($"groups/{groupId}/export/backup.json", ct);

    /// <summary>Timeline hoạt động nhóm (CLAUDE.md mục 15.5).</summary>
    public Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(Guid groupId, int page, int pageSize, CancellationToken ct) =>
        GetAsync<PagedResult<AuditLogDto>>($"groups/{groupId}/audit-logs?page={page}&pageSize={pageSize}", ct);

    public Task<SettlementDto> CreateSettlementAsync(Guid groupId, CreateSettlementRequest request, CancellationToken ct) =>
        PostAsync<CreateSettlementRequest, SettlementDto>($"groups/{groupId}/settlements", request, ct);

    /// <summary>Miễn nợ (CLAUDE.md mục 25.2) — tạo trực tiếp settlement Confirmed, chỉ chủ nợ
    /// (request.ToMemberId) gọi được.</summary>
    public Task<SettlementDto> WaiveSettlementAsync(Guid groupId, WaiveSettlementRequest request, CancellationToken ct) =>
        PostAsync<WaiveSettlementRequest, SettlementDto>($"groups/{groupId}/settlements/waive", request, ct);

    public Task<SettlementDto> ConfirmSettlementAsync(Guid settlementId, CancellationToken ct) =>
        PostAsync<object?, SettlementDto>($"settlements/{settlementId}/confirm", null, ct);

    public Task<SettlementDto> RejectSettlementAsync(Guid settlementId, CancellationToken ct) =>
        PostAsync<object?, SettlementDto>($"settlements/{settlementId}/reject", null, ct);

    public Task DeleteSettlementAsync(Guid settlementId, CancellationToken ct) => DeleteAsync($"settlements/{settlementId}", ct);

    public Task<IReadOnlyList<SettlementDto>> GetDeletedSettlementsAsync(Guid groupId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<SettlementDto>>($"groups/{groupId}/deleted-settlements", ct);

    public Task<SettlementDto> RestoreSettlementAsync(Guid settlementId, CancellationToken ct) =>
        PostAsync<object?, SettlementDto>($"settlements/{settlementId}/restore", null, ct);

    // ===== Notifications (CLAUDE.md mục 13) =====
    public Task<PagedResult<NotificationDto>> GetNotificationsAsync(int page, int pageSize, CancellationToken ct) =>
        GetAsync<PagedResult<NotificationDto>>($"notifications?page={page}&pageSize={pageSize}", ct);

    public Task<UnreadCountDto> GetUnreadNotificationCountAsync(CancellationToken ct) =>
        GetAsync<UnreadCountDto>("notifications/unread-count", ct);

    public Task MarkNotificationAsReadAsync(Guid id, CancellationToken ct) =>
        PostNoContentAsync($"notifications/{id}/read", new { }, ct);

    public Task MarkAllNotificationsAsReadAsync(CancellationToken ct) =>
        PostNoContentAsync("notifications/read-all", new { }, ct);

    // ===== Web Push (CLAUDE.md mục 25.7) =====
    public Task<VapidPublicKeyDto> GetVapidPublicKeyAsync(CancellationToken ct) =>
        GetAsync<VapidPublicKeyDto>("users/me/push-vapid-public-key", ct);

    public Task SubscribeToPushAsync(CreatePushSubscriptionRequest request, CancellationToken ct) =>
        PostNoContentAsync("users/me/push-subscriptions", request, ct);

    public Task UnsubscribeFromPushAsync(string endpoint, CancellationToken ct) =>
        DeleteAsync($"users/me/push-subscriptions?endpoint={Uri.EscapeDataString(endpoint)}", ct);

    // ===== Tìm kiếm xuyên nhóm (CLAUDE.md mục 25.8) =====
    public Task<GlobalSearchResultDto> GlobalSearchAsync(string query, CancellationToken ct) =>
        GetAsync<GlobalSearchResultDto>($"users/me/search?q={Uri.EscapeDataString(query)}", ct);

    // ===== Helpers =====
    private async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync(path, ct);
        return await ReadOrThrowAsync<TResponse>(response, ct);
    }

    private async Task<byte[]> GetBytesAsync(string path, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest? body, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, ct);
        return await ReadOrThrowAsync<TResponse>(response, ct);
    }

    private async Task PostNoContentAsync<TRequest>(string path, TRequest body, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<TResponse> PatchAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct)
    {
        var response = await _httpClient.PatchAsJsonAsync(path, body, JsonOptions, ct);
        return await ReadOrThrowAsync<TResponse>(response, ct);
    }

    private async Task<TResponse> PutAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct)
    {
        var response = await _httpClient.PutAsJsonAsync(path, body, JsonOptions, ct);
        return await ReadOrThrowAsync<TResponse>(response, ct);
    }

    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        var response = await _httpClient.DeleteAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private static async Task<TResponse> ReadOrThrowAsync<TResponse>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
        return result ?? throw new ApiException((int)response.StatusCode, "EMPTY_RESPONSE", "Server trả về nội dung rỗng.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorCode = "UNKNOWN_ERROR";
        var message = $"Lỗi không xác định (HTTP {(int)response.StatusCode}).";

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsBody>(JsonOptions, ct);
            if (problem is not null)
            {
                errorCode = problem.ErrorCode ?? errorCode;
                message = problem.Title ?? message;
            }
        }
        catch
        {
            // body không phải JSON hợp lệ — giữ message mặc định.
        }

        throw new ApiException((int)response.StatusCode, errorCode, message);
    }

    private sealed record ProblemDetailsBody(string? Title, string? ErrorCode);
}
