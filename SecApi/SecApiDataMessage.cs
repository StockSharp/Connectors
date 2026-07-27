namespace StockSharp.SecApi;

/// <summary>SEC-API.io dataset kinds.</summary>
public enum SecApiDataKinds
{
    /// <summary>SEC filing metadata.</summary>
    Filings,

    /// <summary>XBRL filing converted to JSON.</summary>
    Xbrl,

    /// <summary>Form 13F institutional holdings.</summary>
    InstitutionalHoldings,

    /// <summary>Form 3, 4, and 5 insider transactions.</summary>
    InsiderTrades,

    /// <summary>Form 13D and 13G beneficial ownership reports.</summary>
    BeneficialOwnership,
}

static class SecApiMessageTypes
{
    public const MessageTypes Dataset =
        (MessageTypes)(-5003);
}

/// <summary>Custom data types exposed by the SEC-API.io connector.</summary>
public static class SecApiDataTypes
{
    /// <summary>SEC filing metadata.</summary>
    public static readonly DataType Filings =
        Create(SecApiDataKinds.Filings, "SEC filings");

    /// <summary>XBRL filing converted to JSON.</summary>
    public static readonly DataType Xbrl =
        Create(SecApiDataKinds.Xbrl, "XBRL to JSON");

    /// <summary>Form 13F institutional holdings.</summary>
    public static readonly DataType InstitutionalHoldings =
        Create(
            SecApiDataKinds.InstitutionalHoldings,
            "Form 13F institutional holdings");

    /// <summary>Form 3, 4, and 5 insider transactions.</summary>
    public static readonly DataType InsiderTrades =
        Create(
            SecApiDataKinds.InsiderTrades,
            "SEC insider trades");

    /// <summary>Form 13D and 13G beneficial ownership.</summary>
    public static readonly DataType BeneficialOwnership =
        Create(
            SecApiDataKinds.BeneficialOwnership,
            "Form 13D/13G beneficial ownership");

    /// <summary>All SEC-API.io custom data types.</summary>
    public static IReadOnlyList<DataType> All { get; } =
    [
        Filings,
        Xbrl,
        InstitutionalHoldings,
        InsiderTrades,
        BeneficialOwnership,
    ];

    /// <summary>Get the data type for a dataset kind.</summary>
    public static DataType Get(SecApiDataKinds kind)
        => kind switch
        {
            SecApiDataKinds.Filings => Filings,
            SecApiDataKinds.Xbrl => Xbrl,
            SecApiDataKinds.InstitutionalHoldings =>
                InstitutionalHoldings,
            SecApiDataKinds.InsiderTrades => InsiderTrades,
            SecApiDataKinds.BeneficialOwnership =>
                BeneficialOwnership,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    /// <summary>Try to get a dataset kind from a data type.</summary>
    public static bool TryGetKind(
        DataType dataType,
        out SecApiDataKinds kind)
    {
        foreach (var value in Enum.GetValues<SecApiDataKinds>())
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
        SecApiDataKinds kind,
        string name)
        => DataType
            .Create<SecApiDataMessage>(kind, true)
            .SetName(name)
            .Immutable();
}

/// <summary>
/// A SEC-API.io response preserved as normalized JSON.
/// </summary>
public class SecApiDataMessage :
    BaseSubscriptionIdMessage<SecApiDataMessage>,
    ISecurityIdMessage,
    IServerTimeMessage
{
    /// <summary>Initializes a new instance.</summary>
    public SecApiDataMessage()
        : base(SecApiMessageTypes.Dataset)
    {
    }

    /// <summary>Dataset kind.</summary>
    public SecApiDataKinds Dataset { get; set; }

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
        SecApiDataTypes.Get(Dataset);

    /// <inheritdoc />
    public override Message Clone()
    {
        var copy = new SecApiDataMessage
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
