namespace StockSharp.TradingEconomics;

/// <summary>Trading Economics company dataset kinds.</summary>
public enum TradingEconomicsDataKinds
{
    /// <summary>Latest company financial indicators.</summary>
    Financials,

    /// <summary>Company earnings and revenue releases.</summary>
    Earnings,
}

static class TradingEconomicsMessageTypes
{
    public const MessageTypes Dataset =
        (MessageTypes)(-5002);
}

/// <summary>Custom data types exposed by the Trading Economics connector.</summary>
public static class TradingEconomicsDataTypes
{
    /// <summary>Latest company financial indicators.</summary>
    public static readonly DataType Financials =
        Create(
            TradingEconomicsDataKinds.Financials,
            "Company financials");

    /// <summary>Company earnings and revenue releases.</summary>
    public static readonly DataType Earnings =
        Create(
            TradingEconomicsDataKinds.Earnings,
            "Earnings and revenues");

    /// <summary>All Trading Economics custom data types.</summary>
    public static IReadOnlyList<DataType> All { get; } =
    [
        Financials,
        Earnings,
    ];

    /// <summary>Get the data type for a dataset kind.</summary>
    public static DataType Get(TradingEconomicsDataKinds kind)
        => kind switch
        {
            TradingEconomicsDataKinds.Financials => Financials,
            TradingEconomicsDataKinds.Earnings => Earnings,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    /// <summary>Try to get a dataset kind from a data type.</summary>
    public static bool TryGetKind(
        DataType dataType,
        out TradingEconomicsDataKinds kind)
    {
        foreach (var value in Enum.GetValues<
            TradingEconomicsDataKinds>())
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
        TradingEconomicsDataKinds kind,
        string name)
        => DataType
            .Create<TradingEconomicsDataMessage>(kind, true)
            .SetName(name)
            .Immutable();
}

/// <summary>
/// A Trading Economics company dataset preserved as normalized JSON.
/// </summary>
public class TradingEconomicsDataMessage :
    BaseSubscriptionIdMessage<TradingEconomicsDataMessage>,
    ISecurityIdMessage,
    IServerTimeMessage
{
    /// <summary>Initializes a new instance.</summary>
    public TradingEconomicsDataMessage()
        : base(TradingEconomicsMessageTypes.Dataset)
    {
    }

    /// <summary>Dataset kind.</summary>
    public TradingEconomicsDataKinds Dataset { get; set; }

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
        TradingEconomicsDataTypes.Get(Dataset);

    /// <inheritdoc />
    public override Message Clone()
    {
        var copy = new TradingEconomicsDataMessage
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
