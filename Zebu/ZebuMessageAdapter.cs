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
public class ZebuMessageAdapter : ShoonyaMessageAdapter, IKeySecretAdapter
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
        Name = "OAuth client ID",
        Description = "Client ID generated in the Zebu MYNT API settings.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "OAuth client secret",
        Description = "Client secret generated in the Zebu MYNT API settings.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>Authorization code returned to the registered callback.</summary>
    [Display(
        Name = "Authorization code",
        Description = "One-time code returned by the Zebu OAuth authorization redirect.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString AuthorizationCode { get; set; }

    /// <summary>OAuth refresh token.</summary>
    [Display(
        Name = "Refresh token",
        Description = "Refresh token used to obtain a new Zebu access token.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public SecureString RefreshToken { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "Access token",
        Description = "OAuth Bearer access token. It is populated after code exchange or refresh.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public new SecureString Token
    {
        get => base.Token;
        set => base.Token = value;
    }

    /// <summary>Zebu user identifier.</summary>
    [Display(
        Name = "User ID",
        Description = "Zebu client code. OAuth normally populates it automatically.",
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
        Name = "Account ID",
        Description = "Trading account ID. When empty, User ID is used.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public new string AccountId
    {
        get => base.AccountId;
        set => base.AccountId = value;
    }

    /// <summary>Expiration time of the current OAuth access token.</summary>
    [Display(
        Name = "Token expires at",
        Description = "UTC expiration time reported by the latest OAuth token response.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>Default MYNT order product.</summary>
    [Display(
        Name = "Default product",
        Description = "Default MYNT product used for new Zebu orders.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 8)]
    public new ShoonyaProducts DefaultProduct
    {
        get => base.DefaultProduct;
        set => base.DefaultProduct = value;
    }

    /// <summary>Maximum number of WebSocket reconnect attempts.</summary>
    [Display(
        Name = "Reconnect attempts",
        Description = "Maximum number of attempts to reconnect the Zebu WebSocket.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 9)]
    public new int ReconnectAttempts
    {
        get => base.ReconnectAttempts;
        set => base.ReconnectAttempts = value;
    }

    /// <summary>OAuth authorization page.</summary>
    [Display(
        Name = "Authorization address",
        Description = "Zebu OAuth page where the user authorizes the application.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 10)]
    public Uri AuthorizationAddress { get; set; } =
        _defaultAuthorizationAddress;

    /// <summary>REST API endpoint.</summary>
    [Display(
        Name = "REST endpoint",
        Description = "Zebu MYNT OAuth REST endpoint.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 11)]
    public new string RestEndpoint
    {
        get => base.RestEndpoint;
        set => base.RestEndpoint = value;
    }

    /// <summary>Instrument archive endpoint template.</summary>
    [Display(
        Name = "Instrument endpoint template",
        Description = "Zebu exchange master ZIP endpoint template.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 12)]
    public new string InstrumentEndpointTemplate
    {
        get => base.InstrumentEndpointTemplate;
        set => base.InstrumentEndpointTemplate = value;
    }

    /// <summary>WebSocket endpoint.</summary>
    [Display(
        Name = "WebSocket endpoint",
        Description = "Zebu MYNT OAuth WebSocket endpoint.",
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
