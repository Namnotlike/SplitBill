namespace SplitBill.Application.Expenses;

/// <summary>Preset cách chia hay dùng (CLAUDE.md mục 21).</summary>
public sealed record SplitPresetDto(
    Guid Id,
    Guid GroupId,
    string Name,
    string SplitMode,
    SplitConfigInput SplitConfig,
    DateTimeOffset CreatedAt);

public sealed record CreateSplitPresetRequest(string Name, string SplitMode, SplitConfigInput SplitConfig);
