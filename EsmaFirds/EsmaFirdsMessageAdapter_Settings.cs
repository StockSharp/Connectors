namespace StockSharp.EsmaFirds;

/// <summary>
/// Message adapter for the public ESMA FIRDS instrument-reference API.
/// </summary>
[MediaIcon(Media.MediaNames.esma_firds)]
[Doc("topics/api/connectors/stock_market/esma_firds.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.EsmaFirdsKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Europe |
    MessageAdapterCategories.Stock)]
public partial class EsmaFirdsMessageAdapter :
    MessageAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Official ESMA registers API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://registers.esma.europa.eu/");

    /// <summary>
    /// Comma-separated first letters of included ISO 10962 CFI categories.
    /// </summary>
    [Display(
        Name = "CFI categories",
        Description = "Comma-separated ISO 10962 CFI category letters. E and C select equities and collective investment vehicles.",
        GroupName = "Filters",
        Order = 1)]
    public string CfiCategories { get; set; } = "E,C";

    /// <summary>
    /// Whether instruments with a past termination date are excluded.
    /// </summary>
    [Display(
        Name = "Active instruments only",
        Description = "Exclude instruments whose trading termination date is in the past.",
        GroupName = "Filters",
        Order = 2)]
    public bool ActiveOnly { get; set; } = true;

    /// <summary>Maximum records returned by one lookup.</summary>
    [Display(
        Name = "Maximum results",
        Description = "Maximum number of FIRDS instrument records returned by one lookup.",
        GroupName = "Limits",
        Order = 3)]
    public int MaxResults { get; set; } = 100;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Address), Address)
            .Set(nameof(CfiCategories), CfiCategories)
            .Set(nameof(ActiveOnly), ActiveOnly)
            .Set(nameof(MaxResults), MaxResults);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Address = storage.GetValue(nameof(Address), Address);
        CfiCategories = storage.GetValue(
            nameof(CfiCategories), CfiCategories);
        ActiveOnly = storage.GetValue(
            nameof(ActiveOnly), ActiveOnly);
        MaxResults = storage.GetValue(
            nameof(MaxResults), MaxResults);
    }
}
