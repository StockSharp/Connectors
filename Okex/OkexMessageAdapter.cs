namespace StockSharp.Okex;

using Nito.AsyncEx;

public partial class OkexMessageAdapter
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } = Native.Extensions.TimeFrames.Select(p => p.Key).ToArray();

	private HttpClient _httpClient;
	private PublicPusherClient _publicPusherClient;
	private BusinessPusherClient _businessPusherClient;
	private PrivatePusherClient _privatePusherClient;

	private ConnectionStateTracker _tracker;
	private Authenticator _authenticator;

	/// <summary>
	/// Initializes a new instance of the <see cref="OkexMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public OkexMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);

		ResetWebSocketAddresses();

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
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5, 400];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Okex];

	#region subscribe pushers

	private void SubscribePushers(params BasePusherClient[] clients)
	{
		if (clients == null)
			throw new ArgumentNullException(nameof(clients));

		foreach (var client in clients)
		{
			if (client is null)
				continue;

			client.Error += SessionOnPusherError;

			if (client is PublicPusherClient pub)
			{
				//pub.InstrumentReceived	+= SessionOnInstrumentReceived;
				pub.OrderBookReceived	+= SessionOnOrderBookReceived;
				pub.Level1Received		+= SessionOnLevel1Received;
			}
			else if (client is BusinessPusherClient bus)
			{
				bus.TickReceived		+= SessionOnTickReceived;
				bus.CandleReceived		+= SessionOnCandleReceived;
			}
			else if (client is PrivatePusherClient priv)
			{
				priv.OrderChanged		+= SessionOnOrderChanged;
				priv.AccountChanged		+= SessionOnAccountChanged;
				priv.PositionChanged	+= SessionOnPositionChanged;
			}
		}
	}

	private void UnsubscribePushers(params BasePusherClient[] clients)
	{
		if (clients is null)
			throw new ArgumentNullException(nameof(clients));

		foreach (var client in clients)
		{
			if (client is null)
				continue;

			client.Error -= SessionOnPusherError;

			if (client is PublicPusherClient pub)
			{
				//pub.InstrumentReceived	-= SessionOnInstrumentReceived;
				pub.OrderBookReceived	-= SessionOnOrderBookReceived;
				pub.Level1Received		-= SessionOnLevel1Received;
			}
			else if (client is BusinessPusherClient bus)
			{
				bus.TickReceived		-= SessionOnTickReceived;
				bus.CandleReceived		-= SessionOnCandleReceived;
			}
			else if (client is PrivatePusherClient priv)
			{
				priv.OrderChanged		-= SessionOnOrderChanged;
				priv.AccountChanged		-= SessionOnAccountChanged;
				priv.PositionChanged	-= SessionOnPositionChanged;
			}
		}
	}

	#endregion

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

		if (_httpClient != null || _publicPusherClient != null || _privatePusherClient != null || _businessPusherClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_authenticator = new(Key, Secret, Passphrase, IsDemo);

		_httpClient = new(RestAddress, _authenticator) { Parent = this };

		var attempts = ReConnectionSettings.ReAttemptCount;

		if (this.IsTransactional())
		{
			var accCfg = await _httpClient.GetAccountConfigAsync(cancellationToken);
			if (accCfg.PosMode != PositionMode.Net)
			{
				this.AddWarningLog($"unexpected pos mode '{accCfg.PosMode}'. trying to set net_mode...");
				var cfg = await _httpClient.SetNetModeAsync(cancellationToken);

				if (cfg.PosMode != PositionMode.Net)
					throw new InvalidOperationException($"failed to set pos mode to net_mode. only net position mode is supported. actual={accCfg.PosMode}");
			}

			_privatePusherClient = new(_authenticator, WebSocketAddressPrivate, attempts, accCfg.AccountLevel, ReConnectionSettings.WorkingTime) { Parent = this };
			_businessPusherClient = new(_authenticator, WebSocketAddressBusiness, attempts, ReConnectionSettings.WorkingTime) { Parent = this };
		}

		_publicPusherClient = new(WebSocketAddressPublic, attempts, ReConnectionSettings.WorkingTime) { Parent = this };

		SubscribePushers(_publicPusherClient, _privatePusherClient, _businessPusherClient);

		_tracker = new();
		_tracker.StateChanged += SendOutConnectionStateAsync;

		_tracker.Add(_publicPusherClient);

		if (_privatePusherClient is not null)
			_tracker.Add(_privatePusherClient);

		if (_businessPusherClient is not null)
			_tracker.Add(_businessPusherClient);

		await _tracker.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage msg, CancellationToken cancellationToken)
	{
		if (_httpClient == null || _publicPusherClient == null /*|| _privatePusherClient == null || _businessPusherClient == null*/)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_httpClient.Dispose();

		_tracker.Disconnect();

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

		UnsubscribePushers(_publicPusherClient, _privatePusherClient, _businessPusherClient);

		async ValueTask<T> DisposePusher<T>(T pc)
			where T : BasePusherClient
		{
			if (pc is null)
				return null;

			try
			{
				pc.Disconnect();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			return null;
		}

		_publicPusherClient = await DisposePusher(_publicPusherClient);
		_privatePusherClient = await DisposePusher(_privatePusherClient);
		_businessPusherClient = await DisposePusher(_businessPusherClient);

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

		_candleTransactions.Clear();
		_processedTrades.Clear();

		_transactionsBuffer.Clear();
		_ordersOnline = null;

		if (_tracker is not null)
		{
			_tracker.StateChanged -= SendOutConnectionStateAsync;
			_tracker.Dispose();
			_tracker = null;
		}

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		ValueTask Ping(BasePusherClient client)
		{
			if (client is null)
				return default;

			return client.PingAsync(cancellationToken);
		}

		await Ping(_publicPusherClient);
		await Ping(_privatePusherClient);
		await Ping(_businessPusherClient);
	}

	private ValueTask SessionOnPusherError(BasePusherClient pusher, Exception error, CancellationToken cancellationToken) => SendOutErrorAsync(error, cancellationToken);
}