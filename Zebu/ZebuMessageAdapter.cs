namespace StockSharp.Zebu;

using StockSharp.Zebu.Native;

/// <summary>The message adapter for Zebu MYNT OAuth API.</summary>
[MediaIcon(Media.MediaNames.zebu)]
[Doc("topics/api/connectors/stock_market/zebu.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.ZebuKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
    MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
    MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(ZebuOrderCondition))]
public class ZebuMessageAdapter : NorenMessageAdapter, IKeySecretAdapter
{
    private static readonly Uri _defaultAuthorizationAddress =
        new("https://go.mynt.in/OAuthlogin/authorize/oauth");
    private const string _defaultRestEndpoint =
        "https://go.mynt.in/NorenWClientAPI/";
    private const string _defaultInstrumentEndpointTemplate =
        "https://go.mynt.in/{0}_symbols.txt.zip";
    private const string _defaultWebSocketEndpoint =
        "wss://go.mynt.in/NorenWSAPI/";

    /// <summary>
    /// Initializes a new instance of the <see cref="ZebuMessageAdapter"/>.
    /// </summary>
    public ZebuMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        RestEndpoint = _defaultRestEndpoint;
        InstrumentEndpointTemplate = _defaultInstrumentEndpointTemplate;
        WebSocketEndpoint = _defaultWebSocketEndpoint;
    }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.OAuthClientIdKey,
        Description = LocalizedStrings.ClientIdGeneratedInTheZebuMyntApiSettingsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.OAuthClientSecretKey,
        Description = LocalizedStrings.ClientSecretGeneratedInTheZebuMyntApiSettingsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>Authorization code returned to the registered callback.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AuthorizationCodeKey,
        Description = LocalizedStrings.OneTimeCodeReturnedByTheZebuOAuthAuthorizationRedirectDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString AuthorizationCode { get; set; }

    /// <summary>OAuth refresh token.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RefreshTokenKey,
        Description = LocalizedStrings.RefreshTokenUsedToObtainANewZebuAccessTokenDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public SecureString RefreshToken { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccessTokenKey,
        Description = LocalizedStrings.OAuthBearerAccessTokenItIsPopulatedAfterCodeExchangeOrRefreshDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public new SecureString Token
    {
        get => base.Token;
        set => base.Token = value;
    }

    /// <summary>Zebu user identifier.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.UserIdLabelKey,
        Description = LocalizedStrings.ZebuClientCodeOAuthNormallyPopulatesItAutomaticallyDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public new string UserId
    {
        get => base.UserId;
        set => base.UserId = value;
    }

    /// <summary>Zebu trading account identifier.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountIdKey,
        Description = LocalizedStrings.TradingAccountIdWhenEmptyUserIdIsUsedDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public new string AccountId
    {
        get => base.AccountId;
        set => base.AccountId = value;
    }

    /// <summary>Expiration time of the current OAuth access token.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenExpiresAtKey,
        Description = LocalizedStrings.UtcExpirationTimeReportedByTheLatestOAuthTokenResponseDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>Default MYNT order product.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultProductKey,
        Description = LocalizedStrings.DefaultMyntProductUsedForNewZebuOrdersDescKey,
        GroupName = LocalizedStrings.OrderKey,
        Order = 8)]
    public new NorenProducts DefaultProduct
    {
        get => base.DefaultProduct;
        set => base.DefaultProduct = value;
    }

    /// <summary>Maximum number of WebSocket reconnect attempts.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ReconnectAttemptsLabelKey,
        Description = LocalizedStrings.MaximumNumberOfAttemptsToReconnectTheZebuWebSocketDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 9)]
    public new int ReconnectAttempts
    {
        get => base.ReconnectAttempts;
        set => base.ReconnectAttempts = value;
    }

    /// <summary>OAuth authorization page.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AuthorizationAddressKey,
        Description = LocalizedStrings.ZebuOAuthPageWhereTheUserAuthorizesTheApplicationDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 10)]
    public Uri AuthorizationAddress { get; set; } =
        _defaultAuthorizationAddress;

    /// <summary>REST API endpoint.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RestEndpointKey,
        Description = LocalizedStrings.ZebuMyntOAuthRestEndpointDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 11)]
    public new string RestEndpoint
    {
        get => base.RestEndpoint;
        set => base.RestEndpoint = value;
    }

    /// <summary>Instrument archive endpoint template.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.InstrumentEndpointTemplateKey,
        Description = LocalizedStrings.ZebuExchangeMasterZipEndpointTemplateDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 12)]
    public new string InstrumentEndpointTemplate
    {
        get => base.InstrumentEndpointTemplate;
        set => base.InstrumentEndpointTemplate = value;
    }

    /// <summary>WebSocket endpoint.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketEndpointKey,
        Description = LocalizedStrings.ZebuMyntOAuthWebSocketEndpointDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 13)]
    public new string WebSocketEndpoint
    {
        get => base.WebSocketEndpoint;
        set => base.WebSocketEndpoint = value;
    }

    /// <summary>Create the OAuth page URL for the configured client ID.</summary>
    public Uri CreateAuthorizationUri()
    {
        var clientId = Key.ThrowIfEmpty(nameof(Key)).UnSecure();
        var builder = new UriBuilder(
            AuthorizationAddress ??
            throw new InvalidOperationException(
                "Zebu authorization address is not configured."));
        builder.Query = $"client_id={Uri.EscapeDataString(clientId)}";
        return builder.Uri;
    }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(AuthorizationCode), AuthorizationCode)
            .Set(nameof(RefreshToken), RefreshToken)
            .Set(nameof(TokenExpiresAt), TokenExpiresAt)
            .Set(nameof(AuthorizationAddress), AuthorizationAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        AuthorizationCode =
            storage.GetValue<SecureString>(nameof(AuthorizationCode));
        RefreshToken =
            storage.GetValue<SecureString>(nameof(RefreshToken));
        TokenExpiresAt =
            storage.GetValue<DateTime?>(nameof(TokenExpiresAt));
        AuthorizationAddress = storage.GetValue(
            nameof(AuthorizationAddress),
            AuthorizationAddress);
    }

    /// <inheritdoc />
    protected override bool IsBearerAuthentication => true;

    /// <inheritdoc />
    protected override NorenOrderCondition CreateOrderCondition()
        => new ZebuOrderCondition();

    /// <inheritdoc />
    protected override async ValueTask PrepareConnectionAsync(
        CancellationToken cancellationToken)
    {
        var expiresSoon = TokenExpiresAt is { } expiresAt &&
            expiresAt.ToUniversalTime() <= DateTime.UtcNow.AddMinutes(1);
        if (!Token.IsEmpty() && !expiresSoon)
            return;

        if (expiresSoon &&
            RefreshToken.IsEmpty() &&
            AuthorizationCode.IsEmpty())
        {
            throw new InvalidOperationException(
                "The Zebu access token has expired and no refresh token or " +
                "authorization code is configured.");
        }

        var restAddress = new Uri(
            RestEndpoint.ThrowIfEmpty(nameof(RestEndpoint)));
        using var oauth = new ZebuOAuthClient(restAddress)
        {
            Parent = this,
        };
        ZebuOAuthToken result;
        if (!RefreshToken.IsEmpty())
        {
            result = await oauth.Refresh(
                RefreshToken,
                cancellationToken);
        }
        else
        {
            result = await oauth.ExchangeCode(
                Key,
                Secret,
                AuthorizationCode,
                cancellationToken);
        }

        Token = result.AccessToken.Secure();
        if (!result.RefreshToken.IsEmpty())
            RefreshToken = result.RefreshToken.Secure();
        UserId = UserId.IsEmpty(result.UserId);
        AccountId = AccountId.IsEmpty(result.AccountId).IsEmpty(UserId);
        TokenExpiresAt = result.ExpiresIn > 0
            ? DateTime.UtcNow.AddSeconds(result.ExpiresIn)
            : null;
    }
}
