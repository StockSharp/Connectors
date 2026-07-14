namespace StockSharp.ByBit;

#if !NO_LICENSE
using StockSharp.Licensing;
#endif

[OrderCondition(typeof(ByBitOrderCondition))]
public partial class ByBitMessageAdapter
{
	private HttpClient _httpClient;
	private PublicSocketClient _spotClient;
	private PublicSocketClient _linearClient;
	private PublicSocketClient _inverseClient;
	private PublicSocketClient _optionsClient;
	private PrivateSocketClient _privateClient;
	private ConnectionStateTracker _tracker;
	private readonly CachedSynchronizedList<BaseSocketClient> _socketClients = new();
	private Authenticator _authenticator;

	/// <summary>
	/// Initializes a new instance of the <see cref="ByBitMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public ByBitMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);

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
	public override string[] AssociatedBoards { get; } = [BoardCodes.ByBit, BoardCodes.ByBitInv, BoardCodes.ByBitLin];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [1, 25, 50, 200, 500];

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => IsDemo ? base.FeatureName : nameof(ByBit);
#endif

	private void SubscribePusherClient(BaseSocketClient client)
	{
		client.Error += SendOutErrorAsync;

		if (client is PublicSocketClient publicClient)
		{
			publicClient.OrderBookDelta += SessionOnOrderBookDelta;
			publicClient.OrderBookSnapshot += SessionOnOrderBookSnapshot;
			publicClient.KlinesReceived += SessionOnKlinesReceived;
			publicClient.TradesReceived += SessionOnTradesReceived;
			publicClient.TickerReceived += SessionOnTickerReceived;
		}
		else if (client is PrivateSocketClient privateClient)
		{
			privateClient.PositionsReceived += SessionOnPositionsReceived;
			privateClient.ExecutionsReceived += SessionOnExecutionsReceived;
			privateClient.OrdersReceived += SessionOnOrdersReceived;
			privateClient.WalletsReceived += SessionOnWalletsReceived;
		}
	}

	private void UnsubscribePusherClient(BaseSocketClient client)
	{
		client.Error -= SendOutErrorAsync;

		if (client is PublicSocketClient publicClient)
		{
			publicClient.OrderBookDelta -= SessionOnOrderBookDelta;
			publicClient.OrderBookSnapshot -= SessionOnOrderBookSnapshot;
			publicClient.KlinesReceived -= SessionOnKlinesReceived;
			publicClient.TradesReceived -= SessionOnTradesReceived;
			publicClient.TickerReceived -= SessionOnTickerReceived;
		}
		else if (client is PrivateSocketClient privateClient)
		{
			privateClient.PositionsReceived -= SessionOnPositionsReceived;
			privateClient.ExecutionsReceived -= SessionOnExecutionsReceived;
			privateClient.OrdersReceived -= SessionOnOrdersReceived;
			privateClient.WalletsReceived -= SessionOnWalletsReceived;
		}
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

		async ValueTask<TClient> reset<TClient>(TClient client)
			where TClient : BaseSocketClient
		{
			if (client is null)
				return null;

			try
			{
				UnsubscribePusherClient(client);
				client.Disconnect();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			return null;
		}

		_spotClient = await reset(_spotClient);
		_linearClient = await reset(_linearClient);
		_inverseClient = await reset(_inverseClient);
		_optionsClient = await reset(_optionsClient);
		_privateClient = await reset(_privateClient);

		if (_tracker is not null)
		{
			_tracker.StateChanged -= SendOutConnectionStateAsync;
			_tracker.Dispose();
			_tracker = null;
		}

		if (_authenticator is not null)
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

		_subscriptions.Clear();
		_subscriptionIds.Clear();
		_orderBookSeq.Clear();
		_socketClients.Clear();

		_userTradesTransId = default;
		_walletsTransId = default;

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

#if !NO_LICENSE
		var msg = IsDemo ? null : await nameof(ByBit).ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);
		if (!msg.IsEmpty())
			throw new InvalidOperationException(msg);
#endif

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_authenticator = new(this.IsTransactional(), Key, Secret);

		const string ver = "v5";

		_httpClient = new($"https://api.{Address}/{ver}/", $"https://{(IsDemo ? "api-demo" : "api")}.{Address}/{ver}/", _authenticator, RecvWindow, TimeStampOffset) { Parent = this };

		var publicSocketUrl = $"wss://stream.{Address}/{ver}/";
		var privateSocketUrl = $"wss://{(IsDemo ? "stream-demo" : "stream")}.{Address}/{ver}/";

		var reconnectAttempts = ReConnectionSettings.ReAttemptCount;

		_tracker = new();
		_tracker.StateChanged += SendOutConnectionStateAsync;

		void addSocket(BaseSocketClient client)
		{
			_socketClients.Add(client);
			_tracker.Add(client);
		}

		PublicSocketClient tryPublic(ByBitSections section)
		{
			if (!Sections.Contains(section) || !this.IsMarketData())
				return null;

			PublicSocketClient client = new($"{publicSocketUrl}public/{section.ToNative()}", reconnectAttempts, ReConnectionSettings.WorkingTime, section) { Parent = this };
			SubscribePusherClient(client);
			addSocket(client);
			return client;
		}
		
		_spotClient = tryPublic(ByBitSections.Spot);
		_linearClient = tryPublic(ByBitSections.Linear);
		_inverseClient = tryPublic(ByBitSections.Inverse);
		_optionsClient = tryPublic(ByBitSections.Options);

		if (this.IsTransactional())
		{
			_privateClient = new($"{privateSocketUrl}private", reconnectAttempts, ReConnectionSettings.WorkingTime, _authenticator, RecvWindow) { Parent = this };
			SubscribePusherClient(_privateClient);
			addSocket(_privateClient);
		}

		await _tracker.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_tracker.Disconnect();

		return default;
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> _socketClients.Cache.Select(c => c.SendPing(timeMsg.TransactionId, cancellationToken)).WhenAll();
}