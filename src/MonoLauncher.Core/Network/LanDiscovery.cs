using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace MonoLauncher.Core.Network;

public sealed record LanServer(string Address, int Port, string Motd, DateTimeOffset SeenAt)
{
    public string Endpoint => $"{Address}:{Port}";
    public string Display => string.IsNullOrWhiteSpace(Motd) ? Endpoint : $"{Motd}  ({Endpoint})";
}

/// <summary>
/// Listens for the vanilla LAN broadcast on 224.0.2.60:4445 ("[MOTD]...[/MOTD][AD]port[/AD]").
/// Read-only: it joins the multicast group and never sends probes, exactly like the vanilla client.
/// </summary>
public sealed class LanDiscovery
{
    private static readonly IPAddress Group = IPAddress.Parse("224.0.2.60");
    private const int Port = 4445;

    public async Task<IReadOnlyList<LanServer>> DiscoverAsync(TimeSpan duration, CancellationToken ct = default)
    {
        var found = new Dictionary<string, LanServer>(StringComparer.OrdinalIgnoreCase);
        using var client = new UdpClient();
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try
        {
            client.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            client.JoinMulticastGroup(Group);
        }
        catch
        {
            // Some adapters refuse multicast; fall back to an ephemeral port (only works if the
            // broadcast is also sent to that port, which is rare). Return empty rather than throw.
            return Array.Empty<LanServer>();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(duration);
        var buffer = new byte[2048];
        try
        {
            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(result.Buffer);
                var motd = Regex.Match(text, @"\[MOTD\](.*?)\[/MOTD\]", RegexOptions.Singleline).Groups[1].Value;
                var ad = Regex.Match(text, @"\[AD\](.*?)\[/AD\]", RegexOptions.Singleline).Groups[1].Value;
                if (!int.TryParse(ad.Trim(), out var port)) continue;
                var address = result.RemoteEndPoint.Address.ToString();
                var server = new LanServer(address, port, motd.Trim(), DateTimeOffset.Now);
                found[server.Endpoint] = server;
            }
        }
        finally
        {
            try { client.DropMulticastGroup(Group); } catch { }
        }

        return found.Values.OrderBy(s => s.Motd).ToList();
    }
}
