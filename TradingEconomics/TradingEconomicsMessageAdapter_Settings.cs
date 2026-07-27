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
        Description = LocalizedStrings.OfficialTradingEconomicsProductionApiRootDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.tradingeconomics.com/");

    /// <summary>Default market suffix for bare tickers.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultMarketSuffixKey,
        Description = LocalizedStrings.TradingEconomicsSuffixAppendedToBareTickersForExampleUsDescKey,
        GroupName = LocalizedStrings.SymbolsKey,
        Order = 2)]
    public string DefaultMarket { get; set; } = "US";

    /// <summary>Country or term used for an empty security lookup.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultSearchKey,
        Description = LocalizedStrings.CountryOrSearchTermUsedWhenASecurityLookupHasNoSymbolOrNameDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 3)]
    public string DefaultSearch { get; set; } = "united states";

    /// <summary>Maximum news articles emitted per request.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.NewsLimitKey,
        Description = LocalizedStrings.MaximumNumberOfTradingEconomicsNewsArticlesEmittedPerRequestDescKey,
        GroupName = LocalizedStrings.LimitsKey,
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
