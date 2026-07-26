namespace StockSharp.Comdirect;

/// <summary>
/// The message adapter for comdirect REST API.
/// </summary>
[MediaIcon(Media.MediaNames.comdirect)]
[Doc("topics/api/connectors/europe/comdirect.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.ComdirectKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(MessageAdapterCategories.Europe |
    MessageAdapterCategories.Transactions | MessageAdapterCategories.Stock |
    MessageAdapterCategories.Free)]
public partial class ComdirectMessageAdapter : MessageAdapter,
    ILoginPasswordAdapter, IKeySecretAdapter, IAddressAdapter<Uri>
{
    private static readonly Uri _defaultAddress =
        new("https://api.comdirect.de/");

    /// <inheritdoc />
    [Display(
        Name = "OAuth client ID",
        Description = "Client ID issued for the comdirect developer access.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "OAuth client secret",
        Description = "Client secret issued for the comdirect developer access.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "Access number",
        Description = "Eight-digit comdirect access number.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public string Login { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "PIN",
        Description = "Six-digit comdirect access PIN.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString Password { get; set; }

    /// <summary>
    /// TAN procedure used to activate the session.
    /// </summary>
    [Display(
        Name = "TAN procedure",
        Description = "TAN procedure used to activate the comdirect session.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public ComdirectTanTypes TanType { get; set; } =
        ComdirectTanTypes.PhotoTanPush;

    /// <summary>
    /// Callback invoked for a generated TAN challenge. It must return a TAN
    /// for PhotoTAN or mobileTAN. For PhotoTAN Push it can wait until the
    /// user approves the request and return an empty string.
    /// </summary>
    [Browsable(false)]
    public Func<ComdirectTanChallenge, CancellationToken, ValueTask<string>>
        TanProvider
    { get; set; }

    /// <summary>
    /// Interval for polling orders and portfolio data.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Currency used for an order limit when an instrument has not been
    /// requested before order registration.
    /// </summary>
    [Display(
        Name = "Default trading currency",
        Description = "ISO currency used for order limits when instrument metadata is not cached.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public string DefaultCurrency { get; set; } = "EUR";

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 7)]
    public Uri Address { get; set; } = _defaultAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Login), Login)
            .Set(nameof(Password), Password)
            .Set(nameof(TanType), TanType)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(DefaultCurrency), DefaultCurrency)
            .Set(nameof(Address), Address);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Login = storage.GetValue<string>(nameof(Login));
        Password = storage.GetValue<SecureString>(nameof(Password));
        TanType = storage.GetValue(nameof(TanType), TanType);
        PollingInterval = storage.GetValue(
            nameof(PollingInterval), PollingInterval);
        DefaultCurrency = storage.GetValue(
            nameof(DefaultCurrency), DefaultCurrency);
        Address = storage.GetValue(nameof(Address), Address);
    }
}
