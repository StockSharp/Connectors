namespace StockSharp.Deribit;

[OrderCondition(typeof(DeribitOrderCondition))]
public partial class DeribitMessageAdapter
{
	private HttpClient _httpClient;
	private PusherClient _pusherClient;
	private Authenticator _authenticator;

	/// <summary>
	/// Initializes a new instance of the <see cref="DeribitMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public DeribitMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromMinutes(5);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.News);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Deribit];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SessionOnPusherError;
		//_pusherClient.NewSymbols += SessionOnNewSymbols;
		_pusherClient.TickerChanged += SessionOnTickerChanged;
		_pusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		_pusherClient.NewTrades += SessionOnNewTrades;
		_pusherClient.NewCandles += SessionOnNewCandles;
		_pusherClient.NewAnnouncement += SessionOnNewNewAnnouncement;
		_pusherClient.AccountChanged += SessionOnAccountChanged;
		_pusherClient.PositionChanged += SessionOnPositionChanged;
		_pusherClient.OrderChanged += SessionOnOrderChanged;
		_pusherClient.NewUserTrades += SessionOnNewUserTrades;
		_pusherClient.WithdrawUpdated += SessionOnWithdrawUpdated;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SessionOnPusherError;
		//_pusherClient.NewSymbols -= SessionOnNewSymbols;
		_pusherClient.TickerChanged -= SessionOnTickerChanged;
		_pusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		_pusherClient.NewTrades -= SessionOnNewTrades;
		_pusherClient.NewCandles -= SessionOnNewCandles;
		_pusherClient.NewAnnouncement -= SessionOnNewNewAnnouncement;
		_pusherClient.AccountChanged -= SessionOnAccountChanged;
		_pusherClient.PositionChanged -= SessionOnPositionChanged;
		_pusherClient.OrderChanged -= SessionOnOrderChanged;
		_pusherClient.NewUserTrades -= SessionOnNewUserTrades;
		_pusherClient.WithdrawUpdated -= SessionOnWithdrawUpdated;
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
				_pusherClient.Dispose();
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

		_orderBooks.Clear();
		_requesIdMap.Clear();

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

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_pusherClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var address = (IsDemo ? "test." : string.Empty) + Address;

		_httpClient = new(address) { Parent = this };

		_authenticator = new(this.IsTransactional(), Key, Secret);
		_pusherClient = new(address, _authenticator, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };
		SubscribePusherClient();

		await _pusherClient.Connect(cancellationToken);

		if (CancelOnDisconnect)
			await _pusherClient.CancelOnDisconnect(TransactionIdGenerator.GetNextId(), true, "connection", cancellationToken);

		if (HeartbeatInterval == default)
			await _pusherClient.DisableHeartbeat(TransactionIdGenerator.GetNextId(), cancellationToken);
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

		return _pusherClient.Disconnect(TransactionIdGenerator.GetNextId(), cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_pusherClient is not null)
			return _pusherClient.Ping(TransactionIdGenerator.GetNextId(), cancellationToken);

		return default;
	}

	private async ValueTask SessionOnPusherError(long? id, MessageTypes? type, Exception error, CancellationToken cancellationToken)
	{
		switch (type)
		{
			case MessageTypes.Connect:
				await SendOutMessageAsync(new ConnectMessage { Error = error }, cancellationToken);
				break;
			case MessageTypes.OrderRegister:
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					Error = error,
					OrderState = OrderStates.Failed,
					OriginalTransactionId = id ?? 0
				}, cancellationToken);
				break;

			case MessageTypes.SecurityLookup:
			case MessageTypes.PortfolioLookup:
				await SendSubscriptionReplyAsync(id ?? 0, cancellationToken, error);
				break;

			default:
				await SendOutErrorAsync(error, cancellationToken);
				break;
		}
	}
}