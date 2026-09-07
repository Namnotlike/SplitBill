using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SplitBill.Application.Auth;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses;
using SplitBill.Application.Export;
using SplitBill.Application.Groups;
using SplitBill.Application.Notifications;
using SplitBill.Application.RecurringExpenses;
using SplitBill.Application.Reminders;
using SplitBill.Application.Settlement;
using SplitBill.Application.Settlements;
using SplitBill.Application.Splitting;
using SplitBill.Application.Users;
using SplitBill.Application.VietQr;
using SplitBill.Infrastructure.Persistence;
using SplitBill.Infrastructure.Persistence.Repositories;
using SplitBill.Infrastructure.Security;

namespace SplitBill.IntegrationTests;

/// <summary>
/// Dựng sẵn DbContext (EF Core InMemory — CLAUDE.md mục 2) + repository thật + service thật cho
/// mỗi test, tránh cần thư viện mocking (không có trong danh sách NuGet được phép ở mục 2).
/// Mỗi test gọi <see cref="Create"/> để có một DB cô lập hoàn toàn với các test khác.
/// </summary>
public sealed class TestHarness : IDisposable
{
    public SplitBillDbContext DbContext { get; }

    public IAuthService AuthService { get; }
    public IUserService UserService { get; }
    public IGroupService GroupService { get; }
    public IExpenseService ExpenseService { get; }
    public IBalanceService BalanceService { get; }
    public ISettlementRecordService SettlementRecordService { get; }
    public IExportService ExportService { get; }
    public INotificationService NotificationService { get; }
    public IRecurringExpenseService RecurringExpenseService { get; }
    public IRecurringExpenseRunner RecurringExpenseRunner { get; }
    public IDebtReminderRunner DebtReminderRunner { get; }
    public IExpenseCommentService ExpenseCommentService { get; }
    public FakeEmailSender EmailSender { get; }

    private TestHarness(SplitBillDbContext dbContext)
    {
        DbContext = dbContext;

        var unitOfWork = new UnitOfWork(dbContext);
        var userRepository = new UserRepository(dbContext);
        var refreshTokenRepository = new RefreshTokenRepository(dbContext);
        var passwordResetTokenRepository = new PasswordResetTokenRepository(dbContext);
        var groupRepository = new GroupRepository(dbContext);
        var expenseRepository = new ExpenseRepository(dbContext);
        var settlementRepository = new SettlementRepository(dbContext);
        var auditLogRepository = new AuditLogRepository(dbContext);
        var receiptImageRepository = new ReceiptImageRepository(dbContext);

        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "SplitBill.Tests",
            Audience = "SplitBill.Tests",
            SigningKey = "test-signing-key-at-least-32-characters-long!",
            AccessTokenMinutes = 30,
            RefreshTokenDays = 14,
            PasswordResetTokenMinutes = 30,
        });
        var jwtTokenGenerator = new JwtTokenGenerator(jwtOptions);
        var webOptions = Options.Create(new WebOptions { BaseUrl = "http://localhost:5103" });
        var shareTokenGenerator = new ShareTokenGenerator();
        var splitCalculator = new ExpenseSplitCalculator();
        var balanceCalculator = new BalanceCalculator();
        var vietQrGenerator = new VietQrGenerator();
        var settlementRanker = new SocialSettlementRanker();
        var settlementPlanner = new SocialSettlementPlanner(settlementRanker);
        var notificationRepository = new NotificationRepository(dbContext);
        var recurringExpenseRepository = new RecurringExpenseRepository(dbContext);
        EmailSender = new FakeEmailSender();
        NotificationService = new NotificationService(notificationRepository, unitOfWork, EmailSender, NullLogger<NotificationService>.Instance, webOptions);

        AuthService = new AuthService(
            userRepository, refreshTokenRepository, passwordResetTokenRepository, unitOfWork,
            jwtTokenGenerator, EmailSender, webOptions, NullLogger<AuthService>.Instance);
        UserService = new UserService(userRepository, unitOfWork);
        GroupService = new GroupService(
            groupRepository, userRepository, expenseRepository, settlementRepository,
            auditLogRepository, unitOfWork, shareTokenGenerator, balanceCalculator, NotificationService);
        ExpenseService = new ExpenseService(
            expenseRepository, groupRepository, auditLogRepository, unitOfWork, splitCalculator, receiptImageRepository, NotificationService);
        BalanceService = new BalanceService(groupRepository, expenseRepository, settlementRepository, balanceCalculator, vietQrGenerator, settlementPlanner);
        SettlementRecordService = new SettlementRecordService(groupRepository, settlementRepository, auditLogRepository, unitOfWork, NotificationService);
        ExportService = new ExportService(ExpenseService, GroupService, BalanceService);
        RecurringExpenseService = new RecurringExpenseService(recurringExpenseRepository, groupRepository, splitCalculator, unitOfWork);
        RecurringExpenseRunner = new RecurringExpenseRunner(
            recurringExpenseRepository, groupRepository, expenseRepository, auditLogRepository,
            splitCalculator, NotificationService, unitOfWork, NullLogger<RecurringExpenseRunner>.Instance);
        DebtReminderRunner = new DebtReminderRunner(
            settlementRepository, groupRepository, NotificationService, unitOfWork, NullLogger<DebtReminderRunner>.Instance);
        var expenseCommentRepository = new ExpenseCommentRepository(dbContext);
        ExpenseCommentService = new ExpenseCommentService(expenseCommentRepository, expenseRepository, groupRepository, unitOfWork);
    }

    public static TestHarness Create()
    {
        var options = new DbContextOptionsBuilder<SplitBillDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestHarness(new SplitBillDbContext(options));
    }

    /// <summary>Đăng ký 1 user thật qua AuthService rồi trả về Id — tiện cho việc dựng dữ liệu test.</summary>
    public async Task<Guid> RegisterUserAsync(string email, string displayName, string password = "Passw0rd123")
    {
        await AuthService.RegisterAsync(new RegisterRequest(email, password, displayName), CancellationToken.None);
        return DbContext.Users.Single(u => u.Email == email).Id;
    }

    public void Dispose() => DbContext.Dispose();
}
