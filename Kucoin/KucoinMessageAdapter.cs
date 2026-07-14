namespace StockSharp.Kucoin;

public partial class KucoinMessageAdapter
{
	private INativeAdapter _spotAdapter;
	private INativeAdapter _futuresAdapter;
	private ConnectionStateTracker _tracker;

	/// <summary>
	/// Initializes a new instance of the <see cref="KucoinMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public KucoinMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Kucoin, BoardCodes.KucoinFT];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	private INativeAdapter GetAdapter(ISecurityIdMessage secIdMsg)
		=> GetAdapter(secIdMsg.CheckOnNull(nameof(secIdMsg)).SecurityId);

	private INativeAdapter GetAdapter(SecurityId secId)
	{
		var adapter = secId.BoardCode.ToUpperInvariant() switch
		{
			BoardCodes.Kucoin => _spotAdapter,
			BoardCodes.KucoinFT => _futuresAdapter,
			_ => throw new ArgumentOutOfRangeException(nameof(secId), secId, LocalizedStrings.InvalidValue)
		};

		return adapter ?? throw new InvalidOperationException($"No adapter found for secId={secId}.");
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_spotAdapter is not null)
		{
			_spotAdapter.Dispose();
			_spotAdapter = null;
		}

		if (_futuresAdapter is not null)
		{
			_futuresAdapter.Dispose();
			_futuresAdapter = null;
		}

		if (_tracker is not null)
		{
			_tracker.StateChanged -= SendOutConnectionStateAsync;
			_tracker.Dispose();
			_tracker = null;
		}

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		var isMarketData = this.IsMarketData();
		var isTransactional = this.IsTransactional();

		if (isTransactional)
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

		if (_spotAdapter != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_futuresAdapter != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_spotAdapter = Sections.Contains(KucoinSections.Spot) ? new Native.Spot.SpotAdapter(this, SendOutMessageAsync) : null;
		_futuresAdapter = Sections.Contains(KucoinSections.Futures) ? new Native.Futures.FuturesAdapter(this, SendOutMessageAsync) : null;

		_tracker = new();
		_tracker.StateChanged += SendOutConnectionStateAsync;

		if (_spotAdapter is INativeAdapter spot)
			_tracker.Add(spot);

		if (_futuresAdapter is INativeAdapter fut)
			_tracker.Add(fut);

		await _tracker.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		_tracker.Disconnect();
		return default;
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_spotAdapter is INativeAdapter spot)
			await spot.TimeAsync(cancellationToken);

		if (_futuresAdapter is INativeAdapter fut)
			await fut.TimeAsync(cancellationToken);
	}
}