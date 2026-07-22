namespace StockSharp.CoinMarketCap;

/// <summary>The message adapter for CoinMarketCap REST and WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.coinmarketcap)]
[Doc("topics/api/connectors/crypto_exchanges/coinmarketcap.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinMarketCapKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles)]
public partial class CoinMarketCapMessageAdapter : MessageAdapter, ITokenAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public CoinMarketCapSecurityKey Key { get; init; }
		public long? Remaining { get; set; }
	}

	private sealed class CoinMarketCapCandle
	{
		public DateTime OpenTime { get; init; }
		public decimal Open { get; init; }
		public decimal High { get; init; }
		public decimal Low { get; init; }
		public decimal Close { get; init; }
		public decimal? Volume { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<int, CoinMarketCapMapEntry> _coins = [];
	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private CoinMarketCapRestClient _rest;
	private CoinMarketCapSocketClient _socket;
	private DateTime _nextPing;

	/// <summary>Initializes a new instance.</summary>
	public CoinMarketCapMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(5);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(CoinMarketCapExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.CoinMarketCap];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.CoinMarketCap);
}
