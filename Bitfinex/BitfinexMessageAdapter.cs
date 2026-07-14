namespace StockSharp.Bitfinex;

#if !NO_LICENSE
using StockSharp.Licensing;
#endif

[OrderCondition(typeof(BitfinexOrderCondition))]
public partial class BitfinexMessageAdapter
{
	private HttpClient _httpClient;
	private PusherClient _pusherClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="BitfinexMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BitfinexMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		//HeartbeatInterval = TimeSpan.FromSeconds(1);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.OrderLog);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths => [25, 100];

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Bitfinex];

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => nameof(Bitfinex);
#endif

	private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SendOutErrorAsync;
		_pusherClient.OrderBookSnaphot += SessionOnOrderBookSnaphot;
		_pusherClient.OrderBookIncrement += SessionOnOrderBookIncrement;
		_pusherClient.NewTrade += SessionOnNewTrade;
		_pusherClient.NewOrderLog += SessionOnNewOrderLog;
		_pusherClient.TickerChanged += SessionOnTickerChanged;
		_pusherClient.NewCandle += SessionOnNewCandle;
		_pusherClient.OrderChanged += SessionOnOrderChanged;
		_pusherClient.OrderError += SessionOnOrderError;
		_pusherClient.NewOwnTrade += SessionOnNewOwnTrade;
		_pusherClient.NewWallet += SessionOnNewWallet;
		_pusherClient.NewPosition += SessionOnNewPosition;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SendOutErrorAsync;
		_pusherClient.OrderBookSnaphot -= SessionOnOrderBookSnaphot;
		_pusherClient.OrderBookIncrement -= SessionOnOrderBookIncrement;
		_pusherClient.NewTrade -= SessionOnNewTrade;
		_pusherClient.NewOrderLog -= SessionOnNewOrderLog;
		_pusherClient.TickerChanged -= SessionOnTickerChanged;
		_pusherClient.NewCandle -= SessionOnNewCandle;
		_pusherClient.OrderChanged -= SessionOnOrderChanged;
		_pusherClient.OrderError -= SessionOnOrderError;
		_pusherClient.NewOwnTrade -= SessionOnNewOwnTrade;
		_pusherClient.NewWallet -= SessionOnNewWallet;
		_pusherClient.NewPosition -= SessionOnNewPosition;
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

		_orderStatusId = 0;
		_candlesTransactions.Clear();

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

#if !NO_LICENSE
		var msg = await "Crypto".ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);
		if (!msg.IsEmpty())
		{
			msg = await nameof(Bitfinex).ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);

			if (!msg.IsEmpty())
				throw new InvalidOperationException(msg);
		}
#endif

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_pusherClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new(Key, Secret) { Parent = this };
		_pusherClient = new(Key, Secret, ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };

		SubscribePusherClient();

		await _pusherClient.Connect(cancellationToken);
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
