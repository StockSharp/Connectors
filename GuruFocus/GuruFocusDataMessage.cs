namespace StockSharp.GuruFocus;

/// <summary>GuruFocus Data API dataset kinds.</summary>
public enum GuruFocusDataKinds
{
    /// <summary>Company profile and current metrics.</summary>
    Profile,

    /// <summary>Historical financial statements.</summary>
    Fundamentals,

    /// <summary>Historical valuation multiples and ratios.</summary>
    Valuations,

    /// <summary>GuruFocus proprietary rankings.</summary>
    Rankings,

    /// <summary>ETF profile, market statistics, and holdings.</summary>
    EtfData,

    /// <summary>SEC filings.</summary>
    SecFilings,

    /// <summary>Corporate insider trades.</summary>
    InsiderTrades,

    /// <summary>Institutional investor position changes.</summary>
    GuruTrades,

    /// <summary>Current institutional investor holdings.</summary>
    GuruHoldings,
}

static class GuruFocusMessageTypes
{
    public const MessageTypes Dataset =
        (MessageTypes)(-5004);
}

/// <summary>Custom data types exposed by the GuruFocus connector.</summary>
public static class GuruFocusDataTypes
{
    /// <summary>Company profile and current metrics.</summary>
    public static readonly DataType Profile =
        Create(GuruFocusDataKinds.Profile, "Company profile");

    /// <summary>Historical financial statements.</summary>
    public static readonly DataType Fundamentals =
        Create(
            GuruFocusDataKinds.Fundamentals,
            "Financial statements");

    /// <summary>Historical valuation multiples and ratios.</summary>
    public static readonly DataType Valuations =
        Create(
            GuruFocusDataKinds.Valuations,
            "Valuations and ratios");

    /// <summary>GuruFocus proprietary rankings.</summary>
    public static readonly DataType Rankings =
        Create(GuruFocusDataKinds.Rankings, "GuruFocus rankings");

    /// <summary>ETF profile, market statistics, and holdings.</summary>
    public static readonly DataType EtfData =
        Create(GuruFocusDataKinds.EtfData, "ETF data");

    /// <summary>SEC filings.</summary>
    public static readonly DataType SecFilings =
        Create(GuruFocusDataKinds.SecFilings, "SEC filings");

    /// <summary>Corporate insider trades.</summary>
    public static readonly DataType InsiderTrades =
        Create(GuruFocusDataKinds.InsiderTrades, "Insider trades");

    /// <summary>Institutional investor position changes.</summary>
    public static readonly DataType GuruTrades =
        Create(GuruFocusDataKinds.GuruTrades, "Guru trades");

    /// <summary>Current institutional investor holdings.</summary>
    public static readonly DataType GuruHoldings =
        Create(GuruFocusDataKinds.GuruHoldings, "Guru holdings");

    /// <summary>All GuruFocus custom data types.</summary>
    public static IReadOnlyList<DataType> All { get; } =
    [
        Profile,
        Fundamentals,
        Valuations,
        Rankings,
        EtfData,
        SecFilings,
        InsiderTrades,
        GuruTrades,
        GuruHoldings,
    ];

    /// <summary>Get the data type for a dataset kind.</summary>
    public static DataType Get(GuruFocusDataKinds kind)
        => kind switch
        {
            GuruFocusDataKinds.Profile => Profile,
            GuruFocusDataKinds.Fundamentals => Fundamentals,
            GuruFocusDataKinds.Valuations => Valuations,
            GuruFocusDataKinds.Rankings => Rankings,
            GuruFocusDataKinds.EtfData => EtfData,
            GuruFocusDataKinds.SecFilings => SecFilings,
            GuruFocusDataKinds.InsiderTrades => InsiderTrades,
            GuruFocusDataKinds.GuruTrades => GuruTrades,
            GuruFocusDataKinds.GuruHoldings => GuruHoldings,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    /// <summary>Try to get a dataset kind from a data type.</summary>
    public static bool TryGetKind(
        DataType dataType,
        out GuruFocusDataKinds kind)
    {
        foreach (var value in Enum.GetValues<GuruFocusDataKinds>())
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
        GuruFocusDataKinds kind,
        string name)
        => DataType
            .Create<GuruFocusDataMessage>(kind, true)
            .SetName(name)
            .Immutable();
}

/// <summary>
/// A GuruFocus Data API response preserved as normalized JSON.
/// </summary>
public class GuruFocusDataMessage :
    BaseSubscriptionIdMessage<GuruFocusDataMessage>,
    ISecurityIdMessage,
    IServerTimeMessage
{
    /// <summary>Initializes a new instance.</summary>
    public GuruFocusDataMessage()
        : base(GuruFocusMessageTypes.Dataset)
    {
    }

    /// <summary>Dataset kind.</summary>
    public GuruFocusDataKinds Dataset { get; set; }

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
        GuruFocusDataTypes.Get(Dataset);

    /// <inheritdoc />
    public override Message Clone()
    {
        var copy = new GuruFocusDataMessage
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
