namespace StockSharp.QuiverQuant;

/// <summary>Quiver Quantitative dataset kinds.</summary>
public enum QuiverQuantDataKinds
{
    /// <summary>U.S. Congress stock transactions.</summary>
    CongressTrades,

    /// <summary>SEC Form 4 insider transactions.</summary>
    InsiderTrades,

    /// <summary>SEC Form 13F institutional holdings.</summary>
    InstitutionalHoldings,

    /// <summary>SEC Form 13F institutional holding changes.</summary>
    InstitutionalChanges,

    /// <summary>Off-exchange short volume and dark-pool intensity.</summary>
    OffExchange,

    /// <summary>U.S. government contracts.</summary>
    GovernmentContracts,

    /// <summary>Corporate lobbying records.</summary>
    Lobbying,

    /// <summary>Corporate PAC donations.</summary>
    CorporateDonors,

    /// <summary>Corporate patent grants.</summary>
    Patents,

    /// <summary>Executive compensation records.</summary>
    ExecutiveCompensation,

    /// <summary>Direct and derivative top shareholders.</summary>
    TopShareholders,

    /// <summary>New Constructs earnings distortion scores.</summary>
    EarningsDistortionScores,

    /// <summary>Recent CNBC analyst transactions.</summary>
    CnbcTrades,

    /// <summary>Patent drift signals.</summary>
    PatentDrift,

    /// <summary>Patent momentum signals.</summary>
    PatentMomentum,

    /// <summary>Event beta values and election odds.</summary>
    EventsBeta,
}

static class QuiverQuantMessageTypes
{
    public const MessageTypes Dataset =
        (MessageTypes)(-5005);
}

/// <summary>
/// Custom data types exposed by the Quiver Quantitative connector.
/// </summary>
public static class QuiverQuantDataTypes
{
    /// <summary>U.S. Congress stock transactions.</summary>
    public static readonly DataType CongressTrades =
        Create(
            QuiverQuantDataKinds.CongressTrades,
            "Congress trades");

    /// <summary>SEC Form 4 insider transactions.</summary>
    public static readonly DataType InsiderTrades =
        Create(
            QuiverQuantDataKinds.InsiderTrades,
            "Insider trades");

    /// <summary>SEC Form 13F institutional holdings.</summary>
    public static readonly DataType InstitutionalHoldings =
        Create(
            QuiverQuantDataKinds.InstitutionalHoldings,
            "Institutional holdings");

    /// <summary>SEC Form 13F institutional holding changes.</summary>
    public static readonly DataType InstitutionalChanges =
        Create(
            QuiverQuantDataKinds.InstitutionalChanges,
            "Institutional changes");

    /// <summary>Off-exchange short volume and dark-pool intensity.</summary>
    public static readonly DataType OffExchange =
        Create(
            QuiverQuantDataKinds.OffExchange,
            "Off-exchange trading");

    /// <summary>U.S. government contracts.</summary>
    public static readonly DataType GovernmentContracts =
        Create(
            QuiverQuantDataKinds.GovernmentContracts,
            "Government contracts");

    /// <summary>Corporate lobbying records.</summary>
    public static readonly DataType Lobbying =
        Create(QuiverQuantDataKinds.Lobbying, "Lobbying");

    /// <summary>Corporate PAC donations.</summary>
    public static readonly DataType CorporateDonors =
        Create(
            QuiverQuantDataKinds.CorporateDonors,
            "Corporate donors");

    /// <summary>Corporate patent grants.</summary>
    public static readonly DataType Patents =
        Create(QuiverQuantDataKinds.Patents, "Patents");

    /// <summary>Executive compensation records.</summary>
    public static readonly DataType ExecutiveCompensation =
        Create(
            QuiverQuantDataKinds.ExecutiveCompensation,
            "Executive compensation");

    /// <summary>Direct and derivative top shareholders.</summary>
    public static readonly DataType TopShareholders =
        Create(
            QuiverQuantDataKinds.TopShareholders,
            "Top shareholders");

    /// <summary>New Constructs earnings distortion scores.</summary>
    public static readonly DataType EarningsDistortionScores =
        Create(
            QuiverQuantDataKinds.EarningsDistortionScores,
            "Earnings distortion scores");

    /// <summary>Recent CNBC analyst transactions.</summary>
    public static readonly DataType CnbcTrades =
        Create(QuiverQuantDataKinds.CnbcTrades, "CNBC trades");

    /// <summary>Patent drift signals.</summary>
    public static readonly DataType PatentDrift =
        Create(QuiverQuantDataKinds.PatentDrift, "Patent drift");

    /// <summary>Patent momentum signals.</summary>
    public static readonly DataType PatentMomentum =
        Create(
            QuiverQuantDataKinds.PatentMomentum,
            "Patent momentum");

    /// <summary>Event beta values and election odds.</summary>
    public static readonly DataType EventsBeta =
        Create(QuiverQuantDataKinds.EventsBeta, "Events beta");

    /// <summary>All Quiver Quantitative custom data types.</summary>
    public static IReadOnlyList<DataType> All { get; } =
    [
        CongressTrades,
        InsiderTrades,
        InstitutionalHoldings,
        InstitutionalChanges,
        OffExchange,
        GovernmentContracts,
        Lobbying,
        CorporateDonors,
        Patents,
        ExecutiveCompensation,
        TopShareholders,
        EarningsDistortionScores,
        CnbcTrades,
        PatentDrift,
        PatentMomentum,
        EventsBeta,
    ];

    /// <summary>Get the data type for a dataset kind.</summary>
    public static DataType Get(QuiverQuantDataKinds kind)
        => kind switch
        {
            QuiverQuantDataKinds.CongressTrades => CongressTrades,
            QuiverQuantDataKinds.InsiderTrades => InsiderTrades,
            QuiverQuantDataKinds.InstitutionalHoldings =>
                InstitutionalHoldings,
            QuiverQuantDataKinds.InstitutionalChanges =>
                InstitutionalChanges,
            QuiverQuantDataKinds.OffExchange => OffExchange,
            QuiverQuantDataKinds.GovernmentContracts =>
                GovernmentContracts,
            QuiverQuantDataKinds.Lobbying => Lobbying,
            QuiverQuantDataKinds.CorporateDonors => CorporateDonors,
            QuiverQuantDataKinds.Patents => Patents,
            QuiverQuantDataKinds.ExecutiveCompensation =>
                ExecutiveCompensation,
            QuiverQuantDataKinds.TopShareholders => TopShareholders,
            QuiverQuantDataKinds.EarningsDistortionScores =>
                EarningsDistortionScores,
            QuiverQuantDataKinds.CnbcTrades => CnbcTrades,
            QuiverQuantDataKinds.PatentDrift => PatentDrift,
            QuiverQuantDataKinds.PatentMomentum => PatentMomentum,
            QuiverQuantDataKinds.EventsBeta => EventsBeta,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    /// <summary>Try to get a dataset kind from a data type.</summary>
    public static bool TryGetKind(
        DataType dataType,
        out QuiverQuantDataKinds kind)
    {
        foreach (var value in Enum.GetValues<QuiverQuantDataKinds>())
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
        QuiverQuantDataKinds kind,
        string name)
        => DataType
            .Create<QuiverQuantDataMessage>(kind, true)
            .SetName(name)
            .Immutable();
}

/// <summary>
/// A Quiver Quantitative response preserved as normalized JSON.
/// </summary>
public class QuiverQuantDataMessage :
    BaseSubscriptionIdMessage<QuiverQuantDataMessage>,
    ISecurityIdMessage,
    IServerTimeMessage
{
    /// <summary>Initializes a new instance.</summary>
    public QuiverQuantDataMessage()
        : base(QuiverQuantMessageTypes.Dataset)
    {
    }

    /// <summary>Dataset kind.</summary>
    public QuiverQuantDataKinds Dataset { get; set; }

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
        QuiverQuantDataTypes.Get(Dataset);

    /// <inheritdoc />
    public override Message Clone()
    {
        var copy = new QuiverQuantDataMessage
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
