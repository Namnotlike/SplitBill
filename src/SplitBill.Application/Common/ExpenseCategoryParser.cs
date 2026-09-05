using SplitBill.Domain.Enums;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Common;

/// <summary>Parse chuỗi Category từ request thành <see cref="ExpenseCategory"/> (CLAUDE.md mục 15.3)
/// — dùng chung giữa <c>ExpenseService</c> và <c>RecurringExpenseService</c> (mục 15.7) để tránh khai
/// báo trùng logic "null/rỗng mặc định Other, tên sai thì báo lỗi rõ ràng".</summary>
public static class ExpenseCategoryParser
{
    public static ExpenseCategory Parse(string? categoryText)
    {
        if (string.IsNullOrWhiteSpace(categoryText))
        {
            return ExpenseCategory.Other;
        }

        if (!Enum.TryParse<ExpenseCategory>(categoryText, ignoreCase: true, out var category))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Category '{categoryText}' không hợp lệ.");
        }

        return category;
    }
}
