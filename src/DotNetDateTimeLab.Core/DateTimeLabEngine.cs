using System.Globalization;

namespace DotNetDateTimeLab.Core;

public enum LabValueType
{
    DateTime,
    DateTimeOffset,
    DateOnly,
    TimeOnly
}

public sealed record ParseRequest(
    string Input,
    LabValueType ValueType,
    string CultureName,
    string? ExactFormat,
    DateTimeStyles Styles,
    string SourceTimeZoneId,
    string TargetTimeZoneId);

public sealed record InspectionItem(string Label, string Value, string? Note = null);
public sealed record ConversionItem(string Label, string Value, string? Note = null);

public sealed record ParseResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string SemanticKind { get; init; } = string.Empty;
    public IReadOnlyList<InspectionItem> Inspection { get; init; } = [];
    public IReadOnlyList<ConversionItem> Conversions { get; init; } = [];
    public bool IsInvalidTime { get; init; }
    public bool IsAmbiguousTime { get; init; }
    public IReadOnlyList<ConversionItem> AmbiguousInstants { get; init; } = [];
    public string GeneratedCode { get; init; } = string.Empty;
}

public static class DateTimeLabEngine
{
    public static ParseResult Parse(ParseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Input))
            return Failure("Enter a date or time value to inspect.", request);

        CultureInfo culture;
        TimeZoneInfo sourceZone;
        TimeZoneInfo targetZone;
        try
        {
            culture = string.IsNullOrEmpty(request.CultureName)
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(request.CultureName);
            sourceZone = FindZone(request.SourceTimeZoneId);
            targetZone = FindZone(request.TargetTimeZoneId);
        }
        catch (Exception exception) when (exception is CultureNotFoundException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return Failure(exception.Message, request);
        }

        try
        {
            return request.ValueType switch
            {
                LabValueType.DateTime => ParseDateTime(request, culture, sourceZone, targetZone),
                LabValueType.DateTimeOffset => ParseDateTimeOffset(request, culture, targetZone),
                LabValueType.DateOnly => ParseDateOnly(request, culture),
                LabValueType.TimeOnly => ParseTimeOnly(request, culture),
                _ => Failure("Unsupported value type.", request)
            };
        }
        catch (ArgumentException exception)
        {
            return Failure(exception.Message, request);
        }
    }

    private static ParseResult ParseDateTime(ParseRequest request, CultureInfo culture, TimeZoneInfo sourceZone, TimeZoneInfo targetZone)
    {
        var parsed = HasExactFormat(request)
            ? DateTime.TryParseExact(request.Input, request.ExactFormat!, culture, request.Styles, out var exactValue) ? exactValue : (DateTime?)null
            : DateTime.TryParse(request.Input, culture, request.Styles, out var flexibleValue) ? flexibleValue : null;

        if (parsed is null)
            return Failure(BuildParseError("DateTime", request, culture), request);

        var value = parsed.Value;
        var wallTime = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        var invalid = sourceZone.IsInvalidTime(wallTime);
        var ambiguous = sourceZone.IsAmbiguousTime(wallTime);
        var inspection = new List<InspectionItem>
        {
            new("Parsed value", value.ToString("O", CultureInfo.InvariantCulture)),
            new("Kind", value.Kind.ToString(), "Kind is metadata; it is not a full time-zone identifier."),
            new("Ticks", value.Ticks.ToString(CultureInfo.InvariantCulture)),
            new("Round-trip (O)", value.ToString("O", CultureInfo.InvariantCulture)),
            new("Culture used", DisplayCulture(culture)),
            new("Source time zone", sourceZone.Id)
        };

        var conversions = new List<ConversionItem>();
        var ambiguousInstants = new List<ConversionItem>();
        if (invalid)
        {
            conversions.Add(new("UTC", "Not available", "This wall-clock time does not exist in the selected source time zone."));
        }
        else if (ambiguous)
        {
            foreach (var offset in sourceZone.GetAmbiguousTimeOffsets(wallTime).OrderBy(offset => offset))
            {
                var instant = new DateTimeOffset(wallTime, offset);
                ambiguousInstants.Add(new(
                    $"Offset {FormatOffset(offset)}",
                    instant.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                    $"Unix {instant.ToUnixTimeSeconds()}"));
            }

            var assumedUtc = TimeZoneInfo.ConvertTimeToUtc(wallTime, sourceZone);
            AddInstantConversions(conversions, new DateTimeOffset(assumedUtc, TimeSpan.Zero), targetZone,
                "TimeZoneInfo.ConvertTimeToUtc chooses one offset for an ambiguous wall time; inspect both candidates below.");
        }
        else
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(wallTime, sourceZone);
            AddInstantConversions(conversions, new DateTimeOffset(utc, TimeSpan.Zero), targetZone);
        }

        return new ParseResult
        {
            Success = true,
            Summary = invalid
                ? "Parsed as DateTime, but the selected source-zone wall time is invalid."
                : ambiguous
                    ? "Parsed as DateTime; this wall time maps to two possible instants."
                    : "Parsed as DateTime and interpreted in the selected source time zone.",
            SemanticKind = "Wall-clock date and time. DateTime alone does not retain a full time-zone identity.",
            Inspection = inspection,
            Conversions = conversions,
            IsInvalidTime = invalid,
            IsAmbiguousTime = ambiguous,
            AmbiguousInstants = ambiguousInstants,
            GeneratedCode = GenerateDateTimeCode(request, culture, sourceZone, targetZone)
        };
    }

    private static ParseResult ParseDateTimeOffset(ParseRequest request, CultureInfo culture, TimeZoneInfo targetZone)
    {
        var parsed = HasExactFormat(request)
            ? DateTimeOffset.TryParseExact(request.Input, request.ExactFormat!, culture, request.Styles, out var exactValue) ? exactValue : (DateTimeOffset?)null
            : DateTimeOffset.TryParse(request.Input, culture, request.Styles, out var flexibleValue) ? flexibleValue : null;

        if (parsed is null)
            return Failure(BuildParseError("DateTimeOffset", request, culture), request);

        var value = parsed.Value;
        var inspection = new List<InspectionItem>
        {
            new("Parsed value", value.ToString("O", CultureInfo.InvariantCulture)),
            new("Offset", FormatOffset(value.Offset), "An offset identifies a UTC difference, not a geographic time zone."),
            new("Ticks", value.Ticks.ToString(CultureInfo.InvariantCulture)),
            new("UTC ticks", value.UtcTicks.ToString(CultureInfo.InvariantCulture)),
            new("Unix seconds", value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new("Unix milliseconds", value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
            new("Round-trip (O)", value.ToString("O", CultureInfo.InvariantCulture)),
            new("Culture used", DisplayCulture(culture))
        };
        var conversions = new List<ConversionItem>();
        AddInstantConversions(conversions, value, targetZone);

        return new ParseResult
        {
            Success = true,
            Summary = "Parsed as DateTimeOffset: one unambiguous instant with its original offset.",
            SemanticKind = "Instant plus UTC offset. The offset does not identify a time zone or its daylight-saving rules.",
            Inspection = inspection,
            Conversions = conversions,
            GeneratedCode = GenerateOffsetCode(request, culture, targetZone)
        };
    }

    private static ParseResult ParseDateOnly(ParseRequest request, CultureInfo culture)
    {
        var style = request.Styles & DateTimeStyles.AllowWhiteSpaces;
        var parsed = HasExactFormat(request)
            ? DateOnly.TryParseExact(request.Input, request.ExactFormat!, culture, style, out var exactValue) ? exactValue : (DateOnly?)null
            : DateOnly.TryParse(request.Input, culture, style, out var flexibleValue) ? flexibleValue : null;
        if (parsed is null)
            return Failure(BuildParseError("DateOnly", request, culture), request);

        var value = parsed.Value;
        return new ParseResult
        {
            Success = true,
            Summary = "Parsed as DateOnly. No time, offset, time zone, or instant is implied.",
            SemanticKind = "Calendar date only—appropriate for birthdays, anniversaries, and business dates.",
            Inspection =
            [
                new("Parsed value", value.ToString("O", CultureInfo.InvariantCulture)),
                new("Day number", value.DayNumber.ToString(CultureInfo.InvariantCulture), "Number of days since 0001-01-01 in the proleptic Gregorian calendar."),
                new("Day of week", value.DayOfWeek.ToString()),
                new("Round-trip (O)", value.ToString("O", CultureInfo.InvariantCulture)),
                new("Culture used", DisplayCulture(culture))
            ],
            Conversions = [new("UTC / Unix time", "Not applicable", "Pair this date with a time and time zone before converting it to an instant.")],
            GeneratedCode = GenerateDateOnlyCode(request, culture)
        };
    }

    private static ParseResult ParseTimeOnly(ParseRequest request, CultureInfo culture)
    {
        var style = request.Styles & DateTimeStyles.AllowWhiteSpaces;
        var parsed = HasExactFormat(request)
            ? TimeOnly.TryParseExact(request.Input, request.ExactFormat!, culture, style, out var exactValue) ? exactValue : (TimeOnly?)null
            : TimeOnly.TryParse(request.Input, culture, style, out var flexibleValue) ? flexibleValue : null;
        if (parsed is null)
            return Failure(BuildParseError("TimeOnly", request, culture), request);

        var value = parsed.Value;
        return new ParseResult
        {
            Success = true,
            Summary = "Parsed as TimeOnly. No date, offset, time zone, or instant is implied.",
            SemanticKind = "Time of day only—appropriate for recurring opening times and schedules.",
            Inspection =
            [
                new("Parsed value", value.ToString("O", CultureInfo.InvariantCulture)),
                new("Ticks since midnight", value.Ticks.ToString(CultureInfo.InvariantCulture)),
                new("Round-trip (O)", value.ToString("O", CultureInfo.InvariantCulture)),
                new("Culture used", DisplayCulture(culture))
            ],
            Conversions = [new("UTC / Unix time", "Not applicable", "Pair this time with a date and time zone before converting it to an instant.")],
            GeneratedCode = GenerateTimeOnlyCode(request, culture)
        };
    }

    private static void AddInstantConversions(List<ConversionItem> conversions, DateTimeOffset value, TimeZoneInfo targetZone, string? utcNote = null)
    {
        var utc = value.ToUniversalTime();
        var local = value.ToLocalTime();
        var target = TimeZoneInfo.ConvertTime(value, targetZone);
        conversions.Add(new("UTC", utc.ToString("O", CultureInfo.InvariantCulture), utcNote));
        conversions.Add(new($"Local ({TimeZoneInfo.Local.Id})", local.ToString("O", CultureInfo.InvariantCulture)));
        conversions.Add(new($"Selected zone ({targetZone.Id})", target.ToString("O", CultureInfo.InvariantCulture)));
        conversions.Add(new("Unix seconds", value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        conversions.Add(new("Unix milliseconds", value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
    }

    private static bool HasExactFormat(ParseRequest request) => !string.IsNullOrWhiteSpace(request.ExactFormat);
    private static string DisplayCulture(CultureInfo culture) => culture.Equals(CultureInfo.InvariantCulture) ? "Invariant culture" : $"{culture.Name} — {culture.DisplayName}";
    private static string FormatOffset(TimeSpan offset) => $"{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
    private static TimeZoneInfo FindZone(string id) => id == "__local" ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(id);

    private static string BuildParseError(string typeName, ParseRequest request, CultureInfo culture)
    {
        var format = HasExactFormat(request) ? $" with exact format “{request.ExactFormat}”" : string.Empty;
        return $"“{request.Input}” could not be parsed as {typeName}{format} using {DisplayCulture(culture)} and {FormatStyles(request.Styles)}.";
    }

    private static ParseResult Failure(string error, ParseRequest request) => new()
    {
        Error = error,
        GeneratedCode = $"// Fix the input or options before code can be generated.\n// Input: {EscapeComment(request.Input)}"
    };

    private static string GenerateDateTimeCode(ParseRequest request, CultureInfo culture, TimeZoneInfo source, TimeZoneInfo target) => $$"""
        using System.Globalization;

        var culture = {{CultureExpression(culture)}};
        var styles = {{StylesExpression(request.Styles)}};
        var value = {{ParseExpression("DateTime", request)}};

        // Treat the parsed value as a wall time in the selected source zone.
        var sourceZone = TimeZoneInfo.FindSystemTimeZoneById("{{EscapeString(source.Id)}}");
        var wallTime = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

        if (sourceZone.IsInvalidTime(wallTime))
            throw new ArgumentException("The wall time does not exist in this time zone.");

        if (sourceZone.IsAmbiguousTime(wallTime))
        {
            foreach (var offset in sourceZone.GetAmbiguousTimeOffsets(wallTime))
                Console.WriteLine(new DateTimeOffset(wallTime, offset).UtcDateTime.ToString("O"));
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(wallTime, sourceZone);
        var targetZone = TimeZoneInfo.FindSystemTimeZoneById("{{EscapeString(target.Id)}}");
        var converted = TimeZoneInfo.ConvertTimeFromUtc(utc, targetZone);
        """;

    private static string GenerateOffsetCode(ParseRequest request, CultureInfo culture, TimeZoneInfo target) => $$"""
        using System.Globalization;

        var culture = {{CultureExpression(culture)}};
        var styles = {{StylesExpression(request.Styles)}};
        var value = {{ParseExpression("DateTimeOffset", request)}};

        var utc = value.ToUniversalTime();
        var unixSeconds = value.ToUnixTimeSeconds();
        var targetZone = TimeZoneInfo.FindSystemTimeZoneById("{{EscapeString(target.Id)}}");
        var converted = TimeZoneInfo.ConvertTime(value, targetZone);
        """;

    private static string GenerateDateOnlyCode(ParseRequest request, CultureInfo culture) => $$"""
        using System.Globalization;

        var culture = {{CultureExpression(culture)}};
        var styles = {{StylesExpression(request.Styles & DateTimeStyles.AllowWhiteSpaces)}};
        var value = {{ParseExpression("DateOnly", request)}};

        // DateOnly intentionally has no time, offset, or time zone.
        Console.WriteLine(value.ToString("O", CultureInfo.InvariantCulture));
        """;

    private static string GenerateTimeOnlyCode(ParseRequest request, CultureInfo culture) => $$"""
        using System.Globalization;

        var culture = {{CultureExpression(culture)}};
        var styles = {{StylesExpression(request.Styles & DateTimeStyles.AllowWhiteSpaces)}};
        var value = {{ParseExpression("TimeOnly", request)}};

        // TimeOnly intentionally has no date, offset, or time zone.
        Console.WriteLine(value.ToString("O", CultureInfo.InvariantCulture));
        """;

    private static string ParseExpression(string typeName, ParseRequest request) => HasExactFormat(request)
        ? $"{typeName}.ParseExact(\"{EscapeString(request.Input)}\", \"{EscapeString(request.ExactFormat!)}\", culture, styles)"
        : $"{typeName}.Parse(\"{EscapeString(request.Input)}\", culture, styles)";

    private static string CultureExpression(CultureInfo culture) => culture.Equals(CultureInfo.InvariantCulture)
        ? "CultureInfo.InvariantCulture"
        : $"CultureInfo.GetCultureInfo(\"{EscapeString(culture.Name)}\")";

    private static string StylesExpression(DateTimeStyles styles)
    {
        if (styles == DateTimeStyles.None) return "DateTimeStyles.None";
        var flags = Enum.GetValues<DateTimeStyles>()
            .Where(flag => flag != DateTimeStyles.None && IsSingleFlag(flag) && styles.HasFlag(flag))
            .Select(flag => $"DateTimeStyles.{flag}");
        return string.Join(" | ", flags);
    }

    private static string FormatStyles(DateTimeStyles styles) => StylesExpression(styles).Replace("DateTimeStyles.", string.Empty, StringComparison.Ordinal);
    private static bool IsSingleFlag(DateTimeStyles value)
    {
        var number = (int)value;
        return number > 0 && (number & (number - 1)) == 0;
    }

    private static string EscapeString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
    private static string EscapeComment(string value) => value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
