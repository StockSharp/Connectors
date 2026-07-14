namespace StockSharp.Bitmex;

[OrderCondition(typeof(BitmexOrderCondition))]
public partial class BitmexMessageAdapter
{
	private HttpClient _httpClient;
	private PusherClient _pusherClient;
	private Authenticator _authenticator;

	/// <summary>
	/// Initializes a new instance of the <see cref="BitmexMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BitmexMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.OrderLog);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Bitmex];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SendOutErrorAsync;
		_pusherClient.TickersChanged += SessionOnTickersChanged;
		_pusherClient.OrderBooksChanged += SessionOnOrderBooksChanged;
		_pusherClient.NewOrderLog += SessionOnNewOrderLog;
		_pusherClient.NewTrades += SessionOnNewTrades;
		_pusherClient.NewQuoteCandles += SessionOnNewQuoteCandles;
		_pusherClient.NewTradeCandles += SessionOnNewTradeCandles;
		_pusherClient.PositionsChanged += SessionOnPositionsChanged;
		_pusherClient.MarginsChanged += SessionOnMarginsChanged;
		_pusherClient.OrderChanged += SessionOnOrderChanged;
		_pusherClient.NewExecutions += SessionOnNewExecutions;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SendOutErrorAsync;
		_pusherClient.TickersChanged -= SessionOnTickersChanged;
		_pusherClient.OrderBooksChanged -= SessionOnOrderBooksChanged;
		_pusherClient.NewOrderLog -= SessionOnNewOrderLog;
		_pusherClient.NewTrades -= SessionOnNewTrades;
		_pusherClient.NewQuoteCandles -= SessionOnNewQuoteCandles;
		_pusherClient.NewTradeCandles -= SessionOnNewTradeCandles;
		_pusherClient.PositionsChanged -= SessionOnPositionsChanged;
		_pusherClient.MarginsChanged -= SessionOnMarginsChanged;
		_pusherClient.OrderChanged -= SessionOnOrderChanged;
		_pusherClient.NewExecutions -= SessionOnNewExecutions;
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

		_buildFromFields.Clear();
		_olSnapshots.Clear();
		_liveQuoteCandles.Clear();
		_liveTradeCandles.Clear();

		_ownTradesTransId = _posTransId = default;

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
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

		_httpClient = new(_authenticator, IsDemo ? "testnet" : "www") { Parent = this };
		_pusherClient = new(_authenticator, IsDemo ? "testnet." : string.Empty, new UTCIncrementalIdGenerator(), ReConnectionSettings.ReAttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };

		SubscribePusherClient();
		return _pusherClient.Connect(cancellationToken);
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