using SplitBill.Application.Abstractions;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Users;

/// <summary>Cài đặt `/users/me` (CLAUDE.md mục 8).</summary>
public sealed class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.InvalidCredentials, "Tài khoản không tồn tại.");

        return ToDto(user);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.InvalidCredentials, "Tài khoản không tồn tại.");

        user.DisplayName = request.DisplayName;
        user.BankAccountNumber = request.BankAccountNumber;
        user.BankBin = request.BankBin;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    private static UserProfileDto ToDto(Domain.Entities.User user) =>
        new(user.Id, user.Email, user.DisplayName, user.BankAccountNumber, user.BankBin);
}
