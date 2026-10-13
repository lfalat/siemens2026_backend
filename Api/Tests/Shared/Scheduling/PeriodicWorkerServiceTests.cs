using System.Collections.Concurrent;
using Kernel.Scheduling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests.Shared.Scheduling;

/// <summary>
/// Tests the loop <see cref="PeriodicWorkerService{TWorker}"/> runs a worker in: once at startup and then every
/// interval, each run in a scope of its own, a failing run logged and survived rather than ending the job, and a
/// shutdown that cancels the run in progress without being reported as a failure.
/// </summary>
public class PeriodicWorkerServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>What the workers report back; a singleton, so it outlives the scope each run is resolved from.</summary>
    private sealed class Probe
    {
        private readonly ConcurrentQueue<object> _instances = new();

        public int Runs => _instances.Count;
        public IReadOnlyCollection<object> Instances => _instances;
        public TaskCompletionSource<CancellationToken> RunStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Ran(object worker) => _instances.Enqueue(worker);

        public async Task WaitForRunsAsync(int count)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (Runs < count)
                await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class CountingWorker(Probe probe) : IScheduledWorker
    {
        public Task<int> RunAsync(CancellationToken ct = default)
        {
            probe.Ran(this);
            return Task.FromResult(1);
        }
    }

    private sealed class FailingWorker(Probe probe) : IScheduledWorker
    {
        public Task<int> RunAsync(CancellationToken ct = default)
        {
            probe.Ran(this);
            throw new InvalidOperationException("the job failed");
        }
    }

    /// <summary>Blocks until cancelled, standing in for a long run that is still going when the host stops.</summary>
    private sealed class BlockingWorker(Probe probe) : IScheduledWorker
    {
        public async Task<int> RunAsync(CancellationToken ct = default)
        {
            probe.RunStarted.TrySetResult(ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(int Id, Exception? Exception)> Events { get; } = new();
        public ConcurrentQueue<object> Scopes { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(RecordingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner.Scopes.Enqueue(state);
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => owner.Events.Enqueue((eventId.Id, exception));
        }
    }

    private static (ServiceProvider Services, IHostedService Host, Probe Probe, RecordingLoggerProvider Logs)
        Build<TWorker>(TimeSpan interval) where TWorker : class, IScheduledWorker
    {
        RecordingLoggerProvider logs = new();
        ServiceProvider services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(logs))
            .AddSingleton<Probe>()
            .AddPeriodicWorker<TWorker>(interval)
            .BuildServiceProvider();
        return (services, services.GetRequiredService<IHostedService>(), services.GetRequiredService<Probe>(), logs);
    }

    [Fact]
    public async Task Runs_at_startup_without_waiting_a_whole_interval()
    {
        (ServiceProvider services, IHostedService host, Probe probe, _) = Build<CountingWorker>(TimeSpan.FromHours(1));
        await using ServiceProvider owned = services;

        await host.StartAsync(Ct);
        await probe.WaitForRunsAsync(1);
        await host.StopAsync(Ct);

        Assert.Equal(1, probe.Runs); // and not a second time: the next tick is an hour away
    }

    [Fact]
    public async Task Keeps_running_every_interval_with_a_fresh_worker_each_time()
    {
        (ServiceProvider services, IHostedService host, Probe probe, _) = Build<CountingWorker>(Interval);
        await using ServiceProvider owned = services;

        await host.StartAsync(Ct);
        await probe.WaitForRunsAsync(3);
        await host.StopAsync(Ct);

        // Scoped per run, so no run sees a context (or anything else) a previous one left behind.
        Assert.Equal(probe.Runs, probe.Instances.Distinct().Count());
    }

    [Fact]
    public async Task A_failing_run_is_logged_and_the_job_runs_again()
    {
        (ServiceProvider services, IHostedService host, Probe probe, RecordingLoggerProvider logs) =
            Build<FailingWorker>(Interval);
        await using ServiceProvider owned = services;

        await host.StartAsync(Ct);
        await probe.WaitForRunsAsync(2);
        await host.StopAsync(Ct);

        Assert.Contains(logs.Events, static e => e.Id == 1101 && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Each_run_is_scoped_by_the_worker_name()
    {
        // Outside a request there is no trace id; this scope is what attributes a run's records to the job.
        (ServiceProvider services, IHostedService host, Probe probe, RecordingLoggerProvider logs) =
            Build<CountingWorker>(TimeSpan.FromHours(1));
        await using ServiceProvider owned = services;

        await host.StartAsync(Ct);
        await probe.WaitForRunsAsync(1);
        await host.StopAsync(Ct);

        Assert.Contains(logs.Scopes, static s => s is IEnumerable<KeyValuePair<string, object?>> pairs
            && pairs.Contains(new KeyValuePair<string, object?>("Worker", nameof(CountingWorker))));
    }

    [Fact]
    public async Task Stopping_cancels_the_run_in_progress_without_reporting_a_failure()
    {
        (ServiceProvider services, IHostedService host, Probe probe, RecordingLoggerProvider logs) =
            Build<BlockingWorker>(Interval);
        await using ServiceProvider owned = services;

        await host.StartAsync(Ct);
        CancellationToken runToken = await probe.RunStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await host.StopAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.True(runToken.IsCancellationRequested);
        Assert.DoesNotContain(logs.Events, static e => e.Id == 1101);
    }
}
