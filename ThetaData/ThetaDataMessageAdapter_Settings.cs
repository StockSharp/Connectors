namespace StockSharp.ThetaData;

/// <summary>The message adapter for ThetaData v3 REST and streaming APIs.</summary>
[MediaIcon(Media.MediaNames.thetadata)]
[Doc("topics/api/connectors/stock_market/thetadata.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ThetaDataKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Options |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
public partial class ThetaDataMessageAdapter : MessageAdapter
{
	/// <summary>Theta Terminal REST API v3 base address.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public Uri Address { get; set; } = new("http://127.0.0.1:25503/v3/");

	/// <summary>Theta Terminal streaming WebSocket address.</summary>
	[Display(Name = "WebSocket", Description = "Theta Terminal streaming endpoint.",
		GroupName = "Connection", Order = 1)]
	[BasicSetting]
	public Uri WebSocketAddress { get; set; } =
		new("ws://127.0.0.1:25520/v1/events");

	/// <summary>Stock venue used by REST requests.</summary>
	[Display(Name = "Stock venue",
		Description = "Nasdaq Basic or the merged UTP and CTA SIP feed.",
		GroupName = "Market data", Order = 2)]
	public ThetaDataStockVenues StockVenue { get; set; }

	/// <summary>Time zone used by offset-free ThetaData timestamps.</summary>
	[Display(Name = "Market time zone",
		Description = "System time-zone identifier for US Eastern market timestamps.",
		GroupName = "Market data", Order = 3)]
	public string MarketTimeZoneId { get; set; } = "America/New_York";

	/// <summary>Start of the requested market session.</summary>
	[Display(Name = "Session start",
		Description = "Earliest market time requested from historical intraday endpoints.",
		GroupName = "Market data", Order = 4)]
	public TimeSpan SessionStart { get; set; } = new(9, 30, 0);

	/// <summary>End of the requested market session.</summary>
	[Display(Name = "Session end",
		Description = "Latest market time requested from historical intraday endpoints.",
		GroupName = "Market data", Order = 5)]
	public TimeSpan SessionEnd { get; set; } = new(16, 0, 0);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Address), Address)
			.Set(nameof(WebSocketAddress), WebSocketAddress)
			.Set(nameof(StockVenue), StockVenue)
			.Set(nameof(MarketTimeZoneId), MarketTimeZoneId)
			.Set(nameof(SessionStart), SessionStart)
			.Set(nameof(SessionEnd), SessionEnd);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Address = storage.GetValue(nameof(Address), Address);
		WebSocketAddress = storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
		StockVenue = storage.GetValue(nameof(StockVenue), StockVenue);
		MarketTimeZoneId = storage.GetValue(nameof(MarketTimeZoneId), MarketTimeZoneId);
		SessionStart = storage.GetValue(nameof(SessionStart), SessionStart);
		SessionEnd = storage.GetValue(nameof(SessionEnd), SessionEnd);
	}
}
