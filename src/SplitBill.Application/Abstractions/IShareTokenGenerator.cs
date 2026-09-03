namespace SplitBill.Application.Abstractions;

/// <summary>Sinh chuỗi random 22 ký tự cho Group.ShareToken (CLAUDE.md mục 4.1).</summary>
public interface IShareTokenGenerator
{
    string Generate();
}
