namespace DiskSpaceInspector.Core.Models;

public enum VisualChartKind
{
    KpiStrip,
    RankBar,
    StackedBar,
    Curve,
    Timeline,
    Heatmap,
    Scatter,
    Radar,
    Matrix,
    RelationshipFlow,
    BubblePack,
    CalendarHeatmap,
    Funnel,
    Waterfall,
    Donut
}

public sealed class StorageAnalyticsSnapshot
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<ChartDefinition> Charts { get; init; } = [];

    public IReadOnlyList<TutorialStep> Tutorials { get; init; } = [];

    public string Summary { get; init; } = string.Empty;
}

public sealed class ChartDefinition
{
    public string Key { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Group { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Insight { get; init; } = string.Empty;

    public VisualChartKind Kind { get; init; }

    public bool IsAdvanced { get; init; }

    public IReadOnlyList<ChartMetric> Metrics { get; init; } = [];

    public IReadOnlyList<ChartSeries> Series { get; init; } = [];

    public IReadOnlyList<ChartPoint> Points { get; init; } = [];

    public IReadOnlyList<HeatmapCell> Cells { get; init; } = [];

    public IReadOnlyList<RelationshipFlow> Flows { get; init; } = [];
}

public sealed class ChartMetric
{
    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string ColorKey { get; init; } = "Other";
}

public sealed class ChartSeries
{
    public string Name { get; init; } = string.Empty;

    public string ColorKey { get; init; } = "Other";

    public IReadOnlyList<ChartPoint> Points { get; init; } = [];
}

public sealed class ChartPoint
{
    public string Label { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Score { get; init; }

    public double Fraction { get; init; }

    public long SizeBytes { get; init; }

    public int Count { get; init; }

    public string Category { get; init; } = string.Empty;

    public string ColorKey { get; init; } = "Other";

    public string Path { get; init; } = string.Empty;

    public string DisplayValue { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public DateTimeOffset? Timestamp { get; init; }

    public long? NodeId { get; init; }
}

public sealed class HeatmapCell
{
    public string Row { get; init; } = string.Empty;

    public string Column { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public double Value { get; init; }

    public long SizeBytes { get; init; }

    public int Count { get; init; }

    public string ColorKey { get; init; } = "Other";

    public string Detail { get; init; } = string.Empty;
}

public sealed class RelationshipFlow
{
    public string Source { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public double Value { get; init; }

    public long SizeBytes { get; init; }

    public double Confidence { get; init; }

    public string ColorKey { get; init; } = "Other";

    public string Detail { get; init; } = string.Empty;

    public long? NodeId { get; init; }
}

public sealed class TutorialStep
{
    public string Key { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Goal { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string SafetyNote { get; init; } = string.Empty;
}
