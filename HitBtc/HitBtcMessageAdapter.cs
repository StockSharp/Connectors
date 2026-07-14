namespace StockSharp.HitBtc;

partial class HitBtcMessageAdapter
{
	private HttpClient _httpClient;
	private PusherClient _pusherClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="HitBtcMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public HitBtcMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		//HeartbeatInterval = TimeSpan.FromSeconds(1);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.HitBtc];

	private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SendOutErrorAsync;
		_pusherClient.TickerChanged += SessionOnTickerChanged;
		_pusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		_pusherClient.NewTrades += SessionOnNewTrades;
		_pusherClient.NewCandle += SessionOnNewCandle;
		_pusherClient.NewSymbols += SessionOnNewSymbols;
		_pusherClient.NewOrders += SessionOnNewOrders;
		_pusherClient.OrderChanged += SessionOnOrderChanged;
		_pusherClient.BalanceChanged += SessionOnBalanceChanged;
		_pusherClient.OrderError += SessionOnOrderError;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SendOutErrorAsync;
		_pusherClient.TickerChanged -= SessionOnTickerChanged;
		_pusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		_pusherClient.NewTrades -= SessionOnNewTrades;
		_pusherClient.NewCandle -= SessionOnNewCandle;
		_pusherClient.NewSymbols -= SessionOnNewSymbols;
		_pusherClient.NewOrders -= SessionOnNewOrders;
		_pusherClient.OrderChanged -= SessionOnOrderChanged;
		_pusherClient.BalanceChanged -= SessionOnBalanceChanged;
		_pusherClient.OrderError -= SessionOnOrderError;
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

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_pusherClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new HttpClient { Parent = this };

		_pusherClient = new PusherClient(Key, Secret, ReConnectionSettings.WorkingTime) { Parent = this };
		SubscribePusherClient();
		await _pusherClient.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_pusherClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_httpClient.Dispose();
		_httpClient = null;

		_pusherClient.Disconnect();

		return default;
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		if (_httpClient != null)
		{
			try
			{
				_httpClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_httpClient = null;
		}

		if (_pusherClient != null)
		{
			try
			{
				UnsubscribePusherClient();
				_pusherClient.Disconnect();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_pusherClient = null;
		}

		_orderBooks.Clear();
		_tickSubscriptionEndDates.Clear();

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}
}