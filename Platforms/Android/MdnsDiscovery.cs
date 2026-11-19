using System.Collections.Concurrent;
using Makaretu.Dns;
using Android.Content;
using Android.Net.Wifi;

namespace CloudBox.Client.Platforms.Android;

public class MdnsDiscovery
{
    public record DiscoveredServer(string Name, string Host, int Port, string? Device);

    public static async Task<List<DiscoveredServer>> DiscoverAsync(
        TimeSpan timeout, CancellationToken ct)
    {
        var results = new ConcurrentDictionary<string, DiscoveredServer>();
        using var mdns = new MulticastService();
        using var sd = new ServiceDiscovery(mdns);

        WifiManager.MulticastLock? multicastLock = null;
        try
        {
            var ctx = global::Android.App.Application.Context;
            var wifi = (WifiManager?)ctx.GetSystemService(Context.WifiService);
            multicastLock = wifi?.CreateMulticastLock("CloudBoxMdnsLock");
            multicastLock?.SetReferenceCounted(false);
            multicastLock?.Acquire();
            Log($"MulticastLock acquired: {multicastLock?.IsHeld}");
        }
        catch (Exception ex)
        {
            Log($"MulticastLock failed: {ex.Message}");
        }

        try
        {
            sd.ServiceInstanceDiscovered += (s, e) =>
            {
                var instanceName = e.ServiceInstanceName.ToString();
                Log($"instance discovered: {instanceName}");

                if (!instanceName.Contains("_cloudbox._tcp"))
                    return;

                var name = instanceName.Split('.')[0];

                // 标准 mDNS 响应：PTR 在 Answers，SRV/TXT/A 在 AdditionalRecords。
                // 先查 Answers，再查 AdditionalRecords，覆盖两种情况。
                var srv = e.Message.Answers.OfType<SRVRecord>().FirstOrDefault()
                       ?? e.Message.AdditionalRecords.OfType<SRVRecord>().FirstOrDefault();

                if (srv == null)
                {
                    Log($"SRV not in message for {instanceName}");
                    return;
                }

                Log($"SRV found: target={srv.Target}, port={srv.Port}");

                // A 记录（IPv4）优先级：Answers → AdditionalRecords
                var a = e.Message.Answers.OfType<ARecord>()
                            .FirstOrDefault(r => r.Name == srv.Target)
                     ?? e.Message.AdditionalRecords.OfType<ARecord>()
                            .FirstOrDefault(r => r.Name == srv.Target);

                string host;
                if (a != null)
                {
                    host = a.Address.ToString();
                    Log($"A found: {srv.Target} -> {host}");
                }
                else
                {
                    // 没拿到 A 记录，退回 hostname。此时 App 里访问 http://cloudbox.local:5080
                    // 大概率解析不了，但至少把结果暴露出来方便排查。
                    host = srv.Target.ToString();
                    Log($"A not found for {srv.Target}, using hostname");
                }

                results.TryAdd(instanceName,
                    new DiscoveredServer(name, host, srv.Port, null));
                Log($"resolved: {name} -> {host}:{srv.Port}");
            };

            mdns.Start();
            Log("mdns started");
            sd.QueryServiceInstances("_cloudbox._tcp");
            Log("PTR query sent");

            await Task.Delay(timeout, ct).ConfigureAwait(false);
        }
        catch (System.OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"mdns error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { mdns.Stop(); } catch { }
            try
            {
                multicastLock?.Release();
                multicastLock?.Dispose();
                Log("MulticastLock released");
            }
            catch { }
        }

        return results.Values.ToList();
    }

    private static void Log(string msg) =>
        global::Android.Util.Log.Debug("CloudBox", msg);
}