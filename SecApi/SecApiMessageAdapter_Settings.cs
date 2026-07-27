namespace StockSharp.SecApi;

/// <summary>Message adapter for the SEC-API.io REST APIs.</summary>
[MediaIcon(Media.MediaNames.sec_api)]
[Doc("topics/api/connectors/stock_market/sec_api.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.SecApiKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.History |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.News)]
public partial class SecApiMessageAdapter :
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
        Description = "Official SEC-API.io production API root.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.sec-api.io/");

    /// <summary>Whether delisted securities are excluded from lookup.</summary>
    [Display(
        Name = "Active securities only",
        Description = "Exclude mappings marked as delisted.",
        GroupName = "Filters",
        Order = 2)]
    public bool ActiveOnly { get; set; } = true;

    /// <summary>Default exchange for an empty security lookup.</summary>
    [Display(
        Name = "Default exchange",
        Description = "SEC-API.io exchange name used for an empty security lookup.",
        GroupName = "Filters",
        Order = 3)]
    public string DefaultExchange { get; set; } = "NASDAQ";

    /// <summary>Default EDGAR form types.</summary>
    [Display(
        Name = "Form types",
        Description = "Comma-separated EDGAR form types used for filing and news requests.",
        GroupName = "Filters",
        Order = 4)]
    public string FormTypes { get; set; } =
        "10-K,10-Q,8-K,6-K,20-F,40-F";

    /// <summary>Maximum records requested from an API search.</summary>
    [Display(
        Name = "Result limit",
        Description = "Maximum records requested per SEC-API.io search; the API permits up to 50.",
        GroupName = "Limits",
        Order = 5)]
    public int ResultLimit { get; set; } = 50;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(ActiveOnly), ActiveOnly)
            .Set(nameof(DefaultExchange), DefaultExchange)
            .Set(nameof(FormTypes), FormTypes)
            .Set(nameof(ResultLimit), ResultLimit);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        ActiveOnly = storage.GetValue(
            nameof(ActiveOnly), ActiveOnly);
        DefaultExchange = storage.GetValue(
            nameof(DefaultExchange), DefaultExchange);
        FormTypes = storage.GetValue(
            nameof(FormTypes), FormTypes);
        ResultLimit = storage.GetValue(
            nameof(ResultLimit), ResultLimit);
    }
}
