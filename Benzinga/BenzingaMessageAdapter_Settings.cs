namespace StockSharp.Benzinga;

/// <summary>The message adapter for Benzinga market data and real-time news APIs.</summary>
[MediaIcon(Media.MediaNames.benzinga)]
[Doc("topics/api/connectors/stock_market/benzinga.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BenzingaKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Paid |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.News)]
public partial class BenzingaMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
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

	/// <summary>Benzinga REST API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.benzinga.com/");

	/// <summary>Official Benzinga real-time news stream address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NewsWebSocketAddressKey,
		Description = LocalizedStrings.OfficialBenzingaRealTimeNewsStreamAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public Uri WebSocketAddress { get; set; } =
		new("wss://api.benzinga.com/api/v1/news/stream");

	/// <summary>Trading session requested from the Bars API.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BarsSessionKey,
		Description = LocalizedStrings.TradingSessionRequestedFromTheHistoricalBarsApiDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 3)]
	public BenzingaSessions BarsSession { get; set; } = BenzingaSessions.Any;

	/// <summary>Optional comma-separated Benzinga news channel filter.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NewsChannelsKey,
		Description = LocalizedStrings.OptionalCommaSeparatedChannelFilterForRestAndStreamingNewsDescKey,
		GroupName = LocalizedStrings.NewsKey,
		Order = 4)]
	public string NewsChannels { get; set; }

	/// <summary>Maximum number of news items returned by one history request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NewsLimitKey,
		Description = LocalizedStrings.MaximumNumberOfItemsReturnedByOneRestNewsRequestDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 5)]
	public int MaxNewsItems { get; set; } = 1000;

	/// <summary>Maximum number of candles emitted by one Bars API request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BarsLimitKey,
		Description = LocalizedStrings.MaximumNumberOfCandlesEmittedByOneHistoricalBarsRequestDescKey,
		GroupName = LocalizedStrings.LimitsKey,
		Order = 6)]
	public int MaxBars { get; set; } = 10000;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(WebSocketAddress), WebSocketAddress)
			.Set(nameof(BarsSession), BarsSession)
			.Set(nameof(NewsChannels), NewsChannels)
			.Set(nameof(MaxNewsItems), MaxNewsItems)
			.Set(nameof(MaxBars), MaxBars);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		WebSocketAddress = storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
		BarsSession = storage.GetValue(nameof(BarsSession), BarsSession);
		NewsChannels = storage.GetValue<string>(nameof(NewsChannels));
		MaxNewsItems = storage.GetValue(nameof(MaxNewsItems), MaxNewsItems);
		MaxBars = storage.GetValue(nameof(MaxBars), MaxBars);
	}
}
