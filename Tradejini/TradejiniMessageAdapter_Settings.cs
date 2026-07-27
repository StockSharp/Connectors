namespace StockSharp.Tradejini;

/// <summary>
/// The message adapter for Tradejini API v2.
/// </summary>
[MediaIcon(Media.MediaNames.tradejini)]
[Doc("topics/api/connectors/stock_market/tradejini.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.TradejiniKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options |
    MessageAdapterCategories.FX |
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(TradejiniOrderCondition))]
public partial class TradejiniMessageAdapter : MessageAdapter, ITokenAdapter
{
    private static readonly Uri _defaultAddress =
        new("https://api.tradejini.com/v2/");

    /// <summary>
    /// API key created for an individual application in the Tradejini
    /// developer portal.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ApiKeyKey,
        Description = LocalizedStrings.ApiKeyForATradejiniIndividualAppTheAppRequiresAWhitelistedStaticIpDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString ApiKey { get; set; }

    /// <summary>
    /// Tradejini account password used by the individual-token flow.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PasswordKey,
        Description = LocalizedStrings.TradejiniAccountPasswordUsedToObtainA24HourAccessTokenDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Password { get; set; }

    /// <summary>
    /// Current OTP or TOTP code used by the individual-token flow.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TwoFactorCodeKey,
        Description = LocalizedStrings.CurrentSmsEmailOtpOrAuthenticatorTotpUsedToObtainAnAccessTokenDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString TwoFactorCode { get; set; }

    /// <summary>
    /// Type of the supplied two-factor code.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TwoFactorTypeKey,
        Description = LocalizedStrings.SelectOtpForAnSmsEmailCodeOrTotpForAnAuthenticatorCodeDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public TradejiniTwoFactorTypes TwoFactorType { get; set; } =
        TradejiniTwoFactorTypes.Totp;

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccessTokenKey,
        Description = LocalizedStrings.TradejiniAccessTokenTokensAreValidFor24HoursWhenSuppliedPasswordAnd2faAreNotUsedDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>
    /// Portfolio name emitted by the connector.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PortfolioNameLabelKey,
        Description = LocalizedStrings.PortfolioNameWhenEmptyTheTradejiniUserIdentifierIsUsedDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public string PortfolioName { get; set; }

    /// <summary>
    /// Default product used for new orders.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultProductKey,
        Description = LocalizedStrings.DefaultTradejiniOrderProductDescKey,
        GroupName = LocalizedStrings.OrderKey,
        Order = 6)]
    public TradejiniProducts DefaultProduct { get; set; } =
        TradejiniProducts.Delivery;

    /// <summary>
    /// REST API root address.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RestAddressKey,
        Description = LocalizedStrings.TradejiniApiV2RestRootAddressDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 7)]
    public Uri Address { get; set; } = _defaultAddress;

    /// <summary>
    /// Interval for order, position, holding, and funds snapshots.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 8)]
    public TimeSpan PollingInterval { get; set; } =
        TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(ApiKey), ApiKey)
            .Set(nameof(Password), Password)
            .Set(nameof(TwoFactorCode), TwoFactorCode)
            .Set(nameof(TwoFactorType), TwoFactorType)
            .Set(nameof(Token), Token)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(DefaultProduct), DefaultProduct)
            .Set(nameof(Address), Address)
            .Set(nameof(PollingInterval), PollingInterval);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
        Password = storage.GetValue<SecureString>(nameof(Password));
        TwoFactorCode =
            storage.GetValue<SecureString>(nameof(TwoFactorCode));
        TwoFactorType =
            storage.GetValue(nameof(TwoFactorType), TwoFactorType);
        Token = storage.GetValue<SecureString>(nameof(Token));
        PortfolioName = storage.GetValue<string>(nameof(PortfolioName));
        DefaultProduct =
            storage.GetValue(nameof(DefaultProduct), DefaultProduct);
        Address = storage.GetValue(nameof(Address), Address);
        PollingInterval =
            storage.GetValue(nameof(PollingInterval), PollingInterval);
    }
}
