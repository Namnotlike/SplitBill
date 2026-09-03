using System.Security.Cryptography;
using SplitBill.Application.Abstractions;

namespace SplitBill.Infrastructure.Security;

/// <summary>Sinh chuỗi random 22 ký tự URL-safe cho Group.ShareToken (CLAUDE.md mục 4.1).</summary>
public sealed class ShareTokenGenerator : IShareTokenGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int Length = 22;

    public string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}
