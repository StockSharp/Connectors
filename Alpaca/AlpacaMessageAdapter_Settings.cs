namespace StockSharp.Alpaca;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum AlpacaSections
{
	/// <summary>
	/// Stock.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StockKey)]
	Stock,

	/// <summary>
	/// Crypto.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CryptoKey)]
	Crypto,
}

/// <summary>
/// The message adapter for <see cref="Alpaca"/>.
/// </summary>
[MediaIcon(Media.MediaNames.alpaca)]
[Doc("topics/api/connectors/stock_market/alpaca.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AlpacaKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Transactions | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Ticks)]
[OrderCondition(typeof(AlpacaOrderCondition))]
public partial class AlpacaMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter
{
	private const string _defaultTradingRestEndpoint = "https://api.alpaca.markets";
	private const string _defaultDemoTradingRestEndpoint = "https://paper-api.alpaca.markets";
	private const string _defaultMarketDataRestEndpoint = "https://data.alpaca.markets";
	private const string _defaultTradingWebSocketEndpoint = "wss://api.alpaca.markets/stream";
	private const string _defaultDemoTradingWebSocketEndpoint = "wss://paper-api.alpaca.markets/stream";
	private const string _defaultMarketDataWebSocketEndpoint = "wss://stream.data.alpaca.markets";

	private class StockFeedSource : ItemsSourceBase<string>
	{
		public StockFeedSource()
			: base(new[] { "sip", "iex", "otc" })
		{
		}
	}

	private class CryptoLocationSource : ItemsSourceBase<string>
	{
		public CryptoLocationSource()
			: base(new[] { "us" })
		{
		}
	}

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey + LocalizedStrings.Dot,
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

	private bool _isDemo;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo
	{
		get => _isDemo;
		set
		{
			_isDemo = value;

			if (value)
				StockFeed = "iex";

			OnPropertyChanged();
		}
	}

	private string _stockFeed = "sip";

	/// <summary>
	/// Data for the stock market.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StockKey,
		Description = LocalizedStrings.StockDataKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	[ItemsSource(typeof(StockFeedSource), IsEditable = true)]
	public string StockFeed
	{
		get => _stockFeed;
		set
		{
			_stockFeed = value;
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// Data for the stock market.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CryptoKey,
		Description = LocalizedStrings.SourceKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	[ItemsSource(typeof(CryptoLocationSource), IsEditable = true)]
	public string CryptoLocation { get; set; } = "us";

	/// <summary>
	/// Sections.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[ItemsSource(typeof(AlpacaSections))]
	[BasicSetting]
	public IEnumerable<AlpacaSections> Sections { get; set; } = Enumerator.GetValues<AlpacaSections>();

	/// <summary>Production trading REST endpoint.</summary>
	[Display(
		Name = "Trading REST endpoint",
		Description = "Production trading REST endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string TradingRestEndpoint { get; set; } = _defaultTradingRestEndpoint;

	/// <summary>Demo trading REST endpoint.</summary>
	[Display(
		Name = "Demo trading REST endpoint",
		Description = "Demo trading REST endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DemoTradingRestEndpoint { get; set; } = _defaultDemoTradingRestEndpoint;

	/// <summary>Market data REST endpoint.</summary>
	[Display(
		Name = "Market data REST endpoint",
		Description = "Market data REST endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string MarketDataRestEndpoint { get; set; } = _defaultMarketDataRestEndpoint;

	/// <summary>Production trading WebSocket endpoint.</summary>
	[Display(
		Name = "Trading WebSocket endpoint",
		Description = "Production trading WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string TradingWebSocketEndpoint { get; set; } = _defaultTradingWebSocketEndpoint;

	/// <summary>Demo trading WebSocket endpoint.</summary>
	[Display(
		Name = "Demo trading WebSocket endpoint",
		Description = "Demo trading WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DemoTradingWebSocketEndpoint { get; set; } = _defaultDemoTradingWebSocketEndpoint;

	/// <summary>Market data WebSocket endpoint.</summary>
	[Display(
		Name = "Market data WebSocket endpoint",
		Description = "Base market data WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string MarketDataWebSocketEndpoint { get; set; } = _defaultMarketDataWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(StockFeed), StockFeed)
			.Set(nameof(CryptoLocation), CryptoLocation)
			.Set(nameof(Sections), Sections.Select(s => s.To<string>()).JoinComma())
			.Set(nameof(TradingRestEndpoint), TradingRestEndpoint)
			.Set(nameof(DemoTradingRestEndpoint), DemoTradingRestEndpoint)
			.Set(nameof(MarketDataRestEndpoint), MarketDataRestEndpoint)
			.Set(nameof(TradingWebSocketEndpoint), TradingWebSocketEndpoint)
			.Set(nameof(DemoTradingWebSocketEndpoint), DemoTradingWebSocketEndpoint)
			.Set(nameof(MarketDataWebSocketEndpoint), MarketDataWebSocketEndpoint)
			;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		StockFeed = storage.GetValue<string>(nameof(StockFeed));
		CryptoLocation = storage.GetValue<string>(nameof(CryptoLocation));
		Sections = storage.GetValue<string>(nameof(Sections)).SplitByComma().Select(s => s.To<AlpacaSections>()).ToArray();
		TradingRestEndpoint = storage.GetValue(nameof(TradingRestEndpoint), TradingRestEndpoint);
		DemoTradingRestEndpoint = storage.GetValue(nameof(DemoTradingRestEndpoint), DemoTradingRestEndpoint);
		MarketDataRestEndpoint = storage.GetValue(nameof(MarketDataRestEndpoint), MarketDataRestEndpoint);
		TradingWebSocketEndpoint = storage.GetValue(nameof(TradingWebSocketEndpoint), TradingWebSocketEndpoint);
		DemoTradingWebSocketEndpoint = storage.GetValue(nameof(DemoTradingWebSocketEndpoint), DemoTradingWebSocketEndpoint);
		MarketDataWebSocketEndpoint = storage.GetValue(nameof(MarketDataWebSocketEndpoint), MarketDataWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": {LocalizedStrings.Key} = {Key.ToId()}";
}
