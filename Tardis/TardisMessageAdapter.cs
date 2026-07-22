namespace StockSharp.Tardis;

/// <summary>The message adapter for Tardis.dev and Tardis Machine.</summary>
[MediaIcon(Media.MediaNames.tardis)]
[Doc("topics/api/connectors/crypto_exchanges/tardis.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TardisKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Candles)]
public partial class TardisMessageAdapter : MessageAdapter, ITokenAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public TardisStreamKey Key { get; init; }
		public long? Remaining { get; set; }
	}

	private sealed class ReplayOrderBook
	{
		private readonly SortedDictionary<decimal, decimal> _bids =
			new(Comparer<decimal>.Create(static (left, right) =>
				right.CompareTo(left)));
		private readonly SortedDictionary<decimal, decimal> _asks = [];

		public bool IsInitialized { get; private set; }

		public bool Apply(TardisBookChange update)
		{
			ArgumentNullException.ThrowIfNull(update);
			if (update.IsSnapshot)
			{
				_bids.Clear();
				_asks.Clear();
				IsInitialized = true;
			}
			else if (!IsInitialized)
			{
				return false;
			}
			ApplyLevels(_bids, update.Bids, "bid");
			ApplyLevels(_asks, update.Asks, "ask");
			return true;
		}

		public QuoteChange[] GetBids()
			=> [.. _bids.Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];

		public QuoteChange[] GetAsks()
			=> [.. _asks.Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];

		private static void ApplyLevels(
			SortedDictionary<decimal, decimal> target,
			TardisBookLevel[] levels, string side)
		{
			foreach (var level in levels ?? [])
			{
				if (level?.Price is not > 0 || level.Amount is null or < 0)
					throw new InvalidDataException(
						$"Tardis returned an invalid {side} replay level.");
				if (level.Amount == 0)
					target.Remove(level.Price.Value);
				else
					target[level.Price.Value] = level.Amount.Value;
			}
		}
	}

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _streamGate = new(1, 1);
	private readonly Dictionary<string, TardisInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly Dictionary<TardisStreamKey, TardisMachineStreamClient>
		_streams = [];
	private TardisRestClient _rest;
	private TardisMachineRestClient _machine;

	/// <summary>Initializes a new instance.</summary>
	public TardisMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(TardisExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Tardis];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType) => false;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.IsAssociated(BoardCodes.Tardis);

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		_streamGate.Dispose();
		base.DisposeManaged();
	}
}
