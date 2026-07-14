namespace StockSharp.Cex;

public partial class CexMessageAdapter : MessageAdapter
{
	private HttpClient _httpClient;
	private PusherClient _pusherClient;
	private Authenticator _authenticator;

	private readonly SynchronizedDictionary<long, Tuple<MessageTypes, SecurityId>> _msgsByTransId = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="CexMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public CexMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(3);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.RemoveSupportedMessage(MessageTypes.OrderStatus);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		//this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths => Messages.Extensions.AnyDepths;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Cex];

	private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SessionOnPusherError;
		//_pusherClient.TickerChanged += SessionOnTickerChanged;
		_pusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		_pusherClient.OrderBookSnapshot += SessionOnOrderBookSnapshot;
		_pusherClient.NewCandle += SessionOnNewCandle;
		_pusherClient.Ohlcv24Changed += SessionOnOhlcv24Changed;
		_pusherClient.BalancesReceived += SessionOnBalancesReceived;
		_pusherClient.BalanceReceived += SessionOnBalanceReceived;
		_pusherClient.OpenOrdersReceived += SessionOnOpenOrdersReceived;
		_pusherClient.OrderPlaced += SessionOnOrderPlaced;
		_pusherClient.OrderReplaced += SessionOnOrderReplaced;
		_pusherClient.OrderCanceled += SessionOnOrderCanceled;
		_pusherClient.NewTransaction += SessionOnNewTransaction;
		_pusherClient.OrderChanged += SessionOnOrderChanged;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SessionOnPusherError;
		//_pusherClient.TickerChanged -= SessionOnTickerChanged;
		_pusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		_pusherClient.OrderBookSnapshot -= SessionOnOrderBookSnapshot;
		_pusherClient.NewCandle -= SessionOnNewCandle;
		_pusherClient.Ohlcv24Changed -= SessionOnOhlcv24Changed;
		_pusherClient.BalancesReceived -= SessionOnBalancesReceived;
		_pusherClient.BalanceReceived -= SessionOnBalanceReceived;
		_pusherClient.OpenOrdersReceived -= SessionOnOpenOrdersReceived;
		_pusherClient.OrderPlaced -= SessionOnOrderPlaced;
		_pusherClient.OrderReplaced -= SessionOnOrderReplaced;
		_pusherClient.OrderCanceled -= SessionOnOrderCanceled;
		_pusherClient.NewTransaction -= SessionOnNewTransaction;
		_pusherClient.OrderChanged -= SessionOnOrderChanged;
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

		_authenticator = new Authenticator(this.IsTransactional(), Key, Secret);

		_httpClient = new HttpClient { Parent = this };
		_pusherClient = new PusherClient(_authenticator, ReConnectionSettings.AttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };

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
		_msgsByTransId.Clear();
		_candleTransactionIds.Clear();

		_tradesSubscriptions.Clear();

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		return ProcessSubscriptionsAsync(cancellationToken);
	}

	private async ValueTask SessionOnPusherError(long? originTransId, Exception error, CancellationToken cancellationToken)
	{
		if (originTransId != null)
		{
			if (_msgsByTransId.TryGetValue(originTransId.Value, out var tuple))
			{
				switch (tuple.Item1)
				{
					case MessageTypes.OrderRegister:
					case MessageTypes.OrderReplace:
					case MessageTypes.OrderCancel:
						await SendOutMessageAsync(new ExecutionMessage
						{
							DataTypeEx = DataType.Transactions,
							HasOrderInfo = true,
							OriginalTransactionId = originTransId.Value,
							Error = error,
							OrderState = OrderStates.Failed,
						}, cancellationToken);
						return;
				}
			}
		}

		await SendOutErrorAsync(error, cancellationToken);
	}
}