using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Tests.Shared;

/// <summary>
/// A Toxiproxy container sharing the Postgres container's network, so a test can put a controllable TCP
/// proxy in front of the database and then break it on purpose — cut the connection mid-operation, or slow
/// it past a timeout.
/// </summary>
/// <remarks>
/// Started lazily by <see cref="PostgresTestHost"/>: the ordinary suite connects to Postgres directly and
/// should not pay for a container it never uses. Talks to Toxiproxy's HTTP API with a hand-rolled client
/// rather than a package — the API is four endpoints and stable, and the dependency would buy nothing.
/// </remarks>
internal sealed class ToxiproxyTestHost : IAsyncDisposable
{
    private const string Image = "ghcr.io/shopify/toxiproxy:2.12.0";
    private const int ControlPort = 8474;

    /// <summary>
    /// Listen ports exposed for proxies. Each <see cref="CreateLink"/> rents one until disposal, so the pool
    /// caps how many fault tests can hold a proxy at once — raise it if that ever binds.
    /// </summary>
    private static readonly int[] ProxyPorts = [8666, 8667, 8668, 8669, 8670, 8671, 8672, 8673];

    private readonly IContainer _container;
    private readonly HttpClient _control;
    private readonly string _upstream;
    private readonly ConcurrentQueue<int> _availablePorts = new(ProxyPorts);

    private ToxiproxyTestHost(IContainer container, HttpClient control, string upstream)
    {
        _container = container;
        _control = control;
        _upstream = upstream;
    }

    /// <summary>Starts the container on <paramref name="network"/>, proxying to <paramref name="upstreamAlias"/>.</summary>
    /// <param name="network">The network the upstream container is on.</param>
    /// <param name="upstreamAlias">The upstream's network alias.</param>
    /// <param name="upstreamPort">The upstream's port inside the network.</param>
    public static ToxiproxyTestHost Start(INetwork network, string upstreamAlias, int upstreamPort)
    {
        ContainerBuilder builder = new ContainerBuilder(Image)
            .WithNetwork(network)
            // The image binds its API to localhost by default, which is unreachable from outside the
            // container; without this the wait strategy below never succeeds.
            .WithCommand("-host", "0.0.0.0")
            .WithPortBinding(ControlPort, assignRandomHostPort: true)
            // The image is distroless, so the usual port probe — which execs a shell inside the container
            // — can never succeed. Ask the control API from the host instead.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(ControlPort).ForPath("/version")));

        foreach (int port in ProxyPorts)
            builder = builder.WithPortBinding(port, assignRandomHostPort: true);

        IContainer container = builder.Build();
        container.StartAsync().GetAwaiter().GetResult();

        HttpClient control = new()
        {
            BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(ControlPort)}"),
        };
        return new ToxiproxyTestHost(container, control, $"{upstreamAlias}:{upstreamPort}");
    }

    /// <summary>Creates a proxy of its own for one test and returns the host endpoint to connect through.</summary>
    public async Task<ToxiproxyLink> CreateLink()
    {
        if (!_availablePorts.TryDequeue(out int listenPort))
            throw new InvalidOperationException(
                $"Out of Toxiproxy listen ports ({ProxyPorts.Length}). Fault tests hold a proxy for their " +
                "lifetime; add more ports or run fewer at once.");

        string name = $"hh-{listenPort}-{Guid.NewGuid():N}";

        try
        {
            using HttpResponseMessage created = await _control.PostAsJsonAsync("/proxies", new ProxyDefinition(
                Name: name, Listen: $"0.0.0.0:{listenPort}", Upstream: _upstream, Enabled: true));
            created.EnsureSuccessStatusCode();

            return new ToxiproxyLink(_control, name, _container.Hostname, _container.GetMappedPublicPort(listenPort),
                () => _availablePorts.Enqueue(listenPort));
        }
        catch
        {
            // Creation may have succeeded even if its response was lost. Only reissue the port once
            // the control API confirms that no proxy still owns it.
            using HttpResponseMessage removed = await _control.DeleteAsync($"/proxies/{name}");
            if (removed.IsSuccessStatusCode || removed.StatusCode == HttpStatusCode.NotFound)
                _availablePorts.Enqueue(listenPort);
            throw;
        }
    }

    private sealed record ProxyDefinition(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("listen")] string Listen,
        [property: JsonPropertyName("upstream")] string Upstream,
        [property: JsonPropertyName("enabled")] bool Enabled);

    /// <summary>Disposes a dedicated test host without waiting for the process-wide container reaper.</summary>
    public async ValueTask DisposeAsync()
    {
        _control.Dispose();
        await _container.DisposeAsync();
    }
}

/// <summary>
/// One test's proxy: the endpoint to connect through, plus the controls to break the link underneath a
/// connection that is already open.
/// </summary>
/// <param name="control">HTTP client bound to the Toxiproxy control API.</param>
/// <param name="name">The proxy's name.</param>
/// <param name="host">Host to connect to.</param>
/// <param name="port">Port to connect to.</param>
/// <param name="releasePort">Returns the listen port to its host after confirmed deletion.</param>
internal sealed class ToxiproxyLink(HttpClient control, string name, string host, int port, Action releasePort) : IAsyncDisposable
{
    private int _disposed;
    /// <summary>Host to point the client at.</summary>
    public string Host => host;

    /// <summary>Port to point the client at.</summary>
    public int Port => port;

    /// <summary>
    /// Severs the link: open connections are closed and new ones refused, as if the database had gone
    /// away mid-conversation.
    /// </summary>
    public async Task CutAsync()
    {
        using HttpResponseMessage response = await control.PostAsJsonAsync(
            $"/proxies/{name}", new { enabled = false });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Restores the link.</summary>
    public async Task RestoreAsync()
    {
        using HttpResponseMessage response = await control.PostAsJsonAsync(
            $"/proxies/{name}", new { enabled = true });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Adds a fixed delay downstream, for testing behaviour under a slow link.</summary>
    /// <param name="delay">The latency to add.</param>
    public async Task SlowAsync(TimeSpan delay)
    {
        using HttpResponseMessage response = await control.PostAsJsonAsync($"/proxies/{name}/toxics", new
        {
            name = "latency",
            type = "latency",
            stream = "downstream",
            attributes = new { latency = (int)delay.TotalMilliseconds, jitter = 0 },
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Removes the proxy, so its port returns to the pool's owner and no toxic outlives the test.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            using HttpResponseMessage response = await control.DeleteAsync($"/proxies/{name}");
            if (response.StatusCode != HttpStatusCode.NotFound)
                response.EnsureSuccessStatusCode();
            releasePort();
        }
        catch (HttpRequestException)
        {
            // The host is being torn down; nothing to clean up.
        }
    }
}
