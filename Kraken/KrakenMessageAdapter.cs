namespace StockSharp.Kraken;

#if !NO_LICENSE
using StockSharp.Licensing;
#endif

[OrderCondition(typeof(KrakenOrderCondition))]
public partial class KrakenMessageAdapter
{
	private Native.Spot.SpotHttpClient _spotHttpClient;
	private Native.Spot.SpotPusherClient _spotPusherClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="KrakenMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public KrakenMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = DefaultHeartbeatInterval;

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
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [10, 25, 100, 500, 1000];

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Kraken];

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => nameof(Kraken);
#endif

	private void SubscribePusherClient()
	{
		_spotPusherClient.StateChanged += SendOutConnectionStateAsync;
		_spotPusherClient.Error += SendOutErrorAsync;
		_spotPusherClient.SubscriptionResponse += SessionOnSubscriptionResponse;
		_spotPusherClient.TickerChanged += SessionOnTickerChanged;
		_spotPusherClient.NewTrades += SessionOnNewTrades;
		_spotPusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		_spotPusherClient.NewCandle += SessionOnNewCandle;
		_spotPusherClient.SystemUpdated += SessionOnSystemUpdated;
	}

	private void UnsubscribePusherClient()
	{
		_spotPusherClient.StateChanged -= SendOutConnectionStateAsync;
		_spotPusherClient.Error -= SendOutErrorAsync;
		_spotPusherClient.SubscriptionResponse -= SessionOnSubscriptionResponse;
		_spotPusherClient.TickerChanged -= SessionOnTickerChanged;
		_spotPusherClient.NewTrades -= SessionOnNewTrades;
		_spotPusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		_spotPusherClient.NewCandle -= SessionOnNewCandle;
		_spotPusherClient.SystemUpdated -= SessionOnSystemUpdated;
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		_orderInfo.Clear();

		if (_spotHttpClient != null)
		{
			try
			{
				_spotHttpClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_spotHttpClient = null;
		}

		if (_spotPusherClient != null)
		{
			try
			{
				UnsubscribePusherClient();
				_spotPusherClient.Disconnect();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_spotPusherClient = null;
		}

		_orderBooks.Clear();
		_candlesTransactions.Clear();

		await base.ResetAsync(resetMsg, cancellationToken);
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

#if !NO_LICENSE
		var msg = "Crypto".ValidateLicense(component: GetType());
		if (!msg.IsEmpty())
		{
			msg = nameof(Kraken).ValidateLicense(component: GetType());

			if (!msg.IsEmpty())
				throw new InvalidOperationException(msg);
		}
#endif

		if (_spotHttpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_spotHttpClient = new(Key, Secret) { Parent = this };

		_spotPusherClient = new(ReConnectionSettings.WorkingTime) { Parent = this };
		SubscribePusherClient();

		return _spotPusherClient.Connect(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_spotHttpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_spotHttpClient.Dispose();
		_spotHttpClient = null;

		_spotPusherClient.Disconnect();

		return default;
	}
}
