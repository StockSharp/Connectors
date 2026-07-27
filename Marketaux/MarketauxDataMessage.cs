namespace StockSharp.Marketaux;

/// <summary>Marketaux aggregation intervals.</summary>
public enum MarketauxIntervals
{
    /// <summary>One-minute intervals.</summary>
    Minute,

    /// <summary>One-hour intervals.</summary>
    Hour,

    /// <summary>One-day intervals.</summary>
    Day,

    /// <summary>One-week intervals.</summary>
    Week,

    /// <summary>One-month intervals.</summary>
    Month,

    /// <summary>One-quarter intervals.</summary>
    Quarter,

    /// <summary>One-year intervals.</summary>
    Year,
}

/// <summary>Marketaux REST dataset kinds.</summary>
public enum MarketauxDataKinds
{
    /// <summary>News with entity relevance and sentiment analysis.</summary>
    NewsAnalysis,

    /// <summary>Entity sentiment time series.</summary>
    SentimentTimeSeries,

    /// <summary>Entity sentiment aggregation.</summary>
    SentimentAggregation,

    /// <summary>Trending entities.</summary>
    TrendingEntities,

    /// <summary>Supported entity types.</summary>
    EntityTypes,

    /// <summary>Supported entity industries.</summary>
    Industries,

    /// <summary>Supported news sources.</summary>
    NewsSources,
}

static class MarketauxMessageTypes
{
    public const MessageTypes Dataset =
        (MessageTypes)(-5007);
}

/// <summary>Custom data types exposed by the Marketaux connector.</summary>
public static class MarketauxDataTypes
{
    /// <summary>News with entity relevance and sentiment analysis.</summary>
    public static readonly DataType NewsAnalysis =
        Create(
            MarketauxDataKinds.NewsAnalysis,
            "News analysis");

    /// <summary>Entity sentiment time series.</summary>
    public static readonly DataType SentimentTimeSeries =
        Create(
            MarketauxDataKinds.SentimentTimeSeries,
            "Sentiment time series");

    /// <summary>Entity sentiment aggregation.</summary>
    public static readonly DataType SentimentAggregation =
        Create(
            MarketauxDataKinds.SentimentAggregation,
            "Sentiment aggregation");

    /// <summary>Trending entities.</summary>
    public static readonly DataType TrendingEntities =
        Create(
            MarketauxDataKinds.TrendingEntities,
            "Trending entities");

    /// <summary>Supported entity types.</summary>
    public static readonly DataType EntityTypes =
        Create(
            MarketauxDataKinds.EntityTypes,
            "Entity types");

    /// <summary>Supported entity industries.</summary>
    public static readonly DataType Industries =
        Create(
            MarketauxDataKinds.Industries,
            "Industries");

    /// <summary>Supported news sources.</summary>
    public static readonly DataType NewsSources =
        Create(
            MarketauxDataKinds.NewsSources,
            "News sources");

    /// <summary>All Marketaux custom data types.</summary>
    public static IReadOnlyList<DataType> All { get; } =
    [
        NewsAnalysis,
        SentimentTimeSeries,
        SentimentAggregation,
        TrendingEntities,
        EntityTypes,
        Industries,
        NewsSources,
    ];

    /// <summary>Get the data type for a dataset kind.</summary>
    public static DataType Get(MarketauxDataKinds kind)
        => kind switch
        {
            MarketauxDataKinds.NewsAnalysis => NewsAnalysis,
            MarketauxDataKinds.SentimentTimeSeries =>
                SentimentTimeSeries,
            MarketauxDataKinds.SentimentAggregation =>
                SentimentAggregation,
            MarketauxDataKinds.TrendingEntities =>
                TrendingEntities,
            MarketauxDataKinds.EntityTypes => EntityTypes,
            MarketauxDataKinds.Industries => Industries,
            MarketauxDataKinds.NewsSources => NewsSources,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    /// <summary>Try to get a dataset kind from a data type.</summary>
    public static bool TryGetKind(
        DataType dataType,
        out MarketauxDataKinds kind)
    {
        foreach (var value in Enum.GetValues<MarketauxDataKinds>())
        {
            if (dataType == Get(value))
            {
                kind = value;
                return true;
            }
        }
        kind = default;
        return false;
    }

    private static DataType Create(
        MarketauxDataKinds kind,
        string name)
        => DataType
            .Create<MarketauxDataMessage>(kind, true)
            .SetName(name)
            .Immutable();
}

/// <summary>
/// A Marketaux response preserved as normalized JSON.
/// </summary>
public class MarketauxDataMessage :
    BaseSubscriptionIdMessage<MarketauxDataMessage>,
    ISecurityIdMessage,
    IServerTimeMessage
{
    /// <summary>Initializes a new instance.</summary>
    public MarketauxDataMessage()
        : base(MarketauxMessageTypes.Dataset)
    {
    }

    /// <summary>Dataset kind.</summary>
    public MarketauxDataKinds Dataset { get; set; }

    /// <inheritdoc />
    public SecurityId SecurityId { get; set; }

    /// <inheritdoc />
    public DateTime ServerTime { get; set; }

    /// <summary>API resource path.</summary>
    public string Resource { get; set; }

    /// <summary>Normalized JSON response.</summary>
    public string Payload { get; set; }

    /// <inheritdoc />
    public override DataType DataType =>
        MarketauxDataTypes.Get(Dataset);

    /// <inheritdoc />
    public override Message Clone()
    {
        var copy = new MarketauxDataMessage
        {
            Dataset = Dataset,
            SecurityId = SecurityId,
            ServerTime = ServerTime,
            Resource = Resource,
            Payload = Payload,
        };
        CopyTo(copy);
        return copy;
    }
}
