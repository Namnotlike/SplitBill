namespace SplitBill.Application.Users;

public sealed record UserProfileDto(
    Guid Id,
    string? Email,
    string DisplayName,
    string? BankAccountNumber,
    string? BankBin);

public sealed record UpdateProfileRequest(string DisplayName, string? BankAccountNumber, string? BankBin);
