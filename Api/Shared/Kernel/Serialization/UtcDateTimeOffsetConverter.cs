using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kernel.Serialization;

/// <summary>
/// Reads any RFC 3339 timestamp and hands the rest of the system the same instant expressed in UTC.
/// </summary>
/// <remarks>
/// <para>
/// Timestamp columns are <c>TIMESTAMPTZ</c> — a point in time, with no offset stored — and Npgsql refuses to bind
/// a <see cref="DateTimeOffset"/> whose <see cref="DateTimeOffset.Offset"/> is not zero to one, throwing rather
/// than converting. But an offset is a legal, ordinary part of an RFC 3339 timestamp:
/// <c>2026-08-26T10:00:00+02:00</c> is exactly what a browser in Slovakia or Czechia sends. Without this
/// converter every such value would reach the query or the insert as-is and come back a 500.
/// </para>
/// <para>
/// Normalising on the way in loses nothing: the offset was never going to be stored, and
/// <see cref="DateTimeOffset"/> compares and orders by the instant either way. Writing is left to the default
/// converter — values leaving this service were read from a <c>TIMESTAMPTZ</c> and are already UTC.
/// </para>
/// </remarks>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    /// <summary>Reads a timestamp and converts it to UTC.</summary>
    /// <param name="reader">The reader positioned on the value.</param>
    /// <param name="typeToConvert">The requested type.</param>
    /// <param name="options">The serializer options in force.</param>
    /// <returns>The same instant, with a zero offset.</returns>
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    /// <summary>Writes the value the way the default converter would.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in force.</param>
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
