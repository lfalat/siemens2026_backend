using Kernel.Scheduling;

namespace Tests.Shared.Scheduling;

/// <summary>
/// Tests the daily-run scheduling arithmetic shared by every <see cref="DailyWorkerService{TWorker}"/>, so a
/// worker's own tests need not re-test when it runs.
/// </summary>
public class DailyWorkerServiceTests
{
    [Fact]
    public void Schedules_later_today_when_before_the_run_time()
    {
        var now = new DateTime(2026, 7, 14, 0, 30, 0, DateTimeKind.Utc); // 00:30 UTC
        Assert.Equal(TimeSpan.FromMinutes(90), DailyWorkerSchedule.DelayUntilNextRun(now, new TimeOnly(2, 0)));
    }

    [Fact]
    public void Schedules_tomorrow_when_past_the_run_time()
    {
        var now = new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc); // 09:00 UTC, past 02:00
        Assert.Equal(TimeSpan.FromHours(17), DailyWorkerSchedule.DelayUntilNextRun(now, new TimeOnly(2, 0)));
    }

    [Fact]
    public void Honors_the_configured_run_time_so_staggered_jobs_differ()
    {
        var now = new DateTime(2026, 7, 14, 1, 30, 0, DateTimeKind.Utc); // 01:30 UTC

        // A 02:00 job is 30 minutes out; a 03:00 job (the staggered sibling) is 90 minutes out.
        Assert.Equal(TimeSpan.FromMinutes(30), DailyWorkerSchedule.DelayUntilNextRun(now, new TimeOnly(2, 0)));
        Assert.Equal(TimeSpan.FromMinutes(90), DailyWorkerSchedule.DelayUntilNextRun(now, new TimeOnly(3, 0)));
    }

    [Fact]
    public void Schedules_a_full_day_out_when_exactly_at_the_run_time()
    {
        var now = new DateTime(2026, 7, 14, 2, 0, 0, DateTimeKind.Utc); // exactly 02:00 UTC
        Assert.Equal(TimeSpan.FromHours(24), DailyWorkerSchedule.DelayUntilNextRun(now, new TimeOnly(2, 0)));
    }
}
