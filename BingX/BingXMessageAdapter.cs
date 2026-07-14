namespace StockSharp.BingX;

public partial class BingXMessageAdapter
{
	private Authenticator _authenticator;
	private readonly CachedSynchronizedDictionary<string, NativeAdapter> _adapters = new(StringComparer.InvariantCultureIgnoreCase);
	private ConnectionStateTracker _tracker;

	/// <summary>
	/// Initializes a new instance of the <see cref="BingXMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BingXMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override string[] AssociatedBoards
		=> [BoardCodes.BingX, BoardCodes.BingXFut];

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		foreach (var (_, adapter) in _adapters.CopyAndClear())
		{
			try
			{
				adapter.NewOutMessage -= SendOutMessageAsync;
				adapter.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}
		}

		if (_authenticator != null)
		{
			try
			{
				_authenticator.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_authenticator = null;
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
		if (this.IsTransactional())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

		if (_adapters.Count > 0)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_authenticator = new(this.IsTransactional(), Key, Secret);

		_tracker = new();
		_tracker.StateChanged += SendOutConnectionStateAsync;

		void addAdapter(NativeAdapter adapter)
		{
			adapter.Parent = this;
			adapter.NewOutMessage += SendOutMessageAsync;
			_adapters.Add(adapter.BoardCode, adapter);
			_tracker.Add(adapter);
		}

		if (Sections.Contains(BingXSections.Spot))
			addAdapter(new Native.Spot.SpotAdapter(this, _authenticator));

		if (Sections.Contains(BingXSections.Futures))
			addAdapter(new Native.Futures.FuturesAdapter(this, _authenticator));

		await _tracker.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_adapters.Count == 0)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_tracker.Disconnect();

		return default;
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> _adapters.CachedValues.Select(a => a.Time(timeMsg, cancellationToken)).WhenAll();
}