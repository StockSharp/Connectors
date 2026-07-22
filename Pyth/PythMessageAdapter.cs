namespace StockSharp.Pyth;

/// <summary>The message adapter for Pyth Pro market data.</summary>
[MediaIcon(Media.MediaNames.pyth)]
[Doc("topics/api/connectors/crypto_exchanges/pyth.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PythKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.Stock |
	MessageAdapterCategories.FX | MessageAdapterCategories.Crypto |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Commodities |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles)]
public partial class PythMessageAdapter : MessageAdapter, ITokenAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public PythSymbol Instrument { get; init; }
		public PythChannels Channel { get; init; }
		public long? Remaining { get; set; }
		public string LastUpdateKey { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _poolGate = new(1, 1);
	private readonly Dictionary<string, PythSymbol> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private PythRestClient _rest;
	private PythSocketPool _pool;

	/// <summary>Initializes a new instance.</summary>
	public PythMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(PythExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Pyth];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.Pyth);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync().AsTask().GetAwaiter().GetResult();
		_poolGate.Dispose();
		base.DisposeManaged();
	}
}
