namespace StockSharp.CSV;

public partial class CSVMessageAdapter : MessageAdapter
{
	private class OnlineInfo(ISubscriptionMessage subscription)
	{
		public DateTime Next { get; set; }
		public ISubscriptionMessage Subscription { get; } = subscription ?? throw new ArgumentNullException(nameof(subscription));
	}

	private readonly SynchronizedDictionary<ImportSettings, OnlineInfo> _nextProcessing = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="CSVMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public CSVMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromMinutes(1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ? SecurityImportSettings != null : base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
		=> Settings.Select(s => s.DataType).ToAsyncEnumerable();

	/// <inheritdoc />
	protected override ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		_nextProcessing.Clear();

		return base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		this.AddSupportedCandleTimeFrames(Settings.Select(s => s.DataType).FilterTimeFrames());

		return base.ConnectAsync(connectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		var curr = CurrentTime;

		foreach (var pair in _nextProcessing.Where(p => p.Value.Next <= curr))
		{
			var settings = pair.Key;
			var info = pair.Value;
			
			var subscription = info.Subscription;

			if (settings.DataType == DataType.PositionChanges)
				await ImportPortfolios(settings, (PortfolioLookupMessage)subscription, cancellationToken);
			else if (settings.DataType == DataType.Transactions)
				await ImportTransactions(settings, (OrderStatusMessage)subscription, cancellationToken);
			else
				await ImportMarketData(settings, (MarketDataMessage)subscription, cancellationToken);

			info.Next = CurrentTime + settings.Interval;
		}
	}

	private bool TryAddNextProcessing(ImportSettings settings, ISubscriptionMessage message)
	{
		if (settings == null)
			throw new ArgumentNullException(nameof(settings));

		if (message == null)
			throw new ArgumentNullException(nameof(message));

		if (settings.Interval == TimeSpan.Zero)
			return false;

		_nextProcessing[settings] = new(message.TypedClone()) { Next = CurrentTime + settings.Interval };
		return true;
	}

	private CsvParser CreateParser(ImportSettings settings)
	{
		if (settings == null)
			throw new ArgumentNullException(nameof(settings));

		var parser = new CsvParser(settings.DataType, settings.SelectedFields) { Parent = this };
		settings.FillParser(parser);
		return parser;
	}
}