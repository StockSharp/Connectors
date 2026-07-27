namespace StockSharp.TradingEconomics;

/// <summary>Message adapter for the Trading Economics Markets REST API.</summary>
[MediaIcon(Media.MediaNames.trading_economics)]
[Doc("topics/api/connectors/stock_market/trading_economics.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.TradingEconomicsKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Europe |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.History |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.News)]
public partial class TradingEconomicsMessageAdapter :
    MessageAdapter,
    ITokenAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Official Trading Economics production API root.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.tradingeconomics.com/");

    /// <summary>Default market suffix for bare tickers.</summary>
    [Display(
        Name = "Default market suffix",
        Description = "Trading Economics suffix appended to bare tickers, for example US.",
        GroupName = "Symbols",
        Order = 2)]
    public string DefaultMarket { get; set; } = "US";

    /// <summary>Country or term used for an empty security lookup.</summary>
    [Display(
        Name = "Default search",
        Description = "Country or search term used when a security lookup has no symbol or name.",
        GroupName = "Filters",
        Order = 3)]
    public string DefaultSearch { get; set; } = "united states";

    /// <summary>Maximum news articles emitted per request.</summary>
    [Display(
        Name = "News limit",
        Description = "Maximum number of Trading Economics news articles emitted per request.",
        GroupName = "Limits",
        Order = 4)]
    public int NewsLimit { get; set; } = 100;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(DefaultMarket), DefaultMarket)
            .Set(nameof(DefaultSearch), DefaultSearch)
            .Set(nameof(NewsLimit), NewsLimit);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        DefaultMarket = storage.GetValue(
            nameof(DefaultMarket), DefaultMarket);
        DefaultSearch = storage.GetValue(
            nameof(DefaultSearch), DefaultSearch);
        NewsLimit = storage.GetValue(
            nameof(NewsLimit), NewsLimit);
    }
}
