using Foundation;

namespace CloudBox.Client.Platforms.iOS;

public class IOSMdnsDiscovery
{
    public record DiscoveredServer(string Name, string Host, int Port);

    public static async Task<List<DiscoveredServer>> DiscoverAsync(
        TimeSpan timeout, CancellationToken ct)
    {
        var results = new List<DiscoveredServer>();
        var discovered = new List<NSNetService>();
        var browser = new NSNetServiceBrowser();

        browser.FoundService += (s, e) => discovered.Add(e.Service);
        browser.SearchForServices("_cloudbox._tcp", "local.");

        try { await Task.Delay(timeout, ct); }
        catch (System.OperationCanceledException) { }

        browser.Stop();

        foreach (var svc in discovered)
        {
            svc.Resolve(5);
            if (!string.IsNullOrEmpty(svc.HostName))
                results.Add(new DiscoveredServer(svc.Name, svc.HostName, (int)svc.Port));
        }

        return results;
    }
}