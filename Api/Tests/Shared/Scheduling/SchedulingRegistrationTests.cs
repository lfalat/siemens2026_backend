using Kernel.Scheduling;
using Microsoft.Extensions.Hosting;

namespace Tests.Shared.Scheduling;

/// <summary>
/// Tests what <see cref="SchedulingRegistration"/> puts in the container: the worker scoped, so each run — and a test —
/// resolves its own, and exactly one hosted service of the right kind driving it. Also pins that a daily job does not
/// run at startup the way a periodic one does.
/// </summary>
public class SchedulingRegistrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Worker : IScheduledWorker
    {
        public static int Runs;

        public Task<int> RunAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref Runs);
            return Task.FromResult(0);
        }
    }

    [Fact]
    public void A_daily_worker_is_scoped_and_driven_by_one_daily_host()
    {
        ServiceCollection services = new();
        services.AddLogging().AddDailyWorker<Worker>(new TimeOnly(3, 0));

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, static d => d.ServiceType == typeof(Worker)).Lifetime);
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<DailyWorkerService<Worker>>(Assert.Single(provider.GetServices<IHostedService>()));
    }

    [Fact]
    public void A_periodic_worker_is_scoped_and_driven_by_one_periodic_host()
    {
        ServiceCollection services = new();
        services.AddLogging().AddPeriodicWorker<Worker>(TimeSpan.FromMinutes(5));

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, static d => d.ServiceType == typeof(Worker)).Lifetime);
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<PeriodicWorkerService<Worker>>(Assert.Single(provider.GetServices<IHostedService>()));
    }

    [Fact]
    public async Task A_daily_host_waits_for_its_time_and_stops_quietly_while_waiting()
    {
        // A run time just behind the clock is nearly 24 hours away, so nothing may run before the host stops.
        TimeOnly justPassed = TimeOnly.FromDateTime(DateTime.UtcNow.AddMinutes(-1));
        using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddDailyWorker<Worker>(justPassed)
            .BuildServiceProvider();
        IHostedService host = provider.GetRequiredService<IHostedService>();
        int before = Volatile.Read(ref Worker.Runs);

        await host.StartAsync(Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        await host.StopAsync(Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal(before, Volatile.Read(ref Worker.Runs));
    }
}
