namespace StockSharp.B3Up2Data;

/// <summary>Text formats distributed by B3 UP2DATA Cloud.</summary>
public enum B3Up2DataFileFormats
{
    /// <summary>Semicolon-delimited CSV.</summary>
    Csv,

    /// <summary>JSON.</summary>
    Json,

    /// <summary>XML.</summary>
    Xml,

    /// <summary>Plain text.</summary>
    Txt,
}

/// <summary>B3 UP2DATA Cloud datasets.</summary>
public enum B3Up2DataDataKinds
{
    /// <summary>Blob metadata for a configurable prefix.</summary>
    BlobCatalog,

    /// <summary>Equities security master.</summary>
    SecurityMaster,

    /// <summary>Equities end-of-day prices.</summary>
    EquitiesEod,

    /// <summary>Equities consolidated trade information.</summary>
    EquitiesTrades,

    /// <summary>ETF trade information.</summary>
    EtfTrades,

    /// <summary>Index end-of-day values.</summary>
    IndexEod,

    /// <summary>Index intraday files.</summary>
    IndexIntraday,

    /// <summary>Index portfolio composition.</summary>
    IndexComposition,

    /// <summary>Confirmed corporate actions.</summary>
    CorporateActions,

    /// <summary>Active corporate-action life cycle.</summary>
    CorporateActionLifeCycle,

    /// <summary>Corporate-action disclosure schedule.</summary>
    CorporateActionSchedule,

    /// <summary>Corporate-action issuer reference data.</summary>
    CorporateActionIssuers,
}

static class B3Up2DataMessageTypes
{
    public const MessageTypes File =
        (MessageTypes)(-5009);
}

/// <summary>Custom file data types exposed by the connector.</summary>
public static class B3Up2DataDataTypes
{
    /// <summary>Blob catalog.</summary>
    public static readonly DataType BlobCatalog =
        Create(B3Up2DataDataKinds.BlobCatalog, "Blob catalog");

    /// <summary>Equities security master.</summary>
    public static readonly DataType SecurityMaster =
        Create(B3Up2DataDataKinds.SecurityMaster, "Security master");

    /// <summary>Equities EOD prices.</summary>
    public static readonly DataType EquitiesEod =
        Create(B3Up2DataDataKinds.EquitiesEod, "Equities EOD");

    /// <summary>Equities trade information.</summary>
    public static readonly DataType EquitiesTrades =
        Create(B3Up2DataDataKinds.EquitiesTrades, "Equities trades");

    /// <summary>ETF trade information.</summary>
    public static readonly DataType EtfTrades =
        Create(B3Up2DataDataKinds.EtfTrades, "ETF trades");

    /// <summary>Index EOD values.</summary>
    public static readonly DataType IndexEod =
        Create(B3Up2DataDataKinds.IndexEod, "Index EOD");

    /// <summary>Index intraday files.</summary>
    public static readonly DataType IndexIntraday =
        Create(B3Up2DataDataKinds.IndexIntraday, "Index intraday");

    /// <summary>Index portfolio composition.</summary>
    public static readonly DataType IndexComposition =
        Create(
            B3Up2DataDataKinds.IndexComposition,
            "Index composition");

    /// <summary>Confirmed corporate actions.</summary>
    public static readonly DataType CorporateActions =
        Create(
            B3Up2DataDataKinds.CorporateActions,
            "Corporate actions");

    /// <summary>Corporate-action life cycle.</summary>
    public static readonly DataType CorporateActionLifeCycle =
        Create(
            B3Up2DataDataKinds.CorporateActionLifeCycle,
            "Corporate-action life cycle");

    /// <summary>Corporate-action schedule.</summary>
    public static readonly DataType CorporateActionSchedule =
        Create(
            B3Up2DataDataKinds.CorporateActionSchedule,
            "Corporate-action schedule");

    /// <summary>Corporate-action issuer reference data.</summary>
    public static readonly DataType CorporateActionIssuers =
        Create(
            B3Up2DataDataKinds.CorporateActionIssuers,
            "Corporate-action issuers");

    /// <summary>All custom B3 UP2DATA data types.</summary>
    public static IReadOnlyList<DataType> All { get; } =
    [
        BlobCatalog,
        SecurityMaster,
        EquitiesEod,
        EquitiesTrades,
        EtfTrades,
        IndexEod,
        IndexIntraday,
        IndexComposition,
        CorporateActions,
        CorporateActionLifeCycle,
        CorporateActionSchedule,
        CorporateActionIssuers,
    ];

    /// <summary>Get a data type by dataset kind.</summary>
    public static DataType Get(B3Up2DataDataKinds kind)
        => kind switch
        {
            B3Up2DataDataKinds.BlobCatalog => BlobCatalog,
            B3Up2DataDataKinds.SecurityMaster => SecurityMaster,
            B3Up2DataDataKinds.EquitiesEod => EquitiesEod,
            B3Up2DataDataKinds.EquitiesTrades => EquitiesTrades,
            B3Up2DataDataKinds.EtfTrades => EtfTrades,
            B3Up2DataDataKinds.IndexEod => IndexEod,
            B3Up2DataDataKinds.IndexIntraday => IndexIntraday,
            B3Up2DataDataKinds.IndexComposition =>
                IndexComposition,
            B3Up2DataDataKinds.CorporateActions =>
                CorporateActions,
            B3Up2DataDataKinds.CorporateActionLifeCycle =>
                CorporateActionLifeCycle,
            B3Up2DataDataKinds.CorporateActionSchedule =>
                CorporateActionSchedule,
            B3Up2DataDataKinds.CorporateActionIssuers =>
                CorporateActionIssuers,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, null),
        };

    /// <summary>Try to resolve a custom data type.</summary>
    public static bool TryGetKind(
        DataType dataType,
        out B3Up2DataDataKinds kind)
    {
        foreach (var value in Enum.GetValues<B3Up2DataDataKinds>())
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
        B3Up2DataDataKinds kind,
        string name)
        => DataType
            .Create<B3Up2DataFileMessage>(kind, true)
            .SetName(name)
            .Immutable();
}

/// <summary>
/// A B3 UP2DATA Cloud blob or catalog entry.
/// </summary>
public class B3Up2DataFileMessage :
    BaseSubscriptionIdMessage<B3Up2DataFileMessage>,
    IServerTimeMessage
{
    /// <summary>Initializes a new instance.</summary>
    public B3Up2DataFileMessage()
        : base(B3Up2DataMessageTypes.File)
    {
    }

    /// <summary>Dataset kind.</summary>
    public B3Up2DataDataKinds Dataset { get; set; }

    /// <inheritdoc />
    public DateTime ServerTime { get; set; }

    /// <summary>Selected SAS channel name.</summary>
    public string Channel { get; set; }

    /// <summary>Blob name inside the UP2DATA container.</summary>
    public string BlobName { get; set; }

    /// <summary>Blob content type.</summary>
    public string ContentType { get; set; }

    /// <summary>Blob length in bytes.</summary>
    public long? ContentLength { get; set; }

    /// <summary>Blob entity tag.</summary>
    public string ETag { get; set; }

    /// <summary>Decoded CSV, JSON, XML, or TXT content.</summary>
    public string Payload { get; set; }

    /// <inheritdoc />
    public override DataType DataType =>
        B3Up2DataDataTypes.Get(Dataset);

    /// <inheritdoc />
    public override Message Clone()
    {
        var copy = new B3Up2DataFileMessage
        {
            Dataset = Dataset,
            ServerTime = ServerTime,
            Channel = Channel,
            BlobName = BlobName,
            ContentType = ContentType,
            ContentLength = ContentLength,
            ETag = ETag,
            Payload = Payload,
        };
        CopyTo(copy);
        return copy;
    }
}
