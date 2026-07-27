namespace StockSharp.Bavest;

/// <summary>Bavest financial statement frequencies.</summary>
public enum BavestFinancialFrequencies
{
    /// <summary>Annual statements.</summary>
    Annual,

    /// <summary>Quarterly statements.</summary>
    Quarterly,
}

/// <summary>Bavest REST dataset kinds.</summary>
public enum BavestDataKinds
{
    /// <summary>Company identity, classification, and market data.</summary>
    CompanyProfile,

    /// <summary>Comprehensive equity financial metrics.</summary>
    EquityMetrics,

    /// <summary>Income statements.</summary>
    IncomeStatements,

    /// <summary>Balance sheets.</summary>
    BalanceSheets,

    /// <summary>Cash-flow statements.</summary>
    CashFlows,

    /// <summary>Trailing-twelve-month financial snapshot.</summary>
    FinancialsTtm,

    /// <summary>Analyst consensus estimates.</summary>
    AnalystConsensus,

    /// <summary>Analyst recommendation history.</summary>
    AnalystRecommendations,

    /// <summary>Analyst price-target consensus.</summary>
    PriceTarget,

    /// <summary>Analyst upgrades and downgrades.</summary>
    UpgradesDowngrades,

    /// <summary>Dividend payment history.</summary>
    DividendHistory,

    /// <summary>ETF identity and classification.</summary>
    EtfProfile,

    /// <summary>ETF performance, risk, and volatility metrics.</summary>
    EtfMetrics,

    /// <summary>Stock screener results.</summary>
    Screener,
}

static class BavestMessageTypes
{
    public const MessageTypes Dataset =
        (MessageTypes)(-5008);
}

/// <summary>Custom data types exposed by the Bavest connector.</summary>
public static class BavestDataTypes
{
    /// <summary>Company profile.</summary>
    public static readonly DataType CompanyProfile =
        Create(BavestDataKinds.CompanyProfile, "Company profile");

    /// <summary>Comprehensive equity metrics.</summary>
    public static readonly DataType EquityMetrics =
        Create(BavestDataKinds.EquityMetrics, "Equity metrics");

    /// <summary>Income statements.</summary>
    public static readonly DataType IncomeStatements =
        Create(
            BavestDataKinds.IncomeStatements,
            "Income statements");

    /// <summary>Balance sheets.</summary>
    public static readonly DataType BalanceSheets =
        Create(BavestDataKinds.BalanceSheets, "Balance sheets");

    /// <summary>Cash-flow statements.</summary>
    public static readonly DataType CashFlows =
        Create(BavestDataKinds.CashFlows, "Cash flows");

    /// <summary>Trailing-twelve-month financial snapshot.</summary>
    public static readonly DataType FinancialsTtm =
        Create(BavestDataKinds.FinancialsTtm, "Financials TTM");

    /// <summary>Analyst consensus estimates.</summary>
    public static readonly DataType AnalystConsensus =
        Create(
            BavestDataKinds.AnalystConsensus,
            "Analyst consensus");

    /// <summary>Analyst recommendation history.</summary>
    public static readonly DataType AnalystRecommendations =
        Create(
            BavestDataKinds.AnalystRecommendations,
            "Analyst recommendations");

    /// <summary>Analyst price-target consensus.</summary>
    public static readonly DataType PriceTarget =
        Create(BavestDataKinds.PriceTarget, "Price target");

    /// <summary>Analyst upgrades and downgrades.</summary>
    public static readonly DataType UpgradesDowngrades =
        Create(
            BavestDataKinds.UpgradesDowngrades,
            "Upgrades and downgrades");

    /// <summary>Dividend payment history.</summary>
    public static readonly DataType DividendHistory =
        Create(
            BavestDataKinds.DividendHistory,
            "Dividend history");

    /// <summary>ETF profile.</summary>
    public static readonly DataType EtfProfile =
        Create(BavestDataKinds.EtfProfile, "ETF profile");

    /// <summary>ETF metrics.</summary>
    public static readonly DataType EtfMetrics =
        Create(BavestDataKinds.EtfMetrics, "ETF metrics");

    /// <summary>Stock screener results.</summary>
    public static readonly DataType Screener =
        Create(BavestDataKinds.Screener, "Stock screener");

    /// <summary>All Bavest custom data types.</summary>
    public static IReadOnlyList<DataType> All { get; } =
    [
        CompanyProfile,
        EquityMetrics,
        IncomeStatements,
        BalanceSheets,
        CashFlows,
        FinancialsTtm,
        AnalystConsensus,
        AnalystRecommendations,
        PriceTarget,
        UpgradesDowngrades,
        DividendHistory,
        EtfProfile,
        EtfMetrics,
        Screener,
    ];

    /// <summary>Get the data type for a dataset kind.</summary>
    public static DataType Get(BavestDataKinds kind)
        => kind switch
        {
            BavestDataKinds.CompanyProfile => CompanyProfile,
            BavestDataKinds.EquityMetrics => EquityMetrics,
            BavestDataKinds.IncomeStatements => IncomeStatements,
            BavestDataKinds.BalanceSheets => BalanceSheets,
            BavestDataKinds.CashFlows => CashFlows,
            BavestDataKinds.FinancialsTtm => FinancialsTtm,
            BavestDataKinds.AnalystConsensus => AnalystConsensus,
            BavestDataKinds.AnalystRecommendations =>
                AnalystRecommendations,
            BavestDataKinds.PriceTarget => PriceTarget,
            BavestDataKinds.UpgradesDowngrades =>
                UpgradesDowngrades,
            BavestDataKinds.DividendHistory => DividendHistory,
            BavestDataKinds.EtfProfile => EtfProfile,
            BavestDataKinds.EtfMetrics => EtfMetrics,
            BavestDataKinds.Screener => Screener,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    /// <summary>Try to get a dataset kind from a data type.</summary>
    public static bool TryGetKind(
        DataType dataType,
        out BavestDataKinds kind)
    {
        foreach (var value in Enum.GetValues<BavestDataKinds>())
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
        BavestDataKinds kind,
        string name)
        => DataType
            .Create<BavestDataMessage>(kind, true)
            .SetName(name)
            .Immutable();
}

/// <summary>
/// A Bavest response preserved as normalized JSON.
/// </summary>
public class BavestDataMessage :
    BaseSubscriptionIdMessage<BavestDataMessage>,
    ISecurityIdMessage,
    IServerTimeMessage
{
    /// <summary>Initializes a new instance.</summary>
    public BavestDataMessage()
        : base(BavestMessageTypes.Dataset)
    {
    }

    /// <summary>Dataset kind.</summary>
    public BavestDataKinds Dataset { get; set; }

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
        BavestDataTypes.Get(Dataset);

    /// <inheritdoc />
    public override Message Clone()
    {
        var copy = new BavestDataMessage
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
