using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;

namespace Tests.Shared.Idempotency;

/// <summary>Exercises the proxy lifecycle independently of the shared PostgreSQL fault-test host.</summary>
[Collection(FaultInjection.Name)]
public class ToxiproxyPortPoolTests
{
    [Fact]
    public async Task Live_links_have_distinct_ports_and_disposing_twice_does_not_add_capacity()
    {
        await using INetwork network = new NetworkBuilder().Build();
        await network.CreateAsync(TestContext.Current.CancellationToken);
        await using ToxiproxyTestHost host = ToxiproxyTestHost.Start(network, "unused", 5432);
        List<ToxiproxyLink> links = [];
        try
        {
            for (int index = 0; index < 8; index++)
                links.Add(await host.CreateLink());
            Assert.Equal(8, links.Select(link => link.Port).Distinct().Count());
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.CreateLink());

            int releasedPort = links[0].Port;
            await links[0].DisposeAsync();
            await links[0].DisposeAsync();
            links[0] = await host.CreateLink();
            Assert.Equal(releasedPort, links[0].Port);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.CreateLink());
        }
        finally
        {
            foreach (ToxiproxyLink link in links)
                await link.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disposed_links_release_capacity_for_later_serial_tests()
    {
        // A separate host is essential: a failed regression must not exhaust other tests' shared pool.
        await using INetwork network = new NetworkBuilder().Build();
        await network.CreateAsync(TestContext.Current.CancellationToken);
        // Creating a proxy does not connect upstream; no database is needed for this lifecycle test.
        await using ToxiproxyTestHost host = ToxiproxyTestHost.Start(network, "unused", 5432);

        // More lifetimes than the eight listen ports, but never more than one live proxy.
        for (int iteration = 0; iteration < 16; iteration++)
        {
            await using ToxiproxyLink link = await host.CreateLink();
            await link.CutAsync();
            await link.RestoreAsync();
        }
    }
}
