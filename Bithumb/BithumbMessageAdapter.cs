namespace StockSharp.Bithumb;

#if !NO_LICENSE
using StockSharp.Licensing;
#endif

public partial class BithumbMessageAdapter
{
	private HttpClient _httpClient;
	private PusherClient _pusherClient;
	private DateTime? _lastTimeBalanceCheck;

	/// <summary>
	/// Initializes a new instance of the <see cref="BithumbMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public BithumbMessageAdapter(IdGenerator transactionIdGenerator)
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
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Bithumb];

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => nameof(Bithumb);
#endif

	private void SubscribePusherClient(PusherClient pusherClient)
	{
		pusherClient.StateChanged += SendOutConnectionStateAsync;
		pusherClient.Error += SendOutErrorAsync;
		pusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		pusherClient.NewTicks += SessionOnNewTicks;
		pusherClient.TickersChanged += SessionOnTickersChanged;
	}

	private void UnsubscribePusherClient(PusherClient pusherClient)
	{
		pusherClient.StateChanged -= SendOutConnectionStateAsync;
		pusherClient.Error -= SendOutErrorAsync;
		pusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		pusherClient.NewTicks -= SessionOnNewTicks;
		pusherClient.TickersChanged -= SessionOnTickersChanged;
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage msg, CancellationToken cancellationToken)
	{
		if (this.IsTransactional())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

#if !NO_LICENSE
		var message = await "Crypto".ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);
		if (!message.IsEmpty())
		{
			message = await nameof(Bithumb).ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);

			if (!message.IsEmpty())
				throw new InvalidOperationException(message);
		}
#endif

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_pusherClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new(IsPrime, Key, Secret) { Parent = this };
		_pusherClient = new(ReConnectionSettings.AttemptCount, ReConnectionSettings.WorkingTime) { Parent = this };

		SubscribePusherClient(_pusherClient);

		await _pusherClient.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage msg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_pusherClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_httpClient.Dispose();
		_pusherClient.Disconnect();

		return default;
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage msg, CancellationToken cancellationToken)
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

		if (_pusherClient is PusherClient pc)
		{
			try
			{
				UnsubscribePusherClient(pc);
				pc.Disconnect();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_pusherClient = null;
		}

		_orderInfo.Clear();
		_lastTimeBalanceCheck = null;

		_orderBookSubscriptions.Clear();
		_tradesSubscriptions.Clear();
		_level1Subscriptions.Clear();

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_orderInfo.Count > 0)
		{
			await OrderStatusAsync(null, cancellationToken);
			await PortfolioLookupAsync(null, cancellationToken);
		}

		if (BalanceCheckInterval > TimeSpan.Zero &&
			(_lastTimeBalanceCheck == null || (CurrentTime - _lastTimeBalanceCheck) > BalanceCheckInterval))
		{
			await PortfolioLookupAsync(null, cancellationToken);
		}
	}
}