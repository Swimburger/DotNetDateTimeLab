using System.Globalization;
using DotNetDateTimeLab.Core;

namespace DotNetDateTimeLab.Tests;

public sealed class DateTimeLabEngineTests
{
    [Fact]
    public void DetectsAmbiguousFallBackTimeAndBothInstants()
    {
        var result = Parse("2026-11-01 01:30", LabValueType.DateTime, "yyyy-MM-dd HH:mm", "America/New_York");

        Assert.True(result.Success);
        Assert.True(result.IsAmbiguousTime);
        Assert.False(result.IsInvalidTime);
        Assert.Equal(2, result.AmbiguousInstants.Count);
        Assert.Contains(result.AmbiguousInstants, item => item.Value == "2026-11-01T05:30:00.0000000Z");
        Assert.Contains(result.AmbiguousInstants, item => item.Value == "2026-11-01T06:30:00.0000000Z");
    }

    [Fact]
    public void DetectsInvalidSpringForwardTime()
    {
        var result = Parse("2026-03-08 02:30", LabValueType.DateTime, "yyyy-MM-dd HH:mm", "America/New_York");

        Assert.True(result.Success);
        Assert.True(result.IsInvalidTime);
        Assert.Contains(result.Conversions, item => item.Label == "UTC" && item.Value == "Not available");
    }

    [Fact]
    public void DateTimeOffsetPreservesAnInstantAndProducesUnixTime()
    {
        var result = DateTimeLabEngine.Parse(new(
            "2026-11-26T14:30:00-05:00", LabValueType.DateTimeOffset, "", null,
            DateTimeStyles.RoundtripKind, "America/New_York", "Europe/Brussels"));

        Assert.True(result.Success);
        Assert.Contains(result.Inspection, item => item.Label == "Offset" && item.Value == "-05:00");
        Assert.Contains(result.Conversions, item => item.Label == "UTC" && item.Value.StartsWith("2026-11-26T19:30:00", StringComparison.Ordinal));
        Assert.Contains("ToUnixTimeSeconds", result.GeneratedCode);
    }

    [Fact]
    public void UtcDateTimeDoesNotGetReinterpretedInTheSelectedSourceZone()
    {
        var result = DateTimeLabEngine.Parse(new(
            "2026-11-26T19:30:00Z", LabValueType.DateTime, "", null,
            DateTimeStyles.RoundtripKind, "America/New_York", "Europe/Brussels"));

        Assert.True(result.Success);
        Assert.Contains(result.Inspection, item => item.Label == "Kind" && item.Value == "Utc");
        Assert.Contains(result.Inspection, item => item.Label == "Source time zone" && item.Value == "Not used");
        Assert.Contains(result.Conversions, item => item.Label == "UTC" && item.Value.StartsWith("2026-11-26T19:30:00", StringComparison.Ordinal));
    }

    [Fact]
    public void DateOnlyMakesInstantConversionsExplicitlyUnavailable()
    {
        var result = DateTimeLabEngine.Parse(new(
            "1988-06-12", LabValueType.DateOnly, "", "yyyy-MM-dd",
            DateTimeStyles.None, "UTC", "UTC"));

        Assert.True(result.Success);
        Assert.Contains(result.Conversions, item => item.Value == "Not applicable");
        Assert.Contains("intentionally has no time", result.GeneratedCode);
    }

    [Fact]
    public void ReportsActionableParseFailure()
    {
        var result = Parse("not-a-date", LabValueType.DateTime, null, "UTC");

        Assert.False(result.Success);
        Assert.Contains("could not be parsed as DateTime", result.Error);
        Assert.Contains("en-US", result.Error);
    }

    private static ParseResult Parse(string input, LabValueType type, string? format, string sourceZone) =>
        DateTimeLabEngine.Parse(new(input, type, "en-US", format, DateTimeStyles.None, sourceZone, "UTC"));
}
