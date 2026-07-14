namespace StockSharp.LBank;

#if !NO_LICENSE
using StockSharp.Licensing;
#endif

partial class LBankMessageAdapter
{
	private HttpClient _httpClient;
	private PusherClient _pusherClient;
	private DateTime? _lastTimeBalanceCheck;

	private string _authKey;
	private DateTime? _authKeyLastTimeRefresh;

	/// <summary>
	/// Initializes a new instance of the <see cref="LBankMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public LBankMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
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
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [10, 50, 100];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.LBank];

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => nameof(LBank);
#endif

	private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SendOutErrorAsync;
		_pusherClient.PingReceived += SessionOnPingReceived;
		_pusherClient.TickerChanged += SessionOnTickerChanged;
		_pusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		_pusherClient.NewTrade += SessionOnNewTrade;
		_pusherClient.NewCandle += SessionOnNewCandle;
		_pusherClient.OrderUpdated += SessionOnOrderUpdated;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SendOutErrorAsync;
		_pusherClient.PingReceived -= SessionOnPingReceived;
		_pusherClient.TickerChanged -= SessionOnTickerChanged;
		_pusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		_pusherClient.NewTrade -= SessionOnNewTrade;
		_pusherClient.NewCandle -= SessionOnNewCandle;
		_pusherClient.OrderUpdated -= SessionOnOrderUpdated;
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
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

		_lastTimeBalanceCheck = null;

		_authKey = null;
		_authKeyLastTimeRefresh = null;

		_candlesTransactions.Clear();

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (this.IsTransactional())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

#if !NO_LICENSE
		var msg = await nameof(LBank).ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);
		if (!msg.IsEmpty())
			throw new InvalidOperationException(msg);
#endif

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_pusherClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new HttpClient(Key, Secret) { Parent = this };

		if (this.IsTransactional())
		{
			_authKey = await _httpClient.GetAuthKeyAsync(cancellationToken);
			_authKeyLastTimeRefresh = DateTime.Now;
		}

		_pusherClient = new PusherClient(ReConnectionSettings.WorkingTime) { Parent = this };
		SubscribePusherClient();
		await _pusherClient.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_pusherClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_authKey != null)
		{
			await _httpClient.DestroyAuthKeyAsync(_authKey, cancellationToken);

			_authKey = null;
			_authKeyLastTimeRefresh = null;
		}

		_httpClient.Dispose();
		_httpClient = null;

		_pusherClient.Disconnect();
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (BalanceCheckInterval > TimeSpan.Zero &&
			(_lastTimeBalanceCheck == null || (CurrentTime - _lastTimeBalanceCheck) > BalanceCheckInterval))
		{
			await PortfolioLookupAsync(null, cancellationToken);
		}

		if (!timeMsg.OriginalTransactionId.IsEmpty())
			await _pusherClient.Pong(timeMsg.OriginalTransactionId, cancellationToken);

		if (_authKeyLastTimeRefresh != null && (DateTime.Now - _authKeyLastTimeRefresh.Value).TotalMinutes > 50)
		{
			await _httpClient.RefreshAuthKeyAsync(_authKey, cancellationToken);
			_authKeyLastTimeRefresh = DateTime.Now;
		}
	}

	private ValueTask SessionOnPingReceived(string id, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeMessage
		{
			TransactionId = TransactionIdGenerator.GetNextId(),
			OriginalTransactionId = id,
			BackMode = MessageBackModes.Direct,
		}, cancellationToken);
	}
}