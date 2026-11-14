using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonoLauncher.Core.Auth;

/// <summary>
/// Microsoft account login for Minecraft (device code flow -> Xbox Live -> XSTS -> Minecraft services).
/// No client secret is embedded: the device-code flow only needs a public client id.
/// The id can be overridden through settings for users who want their own Azure application.
/// </summary>
public sealed class MicrosoftAuthService
{
    public const string DefaultClientId = "00000000402b5328"; // public Minecraft launcher client id
    private const string Scope = "service::user.auth.xboxlive.com::MBI_SSL";

    private readonly HttpClient _http;
    private readonly string _clientId;

    public MicrosoftAuthService(HttpClient http, string? clientId = null)
    {
        _http = http;
        _clientId = string.IsNullOrWhiteSpace(clientId) ? DefaultClientId : clientId!;
    }

    public async Task<MsaDeviceCode> StartDeviceCodeAsync(CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = Scope,
            ["response_type"] = "device_code"
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://login.live.com/oauth20_connect.srf")
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new AuthException($"无法获取设备代码 ({(int)resp.StatusCode})", "device_code_failed");

        var node = JsonNode.Parse(body) ?? throw new AuthException("设备代码响应为空");
        return new MsaDeviceCode
        {
            UserCode = node["user_code"]?.GetValue<string>() ?? "",
            DeviceCode = node["device_code"]?.GetValue<string>() ?? "",
            VerificationUri = node["verification_uri"]?.GetValue<string>() ?? "https://microsoft.com/link",
            Message = node["message"]?.GetValue<string>() ?? "",
            ExpiresIn = node["expires_in"]?.GetValue<int>() ?? 900,
            Interval = Math.Max(2, node["interval"]?.GetValue<int>() ?? 5)
        };
    }

    /// <summary>Polls until the user finishes the browser login. Reports status text through the callback.</summary>
    public async Task<MinecraftAccount> PollForAccountAsync(MsaDeviceCode code, Action<string>? status = null, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(code.ExpiresIn);
        var interval = TimeSpan.FromSeconds(code.Interval);
        while (DateTimeOffset.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            status?.Invoke("等待浏览器中完成登录…");
            var token = await TryRequestTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["device_code"] = code.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }, ct).ConfigureAwait(false);

            if (token is not null)
            {
                status?.Invoke("已获得 Microsoft 令牌，正在验证 Xbox 账户…");
                return await ExchangeForMinecraftAsync(token.AccessToken, token.RefreshToken, ct).ConfigureAwait(false);
            }

            await Task.Delay(interval, ct).ConfigureAwait(false);
        }
        throw new AuthException("设备代码已过期，请重新登录", "device_code_expired");
    }

    public async Task<MinecraftAccount> RefreshAsync(MinecraftAccount account, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.RefreshToken))
            throw new AuthException("该账户没有可用的刷新令牌，请重新登录", "no_refresh_token");

        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["refresh_token"] = account.RefreshToken!,
            ["grant_type"] = "refresh_token",
            ["scope"] = Scope
        }, ct).ConfigureAwait(false);

        var refreshed = await ExchangeForMinecraftAsync(token.AccessToken, token.RefreshToken, ct).ConfigureAwait(false);
        refreshed.Id = account.Id;
        return refreshed;
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);

    private async Task<TokenResponse?> TryRequestTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://login.live.com/oauth20_token.srf")
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode)
        {
            var node = JsonNode.Parse(body)!;
            return new TokenResponse(
                node["access_token"]?.GetValue<string>() ?? "",
                node["refresh_token"]?.GetValue<string>() ?? "",
                node["expires_in"]?.GetValue<int>() ?? 3600);
        }

        var error = TryReadError(body);
        if (error is "authorization_pending" or "slow_down")
            return null;
        throw new AuthException($"Microsoft 登录失败: {error ?? resp.StatusCode.ToString()}", error);
    }

    private async Task<TokenResponse> RequestTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://login.live.com/oauth20_token.srf")
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var error = TryReadError(body);
            throw new AuthException(error == "invalid_grant"
                ? "登录已失效，请重新登录 Microsoft 账户"
                : $"刷新令牌失败: {error ?? resp.StatusCode.ToString()}", error);
        }
        var node = JsonNode.Parse(body)!;
        return new TokenResponse(
            node["access_token"]?.GetValue<string>() ?? "",
            node["refresh_token"]?.GetValue<string>() ?? "",
            node["expires_in"]?.GetValue<int>() ?? 3600);
    }

    private static string? TryReadError(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            return node?["error"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private async Task<MinecraftAccount> ExchangeForMinecraftAsync(string msaToken, string refreshToken, CancellationToken ct)
    {
        // 1) Xbox Live user token
        var xbl = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + msaToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, ct).ConfigureAwait(false);

        var xblToken = xbl["Token"]?.GetValue<string>()
                       ?? throw new AuthException("Xbox Live 未返回令牌", "xbl_no_token");
        var uhs = xbl["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>()
                  ?? throw new AuthException("Xbox Live 未返回用户哈希", "xbl_no_uhs");

        // 2) XSTS token
        JsonNode xsts;
        try
        {
            xsts = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
            {
                Properties = new
                {
                    SandboxId = "RETAIL",
                    UserTokens = new[] { xblToken }
                },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT"
            }, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AuthException(DescribeXstsError(ex), "xsts_failed");
        }

        var xstsToken = xsts["Token"]?.GetValue<string>()
                        ?? throw new AuthException("XSTS 未返回令牌", "xsts_no_token");
        var xuid = xsts["DisplayClaims"]?["xui"]?[0]?["xid"]?.GetValue<string>();

        // 3) Minecraft services token
        var mc = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new
        {
            identityToken = $"XBL3.0 x={uhs};{xstsToken}"
        }, ct).ConfigureAwait(false);

        var access = mc["access_token"]?.GetValue<string>()
                     ?? throw new AuthException("Minecraft 服务未返回访问令牌", "mc_no_token");
        var expiresIn = mc["expires_in"]?.GetValue<int>() ?? 86400;

        // 4) Profile (name + uuid + skin)
        using var profileReq = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        profileReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var profileResp = await _http.SendAsync(profileReq, ct).ConfigureAwait(false);
        if (profileResp.StatusCode == HttpStatusCode.NotFound)
            throw new AuthException("该 Microsoft 账户没有 Minecraft: Java Edition 的所有权", "no_minecraft_ownership");
        profileResp.EnsureSuccessStatusCode();
        var profileBody = await profileResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var profile = JsonNode.Parse(profileBody)!;

        var uuid = profile["id"]?.GetValue<string>() ?? "";
        var name = profile["name"]?.GetValue<string>() ?? "";
        var skin = profile["skins"]?[0]?["url"]?.GetValue<string>();

        return new MinecraftAccount
        {
            Id = MinecraftAccount.NewId(),
            Kind = AccountKind.Microsoft,
            Name = name,
            Uuid = FormatUuid(uuid),
            AccessToken = access,
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.Now.AddSeconds(expiresIn),
            Xuid = xuid,
            SkinUrl = skin,
            LastUsed = DateTimeOffset.Now
        };
    }

    private async Task<JsonNode> PostJsonAsync(string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Accept", "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode} from {new Uri(url).Host}: {body}",
                null, resp.StatusCode);
        }
        return JsonNode.Parse(body) ?? throw new AuthException("响应不是有效 JSON");
    }

    private static string DescribeXstsError(HttpRequestException ex)
    {
        // XSTS returns XErr codes for the most common account problems.
        var text = ex.Message;
        if (text.Contains("2148916233", StringComparison.Ordinal))
            return "该账户尚未创建 Xbox 档案，请先在 xbox.com 登录一次";
        if (text.Contains("2148916238", StringComparison.Ordinal))
            return "该账户为未成年账户，需要先加入家庭组";
        if (text.Contains("2148916227", StringComparison.Ordinal))
            return "该 Xbox 账户已被封禁";
        return "Xbox 身份验证失败：" + text;
    }

    public static string FormatUuid(string uuid)
    {
        var clean = uuid.Replace("-", "").ToLowerInvariant();
        if (clean.Length != 32) return uuid;
        return $"{clean[..8]}-{clean[8..12]}-{clean[12..16]}-{clean[16..20]}-{clean[20..]}";
    }
}

/// <summary>
/// Persists accounts encrypted with Windows DPAPI (current user). Nothing sensitive is ever written
/// in plaintext; the encrypted blob is bound to the current Windows profile.
/// </summary>
public sealed class AccountStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MonoLauncher.Accounts.v1");
    private readonly string _path;
    private readonly object _lock = new();
    private List<MinecraftAccount> _accounts = new();

    public AccountStore(AppPaths paths)
    {
        _path = Path.Combine(paths.ConfigDir, "accounts.bin");
        Load();
    }

    public string StoragePath => _path;

    public IReadOnlyList<MinecraftAccount> Accounts
    {
        get { lock (_lock) return _accounts.ToList(); }
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path)) { _accounts = new(); return; }
                var protectedBytes = File.ReadAllBytes(_path);
                var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                _accounts = JsonSerializer.Deserialize<List<MinecraftAccount>>(plain) ?? new();
            }
            catch
            {
                _accounts = new();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var plain = JsonSerializer.SerializeToUtf8Bytes(_accounts);
            var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            // Write to a temp file then replace, so a crash cannot corrupt the store.
            var tmp = _path + ".tmp";
            File.WriteAllBytes(tmp, protectedBytes);
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public void Upsert(MinecraftAccount account)
    {
        lock (_lock)
        {
            var existing = _accounts.FindIndex(a => a.Id == account.Id);
            if (existing >= 0) _accounts[existing] = account;
            else _accounts.Add(account);
        }
        Save();
    }

    public void Remove(string id)
    {
        lock (_lock) _accounts.RemoveAll(a => a.Id == id);
        Save();
    }

    public MinecraftAccount? DefaultAccount
    {
        get
        {
            lock (_lock)
                return _accounts.OrderByDescending(a => a.LastUsed).FirstOrDefault();
        }
    }

    public void MarkUsed(string id)
    {
        lock (_lock)
        {
            var acc = _accounts.FirstOrDefault(a => a.Id == id);
            if (acc is not null) acc.LastUsed = DateTimeOffset.Now;
        }
        Save();
    }
}
