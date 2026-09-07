using Microsoft.EntityFrameworkCore;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence;

public sealed class SplitBillDbContext : DbContext
{
    public SplitBillDbContext(DbContextOptions<SplitBillDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpensePayer> ExpensePayers => Set<ExpensePayer>();
    public DbSet<ExpenseSplit> ExpenseSplits => Set<ExpenseSplit>();
    public DbSet<ReceiptImage> ReceiptImages => Set<ReceiptImage>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RecurringExpenseTemplate> RecurringExpenseTemplates => Set<RecurringExpenseTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SplitBillDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
