namespace StockSharp.Poloniex;

partial class PoloniexMessageAdapter
{
	private HttpClient _httpClient;
	private PusherClient _pusherClient;
	private Authenticator _authenticator;

	/// <summary>
	/// Initializes a new instance of the <see cref="PoloniexMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public PoloniexMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);

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
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Poloniex];

	private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SendOutErrorAsync;
		_pusherClient.OrderBookSnapshot += SessionOnOrderBookSnapshot;
		_pusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		_pusherClient.NewTrade += SessionOnNewTrade;
		_pusherClient.TickerChanged += SessionOnTickerChanged;
		//_pusherClient.TrollboxMessage += SessionOnTrollboxMessage;

		_pusherClient.BalanceChanged += SessionOnBalanceChanged;
		_pusherClient.OrderPending += SessionOnOrderPending;
		_pusherClient.NewOrder += SessionOnNewOrder;
		_pusherClient.OrderChanged += SessionOnOrderChanged;
		_pusherClient.OrderKilled += SessionOnOrderKilled;
		_pusherClient.NewOwnTrade += SessionOnNewOwnTrade;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SendOutErrorAsync;
		_pusherClient.OrderBookSnapshot -= SessionOnOrderBookSnapshot;
		_pusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		_pusherClient.NewTrade -= SessionOnNewTrade;
		_pusherClient.TickerChanged -= SessionOnTickerChanged;
		//_pusherClient.TrollboxMessage -= SessionOnTrollboxMessage;

		_pusherClient.BalanceChanged -= SessionOnBalanceChanged;
		_pusherClient.OrderPending -= SessionOnOrderPending;
		_pusherClient.NewOrder -= SessionOnNewOrder;
		_pusherClient.OrderChanged -= SessionOnOrderChanged;
		_pusherClient.OrderKilled -= SessionOnOrderKilled;
		_pusherClient.NewOwnTrade -= SessionOnNewOwnTrade;
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		_level1Counter = 0;
		//_trollboxCounter = 0;
		_tickerIds.Clear();

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

		_currencyIds.Clear();
		_tickerIds.Clear();

		_wsTradesSubscriptions.Clear();
		_wsBookSubscriptions.Clear();
		_wsSubscriptions.Clear();

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
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

		_authenticator = new(this.IsTransactional(), Key, Secret);

		_httpClient = new(_authenticator) { Parent = this };

		_pusherClient = new(_authenticator, ReConnectionSettings.WorkingTime) { Parent = this };
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
}