using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class ReceiptImageRepository : IReceiptImageRepository
{
    private readonly SplitBillDbContext _dbContext;

    public ReceiptImageRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ReceiptImage?> GetByExpenseIdAsync(Guid expenseId, CancellationToken cancellationToken) =>
        _dbContext.ReceiptImages.FirstOrDefaultAsync(r => r.ExpenseId == expenseId, cancellationToken);

    public async Task UpsertAsync(ReceiptImage image, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.ReceiptImages.FirstOrDefaultAsync(r => r.ExpenseId == image.ExpenseId, cancellationToken);
        if (existing is null)
        {
            await _dbContext.ReceiptImages.AddAsync(image, cancellationToken);
            return;
        }

        existing.Content = image.Content;
        existing.ContentType = image.ContentType;
        existing.FileName = image.FileName;
        existing.SizeBytes = image.SizeBytes;
        existing.CreatedAt = image.CreatedAt;
    }
}
