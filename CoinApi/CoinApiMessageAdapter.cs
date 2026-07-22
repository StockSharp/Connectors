namespace StockSharp.CoinApi;

/// <summary>The message adapter for CoinAPI Market Data REST and WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.coinapi)]
[Doc("topics/api/connectors/crypto_exchanges/coinapi.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinApiKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Candles)]
public partial class CoinApiMessageAdapter : MessageAdapter, ITokenAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public CoinApiStreamKey Key { get; init; }
		public int Depth { get; init; }
		public long? Remaining { get; set; }
		public DateTime? LastCountedCandle { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, CoinApiSymbol> _symbols =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private CoinApiRestClient _rest;
	private CoinApiSocketClient _socket;

	/// <summary>Initializes a new instance.</summary>
	public CoinApiMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(CoinApiExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.CoinApi];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.CoinApi);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
