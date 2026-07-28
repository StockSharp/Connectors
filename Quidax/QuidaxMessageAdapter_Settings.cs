namespace StockSharp.Quidax;

/// <summary>
/// The message adapter for the Quidax spot exchange.
/// </summary>
[MediaIcon(Media.MediaNames.quidax)]
[Doc("topics/api/connectors/crypto_exchanges/quidax.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QuidaxKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class QuidaxMessageAdapter : MessageAdapter,
	ITokenAdapter
{
	private const string _defaultRestEndpoint =
		"https://openapi.quidax.io/exchange-open-api/api/v1";
	private const string _defaultUserId = "me";
	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> QuidaxExtensions.TimeFrames;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Quidax user identifier. Use <c>me</c> for the token owner.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UserIdKey,
		Description = LocalizedStrings.UserIdKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string UserId { get; set; } = _defaultUserId;

	/// <summary>
	/// REST API root endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Interval between REST market and account refreshes.
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
			.Set(nameof(Token), Token)
			.Set(nameof(UserId), UserId)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		UserId = storage.GetValue(
			nameof(UserId), UserId ?? _defaultUserId);
		RestEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(RestEndpoint), RestEndpoint));
		PollingInterval = storage.GetValue(
			nameof(PollingInterval), PollingInterval);
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.IsEmpty()
			? _defaultRestEndpoint
			: endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Token={Token.ToId()}";
}
