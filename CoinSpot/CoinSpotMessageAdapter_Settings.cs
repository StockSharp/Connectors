namespace StockSharp.CoinSpot;

/// <summary>
/// The message adapter for the CoinSpot spot exchange and broker.
/// </summary>
[MediaIcon(Media.MediaNames.coinspot)]
[Doc("topics/api/connectors/crypto_exchanges/coinspot.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinSpotKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class CoinSpotMessageAdapter : MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultPublicEndpoint =
		"https://www.coinspot.com.au/pubapi/v2";
	private const string _defaultTradingEndpoint =
		"https://www.coinspot.com.au/api/v2";
	private const string _defaultReadOnlyEndpoint =
		"https://www.coinspot.com.au/api/v2/ro";
	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// Public REST API root endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PublicKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string PublicEndpoint { get; set; } =
		_defaultPublicEndpoint;

	/// <summary>
	/// Trading REST API root endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string TradingEndpoint { get; set; } =
		_defaultTradingEndpoint;

	/// <summary>
	/// Read-only private REST API root endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string ReadOnlyEndpoint { get; set; } =
		_defaultReadOnlyEndpoint;

	/// <summary>
	/// Interval between REST refreshes.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	[BasicSetting]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value;
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(PublicEndpoint), PublicEndpoint)
			.Set(nameof(TradingEndpoint), TradingEndpoint)
			.Set(nameof(ReadOnlyEndpoint), ReadOnlyEndpoint)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		PublicEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(PublicEndpoint), PublicEndpoint),
			_defaultPublicEndpoint);
		TradingEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(TradingEndpoint), TradingEndpoint),
			_defaultTradingEndpoint);
		ReadOnlyEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(ReadOnlyEndpoint), ReadOnlyEndpoint),
			_defaultReadOnlyEndpoint);
		PollingInterval = storage.GetValue(
			nameof(PollingInterval), PollingInterval);
		if (PollingInterval <= TimeSpan.Zero)
			PollingInterval = TimeSpan.FromSeconds(5);
	}

	private static string NormalizeEndpoint(
		string endpoint,
		string fallback)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Key={Key.ToId()}";
}
