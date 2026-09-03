using SplitBill.Domain.Enums;

namespace SplitBill.Domain.Entities;

/// <summary>Một nhóm/cuộc chơi có phát sinh chi phí.</summary>
public sealed class Group
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GroupType Type { get; set; }
    public string Currency { get; set; } = "VND";
    public Guid CreatedByUserId { get; set; }
    public string ShareToken { get; set; } = string.Empty;
    public bool SimplifyDebts { get; set; } = true;
    public bool IsArchived { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<Settlement> Settlements { get; set; } = new List<Settlement>();
}
