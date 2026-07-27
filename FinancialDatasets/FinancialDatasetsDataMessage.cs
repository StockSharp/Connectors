namespace StockSharp.FinancialDatasets;

/// <summary>Financial Datasets API dataset kinds.</summary>
public enum FinancialDatasetsDataKinds
{
    /// <summary>Company facts.</summary>
    CompanyFacts,

    /// <summary>Financial statements.</summary>
    FinancialStatements,

    /// <summary>Financial metrics and ratios.</summary>
    FinancialMetrics,

    /// <summary>SEC filings.</summary>
    SecFilings,

    /// <summary>Company earnings.</summary>
    Earnings,

    /// <summary>Insider trades.</summary>
    InsiderTrades,

    /// <summary>Insider ownership.</summary>
    InsiderOwnership,

    /// <summary>Beneficial ownership.</summary>
    BeneficialOwnership,

    /// <summary>Activist ownership.</summary>
    ActivistOwnership,

    /// <summary>Institutional holdings.</summary>
    InstitutionalHoldings,
}

/// <summary>Financial reporting periods.</summary>
public enum FinancialDatasetsPeriods
{
    /// <summary>Annual reports.</summary>
    Annual,

    /// <summary>Quarterly reports.</summary>
    Quarterly,

    /// <summary>Trailing twelve months.</summary>
    Ttm,
}

static class FinancialDatasetsMessageTypes
{
    public const MessageTypes Dataset =
        (MessageTypes)(-5001);
}

/// <summary>Custom data types exposed by the Financial Datasets connector.</summary>
public static class FinancialDatasetsDataTypes
{
    /// <summary>Company facts.</summary>
    public static readonly DataType CompanyFacts =
        Create(FinancialDatasetsDataKinds.CompanyFacts, "Company facts");

    /// <summary>Financial statements.</summary>
    public static readonly DataType FinancialStatements =
        Create(
            FinancialDatasetsDataKinds.FinancialStatements,
            "Financial statements");

    /// <summary>Financial metrics.</summary>
    public static readonly DataType FinancialMetrics =
        Create(
            FinancialDatasetsDataKinds.FinancialMetrics,
            "Financial metrics");

    /// <summary>SEC filings.</summary>
    public static readonly DataType SecFilings =
        Create(FinancialDatasetsDataKinds.SecFilings, "SEC filings");

    /// <summary>Earnings.</summary>
    public static readonly DataType Earnings =
        Create(FinancialDatasetsDataKinds.Earnings, "Earnings");

    /// <summary>Insider trades.</summary>
    public static readonly DataType InsiderTrades =
        Create(
            FinancialDatasetsDataKinds.InsiderTrades,
            "Insider trades");

    /// <summary>Insider ownership.</summary>
    public static readonly DataType InsiderOwnership =
        Create(
            FinancialDatasetsDataKinds.InsiderOwnership,
            "Insider ownership");

    /// <summary>Beneficial ownership.</summary>
    public static readonly DataType BeneficialOwnership =
        Create(
            FinancialDatasetsDataKinds.BeneficialOwnership,
            "Beneficial ownership");

    /// <summary>Activist ownership.</summary>
    public static readonly DataType ActivistOwnership =
        Create(
            FinancialDatasetsDataKinds.ActivistOwnership,
            "Activist ownership");

    /// <summary>Institutional holdings.</summary>
    public static readonly DataType InstitutionalHoldings =
        Create(
            FinancialDatasetsDataKinds.InstitutionalHoldings,
            "Institutional holdings");

    /// <summary>All Financial Datasets custom data types.</summary>
    public static IReadOnlyList<DataType> All { get; } =
    [
        CompanyFacts,
        FinancialStatements,
        FinancialMetrics,
        SecFilings,
        Earnings,
        InsiderTrades,
        InsiderOwnership,
        BeneficialOwnership,
        ActivistOwnership,
        InstitutionalHoldings,
    ];

    /// <summary>Get the data type for a dataset kind.</summary>
    public static DataType Get(FinancialDatasetsDataKinds kind)
        => kind switch
        {
            FinancialDatasetsDataKinds.CompanyFacts => CompanyFacts,
            FinancialDatasetsDataKinds.FinancialStatements =>
                FinancialStatements,
            FinancialDatasetsDataKinds.FinancialMetrics =>
                FinancialMetrics,
            FinancialDatasetsDataKinds.SecFilings => SecFilings,
            FinancialDatasetsDataKinds.Earnings => Earnings,
            FinancialDatasetsDataKinds.InsiderTrades => InsiderTrades,
            FinancialDatasetsDataKinds.InsiderOwnership =>
                InsiderOwnership,
            FinancialDatasetsDataKinds.BeneficialOwnership =>
                BeneficialOwnership,
            FinancialDatasetsDataKinds.ActivistOwnership =>
                ActivistOwnership,
            FinancialDatasetsDataKinds.InstitutionalHoldings =>
                InstitutionalHoldings,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    /// <summary>Try to get a dataset kind from a data type.</summary>
    public static bool TryGetKind(
        DataType dataType,
        out FinancialDatasetsDataKinds kind)
    {
        foreach (var value in Enum.GetValues<
            FinancialDatasetsDataKinds>())
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
        FinancialDatasetsDataKinds kind,
        string name)
        => DataType
            .Create<FinancialDatasetsDataMessage>(kind, true)
            .SetName(name)
            .Immutable();
}

/// <summary>
/// A Financial Datasets API response preserved as normalized JSON.
/// </summary>
public class FinancialDatasetsDataMessage :
    BaseSubscriptionIdMessage<FinancialDatasetsDataMessage>,
    ISecurityIdMessage,
    IServerTimeMessage
{
    /// <summary>Initializes a new instance.</summary>
    public FinancialDatasetsDataMessage()
        : base(FinancialDatasetsMessageTypes.Dataset)
    {
    }

    /// <summary>Dataset kind.</summary>
    public FinancialDatasetsDataKinds Dataset { get; set; }

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
        FinancialDatasetsDataTypes.Get(Dataset);

    /// <inheritdoc />
    public override Message Clone()
    {
        var copy = new FinancialDatasetsDataMessage
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
