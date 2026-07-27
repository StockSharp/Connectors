namespace StockSharp.SetMarketData;

/// <summary>
/// Message adapter for the Stock Exchange of Thailand Market Data API.
/// </summary>
[MediaIcon(Media.MediaNames.set_market_data)]
[Doc("topics/api/connectors/stock_market/set_market_data.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.SetMarketDataKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.StockExchangeofThailandKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth)]
public partial class SetMarketDataMessageAdapter :
    MessageAdapter,
    ITokenAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = "API key created in SET Smart Marketplace.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Official SET Market Data API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri Address { get; set; } =
        new("https://marketplace.set.or.th/api/public/");

    /// <summary>Real-time or delayed market-data product.</summary>
    [Display(
        Name = "Data mode",
        Description = "Licensed real-time or delayed SET Market Data API product.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 2)]
    public SetMarketDataModes DataMode { get; set; } =
        SetMarketDataModes.RealTime;

    /// <summary>Comma-separated SET and mai market filters.</summary>
    [Display(
        Name = "Markets",
        Description = "Comma-separated market filters: SET and mai.",
        GroupName = LocalizedStrings.SecuritiesKey,
        Order = 3)]
    public string Markets { get; set; } = "SET,mai";

    /// <summary>Optional comma-separated index, industry, and sector filters.</summary>
    [Display(
        Name = "Index and sector filters",
        Description = "Optional comma-separated index, industry, or sector codes such as SET50,BANK.",
        GroupName = LocalizedStrings.SecuritiesKey,
        Order = 4)]
    public string IndexSectors { get; set; }

    /// <summary>Comma-separated security-type filters.</summary>
    [Display(
        Name = "Security types",
        Description = "Comma-separated SET security-type codes used by security lookup.",
        GroupName = LocalizedStrings.SecuritiesKey,
        Order = 5)]
    public string SecurityTypeCodes { get; set; } =
        "CS,CSF,PS,PSF,ETF,DR,UT";

    /// <summary>Whether odd-lot quotations are requested.</summary>
    [Display(
        Name = "Include odd lots",
        Description = "Request both main-board and odd-lot stock quotations.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 6)]
    public bool IncludeOddLots { get; set; }

    /// <summary>Whether index securities are included in lookup.</summary>
    [Display(
        Name = "Include indices",
        Description = "Include SET and mai indices in security lookup.",
        GroupName = LocalizedStrings.SecuritiesKey,
        Order = 7)]
    public bool IncludeIndices { get; set; } = true;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(DataMode), DataMode)
            .Set(nameof(Markets), Markets)
            .Set(nameof(IndexSectors), IndexSectors)
            .Set(nameof(SecurityTypeCodes), SecurityTypeCodes)
            .Set(nameof(IncludeOddLots), IncludeOddLots)
            .Set(nameof(IncludeIndices), IncludeIndices);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        DataMode = storage.GetValue(nameof(DataMode), DataMode);
        Markets = storage.GetValue(nameof(Markets), Markets);
        IndexSectors = storage.GetValue(
            nameof(IndexSectors), IndexSectors);
        SecurityTypeCodes = storage.GetValue(
            nameof(SecurityTypeCodes), SecurityTypeCodes);
        IncludeOddLots = storage.GetValue(
            nameof(IncludeOddLots), IncludeOddLots);
        IncludeIndices = storage.GetValue(
            nameof(IncludeIndices), IncludeIndices);
    }
}
