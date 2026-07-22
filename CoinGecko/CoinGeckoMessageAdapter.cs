namespace StockSharp.CoinGecko;

/// <summary>The message adapter for CoinGecko REST and WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.coingecko)]
[Doc("topics/api/connectors/crypto_exchanges/coingecko.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinGeckoKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles)]
public partial class CoinGeckoMessageAdapter : MessageAdapter, ITokenAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public CoinGeckoSecurityKey Key { get; init; }
		public DataType DataType { get; init; }
		public TimeSpan? TimeFrame { get; init; }
		public long? Remaining { get; set; }
		public CoinGeckoCandle ActiveCandle { get; set; }
		public DateTime? LastCountedCandle { get; set; }
	}

	private sealed class CoinGeckoCandle
	{
		public DateTime OpenTime { get; init; }
		public decimal Open { get; init; }
		public decimal High { get; init; }
		public decimal Low { get; init; }
		public decimal Close { get; init; }
		public decimal? Volume { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, CoinGeckoCoin> _coins =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CoinGeckoSecurityKey> _pools =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly HashSet<string> _seenTradeIds = new(StringComparer.Ordinal);
	private readonly Queue<string> _seenTradeOrder = [];
	private CoinGeckoRestClient _rest;
	private CoinGeckoSocketClient _socket;

	/// <summary>Initializes a new instance.</summary>
	public CoinGeckoMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(CoinGeckoExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.CoinGecko, BoardCodes.CoinGeckoOnChain];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.CoinGecko) ||
			securityId.IsAssociated(BoardCodes.CoinGeckoOnChain);

	private static string PoolCacheKey(string network, string poolAddress)
		=> network + ":" + poolAddress;

	private bool RememberTrade(string tradeId)
	{
		if (tradeId.IsEmpty())
			return true;
		using (_sync.EnterScope())
		{
			if (!_seenTradeIds.Add(tradeId))
				return false;
			_seenTradeOrder.Enqueue(tradeId);
			while (_seenTradeOrder.Count > 50000)
				_seenTradeIds.Remove(_seenTradeOrder.Dequeue());
			return true;
		}
	}
}
