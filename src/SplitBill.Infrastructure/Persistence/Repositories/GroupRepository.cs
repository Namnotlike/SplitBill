using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class GroupRepository : IGroupRepository
{
    private readonly SplitBillDbContext _dbContext;

    public GroupRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Group?> GetByIdWithMembersAsync(Guid groupId, CancellationToken cancellationToken) =>
        _dbContext.Groups.Include(g => g.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);

    public Task<Group?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken) =>
        _dbContext.Groups.Include(g => g.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(g => g.ShareToken == shareToken, cancellationToken);

    public Task<List<Group>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.Groups
            .Include(g => g.Members)
            .Where(g => g.Members.Any(m => m.UserId == userId && m.IsActive))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Group group, CancellationToken cancellationToken) =>
        await _dbContext.Groups.AddAsync(group, cancellationToken);

    public Task<bool> ShareTokenExistsAsync(string shareToken, CancellationToken cancellationToken) =>
        _dbContext.Groups.AnyAsync(g => g.ShareToken == shareToken, cancellationToken);

    public Task<GroupMember?> GetMemberByIdAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken) =>
        _dbContext.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.Id == memberId, cancellationToken);

    public async Task AddMemberAsync(GroupMember member, CancellationToken cancellationToken) =>
        await _dbContext.GroupMembers.AddAsync(member, cancellationToken);
}
