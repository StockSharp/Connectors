namespace StockSharp.Finage;

/// <summary>
/// Message adapter for Finage Forex REST and WebSocket APIs.
/// </summary>
[MediaIcon(Media.MediaNames.finage)]
[Doc("topics/api/connectors/forex/finage.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FinageKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.FX |
	MessageAdapterCategories.History |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles)]
public partial class FinageMessageAdapter : MessageAdapter
{
	private static readonly Uri _defaultRestEndpoint =
		new("https://api.finage.co.uk/");
	private static readonly Uri _defaultStreamingEndpoint =
		new("wss://socket.finage.ws:8080/");

	/// <summary>Finage REST API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ApiKeyKey,
		Description = LocalizedStrings.FinageApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <summary>Finage WebSocket socket key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.FinageStreamingTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public SecureString StreamingToken { get; set; }

	/// <summary>Finage REST API root endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.FinageRestEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public Uri RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Finage WebSocket server endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingEndpointKey,
		Description = LocalizedStrings.FinageStreamingEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public Uri StreamingEndpoint { get; set; } =
		_defaultStreamingEndpoint;

	private TimeSpan _requestInterval =
		TimeSpan.FromMilliseconds(500);

	/// <summary>Minimum interval between REST calls.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public TimeSpan RequestInterval
	{
		get => _requestInterval;
		set => _requestInterval =
			value >= TimeSpan.Zero &&
			value <= TimeSpan.FromMinutes(1)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value),
					value, "Finage request interval must be between " +
						"zero and one minute.");
	}

	/// <summary>Optional explicit symbol list.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SymbolsKey,
		Description = LocalizedStrings.FinageSymbolsDescKey,
		GroupName = LocalizedStrings.FiltersKey,
		Order = 0)]
	public string Symbols { get; set; }

	private int _maximumSecurities = 10000;

	/// <summary>Maximum securities returned by a lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.FinageMaximumSecuritiesDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 0)]
	public int MaximumSecurities
	{
		get => _maximumSecurities;
		set => _maximumSecurities = value is >= 1 and <= 100000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value),
				value, "Maximum security count must be between 1 " +
					"and 100000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(StreamingToken), StreamingToken)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(StreamingEndpoint), StreamingEndpoint)
			.Set(nameof(RequestInterval), RequestInterval)
			.Set(nameof(Symbols), Symbols)
			.Set(nameof(MaximumSecurities), MaximumSecurities);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		StreamingToken = storage.GetValue<SecureString>(
			nameof(StreamingToken));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint),
			_defaultRestEndpoint);
		StreamingEndpoint = storage.GetValue(
			nameof(StreamingEndpoint), _defaultStreamingEndpoint);
		RequestInterval = storage.GetValue(nameof(RequestInterval),
			TimeSpan.FromMilliseconds(500));
		Symbols = storage.GetValue<string>(nameof(Symbols));
		MaximumSecurities = storage.GetValue(
			nameof(MaximumSecurities), 10000);
	}
}
