using System.Globalization;
using System.Text.Json;
using Kernel.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Tests.Shared.Serialization;

/// <summary>Carries an optional date range, the way a search filter does.</summary>
public sealed record TimestampFilterProbe(DateTimeOffset? From, DateTimeOffset? To);

/// <summary>
/// Timestamp columns are <c>TIMESTAMPTZ</c>, and Npgsql binds one only from a zero-offset
/// <see cref="DateTimeOffset"/> — it throws on anything else rather than converting. An offset is a legal, ordinary
/// part of an RFC 3339 timestamp though, so without normalising at the boundary a request as unremarkable as
/// <c>"from": "2026-08-26T10:00:00+02:00"</c> from a browser in Slovakia would come back a 500.
/// </summary>
public class UtcDateTimeOffsetConverterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new UtcDateTimeOffsetConverter() },
    };

    [Theory]
    [InlineData("2026-08-26T10:00:00+02:00", "2026-08-26T08:00:00Z")] // Slovakia and Czechia in summer
    [InlineData("2026-12-01T10:00:00+01:00", "2026-12-01T09:00:00Z")] // and in winter
    [InlineData("2026-08-26T10:00:00-05:00", "2026-08-26T15:00:00Z")]
    [InlineData("0139-11-14T12:08:36-05:57", "0139-11-14T18:05:36Z")] // the kind of value a contract sweep generates
    [InlineData("2026-08-26T08:00:00Z", "2026-08-26T08:00:00Z")]      // already UTC — unchanged
    public void An_offset_timestamp_is_read_as_the_same_instant_in_utc(string input, string expected)
    {
        DateTimeOffset parsed = JsonSerializer.Deserialize<DateTimeOffset>($"\"{input}\"", Options);

        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), parsed);
    }

    [Fact]
    public void A_nullable_property_goes_through_the_same_converter()
    {
        // System.Text.Json unwraps DateTimeOffset? onto the registered DateTimeOffset converter. Filters are
        // usually optional, so if that did not hold the fix would cover none of them.
        TimestampFilterProbe? filter = JsonSerializer.Deserialize<TimestampFilterProbe>(
            """{"from":"2026-08-26T10:00:00+02:00","to":null}""", Options);

        Assert.Equal(TimeSpan.Zero, filter!.From!.Value.Offset);
        Assert.Null(filter.To);
    }

    [Fact]
    public void Writing_is_left_to_the_default_format()
    {
        string json = JsonSerializer.Serialize(
            new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero), Options);

        Assert.Equal("\"2026-08-26T08:00:00+00:00\"", json);
    }

    [Fact]
    public void A_malformed_timestamp_is_still_a_deserialization_failure_not_a_crash()
    {
        // Which the host turns into a 400 — the converter must not swallow it into some other shape.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffset>("\"not-a-date\"", Options));
    }

    [Fact]
    public async Task A_normalised_value_is_one_Npgsql_will_bind_to_timestamptz()
    {
        // The other half of the story, against a real PostgreSQL: the reason the offset has to go is that the
        // provider rejects it.
        DateTimeOffset raw = DateTimeOffset.Parse("2026-08-26T10:00:00+02:00", CultureInfo.InvariantCulture);

        await Assert.ThrowsAsync<ArgumentException>(() => BindAsTimestamptzAsync(raw));

        DateTimeOffset normalised = JsonSerializer.Deserialize<DateTimeOffset>("\"2026-08-26T10:00:00+02:00\"", Options);
        DateTimeOffset stored = await BindAsTimestamptzAsync(normalised);

        Assert.Equal(raw, stored); // the same instant survives the round trip
    }

    private static async Task<DateTimeOffset> BindAsTimestamptzAsync(DateTimeOffset value)
    {
        await using NpgsqlCommand command = PostgresTestHost.Current.DataSource.CreateCommand("SELECT @at");
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("at", NpgsqlDbType.TimestampTz) { TypedValue = value });

        // ExecuteScalar would hand back timestamptz as a UTC DateTime; ask for the type the API works with.
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct);
        await reader.ReadAsync(Ct);
        return await reader.GetFieldValueAsync<DateTimeOffset>(0, Ct);
    }
}
