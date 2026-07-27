namespace StockSharp.UnusualWhales;

/// <summary>Unusual Whales REST dataset kinds.</summary>
public enum UnusualWhalesDataKinds
{
    /// <summary>Normalized company profile.</summary>
    CompanyProfile,

    /// <summary>Latest stock-session state.</summary>
    StockState,

    /// <summary>Rule-based unusual options-flow alerts.</summary>
    OptionsFlowAlerts,

    /// <summary>Recent ticker options flow.</summary>
    RecentOptionsFlow,

    /// <summary>Off-exchange and dark-pool prints.</summary>
    DarkPoolTrades,

    /// <summary>Interpolated implied volatility.</summary>
    InterpolatedIv,

    /// <summary>Implied and realized volatility statistics.</summary>
    VolatilityStats,

    /// <summary>Options Greek exposure.</summary>
    GreekExposure,

    /// <summary>Options volume.</summary>
    OptionsVolume,

    /// <summary>SEC insider transactions.</summary>
    InsiderTransactions,

    /// <summary>Recent U.S. Congress transactions.</summary>
    CongressTrades,

    /// <summary>Market-wide options tide.</summary>
    MarketTide,

    /// <summary>Top gainers, losers, and most-active stocks.</summary>
    MarketMovers,
}

static class UnusualWhalesMessageTypes
{
    public const MessageTypes Dataset =
        (MessageTypes)(-5006);
}

/// <summary>Custom data types exposed by the Unusual Whales connector.</summary>
public static class UnusualWhalesDataTypes
{
    /// <summary>Normalized company profile.</summary>
    public static readonly DataType CompanyProfile =
        Create(
            UnusualWhalesDataKinds.CompanyProfile,
            "Company profile");

    /// <summary>Latest stock-session state.</summary>
    public static readonly DataType StockState =
        Create(
            UnusualWhalesDataKinds.StockState,
            "Stock state");

    /// <summary>Rule-based unusual options-flow alerts.</summary>
    public static readonly DataType OptionsFlowAlerts =
        Create(
            UnusualWhalesDataKinds.OptionsFlowAlerts,
            "Options flow alerts");

    /// <summary>Recent ticker options flow.</summary>
    public static readonly DataType RecentOptionsFlow =
        Create(
            UnusualWhalesDataKinds.RecentOptionsFlow,
            "Recent options flow");

    /// <summary>Off-exchange and dark-pool prints.</summary>
    public static readonly DataType DarkPoolTrades =
        Create(
            UnusualWhalesDataKinds.DarkPoolTrades,
            "Dark-pool trades");

    /// <summary>Interpolated implied volatility.</summary>
    public static readonly DataType InterpolatedIv =
        Create(
            UnusualWhalesDataKinds.InterpolatedIv,
            "Interpolated IV");

    /// <summary>Implied and realized volatility statistics.</summary>
    public static readonly DataType VolatilityStats =
        Create(
            UnusualWhalesDataKinds.VolatilityStats,
            "Volatility statistics");

    /// <summary>Options Greek exposure.</summary>
    public static readonly DataType GreekExposure =
        Create(
            UnusualWhalesDataKinds.GreekExposure,
            "Greek exposure");

    /// <summary>Options volume.</summary>
    public static readonly DataType OptionsVolume =
        Create(
            UnusualWhalesDataKinds.OptionsVolume,
            "Options volume");

    /// <summary>SEC insider transactions.</summary>
    public static readonly DataType InsiderTransactions =
        Create(
            UnusualWhalesDataKinds.InsiderTransactions,
            "Insider transactions");

    /// <summary>Recent U.S. Congress transactions.</summary>
    public static readonly DataType CongressTrades =
        Create(
            UnusualWhalesDataKinds.CongressTrades,
            "Congress trades");

    /// <summary>Market-wide options tide.</summary>
    public static readonly DataType MarketTide =
        Create(
            UnusualWhalesDataKinds.MarketTide,
            "Market tide");

    /// <summary>Top gainers, losers, and most-active stocks.</summary>
    public static readonly DataType MarketMovers =
        Create(
            UnusualWhalesDataKinds.MarketMovers,
            "Market movers");

    /// <summary>All Unusual Whales custom data types.</summary>
    public static IReadOnlyList<DataType> All { get; } =
    [
        CompanyProfile,
        StockState,
        OptionsFlowAlerts,
        RecentOptionsFlow,
        DarkPoolTrades,
        InterpolatedIv,
        VolatilityStats,
        GreekExposure,
        OptionsVolume,
        InsiderTransactions,
        CongressTrades,
        MarketTide,
        MarketMovers,
    ];

    /// <summary>Get the data type for a dataset kind.</summary>
    public static DataType Get(UnusualWhalesDataKinds kind)
        => kind switch
        {
            UnusualWhalesDataKinds.CompanyProfile =>
                CompanyProfile,
            UnusualWhalesDataKinds.StockState => StockState,
            UnusualWhalesDataKinds.OptionsFlowAlerts =>
                OptionsFlowAlerts,
            UnusualWhalesDataKinds.RecentOptionsFlow =>
                RecentOptionsFlow,
            UnusualWhalesDataKinds.DarkPoolTrades =>
                DarkPoolTrades,
            UnusualWhalesDataKinds.InterpolatedIv =>
                InterpolatedIv,
            UnusualWhalesDataKinds.VolatilityStats =>
                VolatilityStats,
            UnusualWhalesDataKinds.GreekExposure =>
                GreekExposure,
            UnusualWhalesDataKinds.OptionsVolume =>
                OptionsVolume,
            UnusualWhalesDataKinds.InsiderTransactions =>
                InsiderTransactions,
            UnusualWhalesDataKinds.CongressTrades =>
                CongressTrades,
            UnusualWhalesDataKinds.MarketTide => MarketTide,
            UnusualWhalesDataKinds.MarketMovers =>
                MarketMovers,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    /// <summary>Try to get a dataset kind from a data type.</summary>
    public static bool TryGetKind(
        DataType dataType,
        out UnusualWhalesDataKinds kind)
    {
        foreach (var value in Enum.GetValues<
            UnusualWhalesDataKinds>())
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
        UnusualWhalesDataKinds kind,
        string name)
        => DataType
            .Create<UnusualWhalesDataMessage>(kind, true)
            .SetName(name)
            .Immutable();
}

/// <summary>
/// An Unusual Whales response preserved as normalized JSON.
/// </summary>
public class UnusualWhalesDataMessage :
    BaseSubscriptionIdMessage<UnusualWhalesDataMessage>,
    ISecurityIdMessage,
    IServerTimeMessage
{
    /// <summary>Initializes a new instance.</summary>
    public UnusualWhalesDataMessage()
        : base(UnusualWhalesMessageTypes.Dataset)
    {
    }

    /// <summary>Dataset kind.</summary>
    public UnusualWhalesDataKinds Dataset { get; set; }

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
        UnusualWhalesDataTypes.Get(Dataset);

    /// <inheritdoc />
    public override Message Clone()
    {
        var copy = new UnusualWhalesDataMessage
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
