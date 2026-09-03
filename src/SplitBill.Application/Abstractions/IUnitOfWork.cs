namespace SplitBill.Application.Abstractions;

/// <summary>Trừu tượng cho việc lưu thay đổi xuống DB, cài đặt ở Infrastructure bằng EF Core.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
