using System.Security.Cryptography;
using System.Text;

namespace MonoLauncher.Core.Auth;

public enum AccountKind { Microsoft, Offline }

public sealed class MinecraftAccount
{
    public required string Id { get; set; }
    public AccountKind Kind { get; set; }
    public required string Name { get; set; }
    public required string Uuid { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Xuid { get; set; }
    public string? SkinUrl { get; set; }
    public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.Now;

    public bool IsExpired => ExpiresAt is { } exp && exp < DateTimeOffset.Now.AddMinutes(2);

    /// <summary>The value passed to --accessToken (never logged, never persisted in cleartext).</summary>
    public string EffectiveAccessToken => AccessToken ?? "0";

    public string UuidWithoutDashes => Uuid.Replace("-", "").ToLowerInvariant();

    public static string NewId() => Guid.NewGuid().ToString("N");
}

public sealed class MsaDeviceCode
{
    public required string UserCode { get; init; }
    public required string DeviceCode { get; init; }
    public required string VerificationUri { get; init; }
    public string Message { get; init; } = "";
    public int ExpiresIn { get; init; } = 900;
    public int Interval { get; init; } = 5;
}

public sealed class AuthException(string message, string? code = null) : Exception(message)
{
    /// <summary>Machine readable error code (e.g. XSTS error, oauth error).</summary>
    public string? Code { get; } = code;
}

public static class OfflineIdentity
{
    /// <summary>Produces the same v3 UUID as vanilla offline mode: MD5("OfflinePlayer:"+name).</summary>
    public static string UuidFor(string name)
    {
        var input = Encoding.UTF8.GetBytes("OfflinePlayer:" + name);
        var hash = MD5.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // IETF variant
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    public static MinecraftAccount Create(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 16)
            throw new ArgumentException("离线用户名长度必须为 1-16 个字符", nameof(name));
        return new MinecraftAccount
        {
            Id = MinecraftAccount.NewId(),
            Kind = AccountKind.Offline,
            Name = trimmed,
            Uuid = UuidFor(trimmed),
            AccessToken = "0",
            ExpiresAt = DateTimeOffset.MaxValue,
            LastUsed = DateTimeOffset.Now
        };
    }
}
