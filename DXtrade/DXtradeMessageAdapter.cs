namespace StockSharp.DXtrade;

#if !NO_LICENSE
using StockSharp.Licensing;
#endif

[OrderCondition(typeof(DXtradeOrderCondition))]
public partial class DXtradeMessageAdapter
{
	private HttpClient _httpClient;
	private PublicSocketClient _publicClient;
	private PrivateSocketClient _privateClient;
	private ConnectionStateTracker _tracker;

	/// <summary>
	/// Initializes a new instance of the <see cref="DXtradeMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public DXtradeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromMinutes(1);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.DevExperts];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => IsDemo ? base.FeatureName : nameof(DXtrade);
#endif

	private void SubscribePusherClient(BaseSocketClient client)
	{
		client.Error += SendOutErrorAsync;

		if (client is PublicSocketClient publicClient)
		{
			publicClient.CandleReceived += SessionOnCandleReceived;
			publicClient.QuoteReceived += SessionOnQuoteReceived;
		}
		else if (client is PrivateSocketClient privateClient)
		{
			privateClient.PortfolioReceived += SessionOnPortfolioReceived;
		}
	}

	private void UnsubscribePusherClient(BaseSocketClient client)
	{
		client.Error -= SendOutErrorAsync;

		if (client is PublicSocketClient publicClient)
		{
			publicClient.CandleReceived -= SessionOnCandleReceived;
			publicClient.QuoteReceived -= SessionOnQuoteReceived;
		}
		else if (client is PrivateSocketClient privateClient)
		{
			privateClient.PortfolioReceived -= SessionOnPortfolioReceived;
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

		if (_tracker is not null)
		{
			_tracker.StateChanged -= SendOutConnectionStateAsync;
			_tracker.Dispose();
			_tracker = null;
		}

		_publicClient = await reset(_publicClient);
		_privateClient = await reset(_privateClient);

		_processTransIds.Clear();
		_candleTransIds.Clear();
		_defaultAcc = default;

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (this.IsTransactional())
		{
			if (Login.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);

			if (Password.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.PasswordNotSpecified);
		}

#if !NO_LICENSE
		var msg = IsDemo ? null : await nameof(DXtrade).ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);
		if (!msg.IsEmpty())
			throw new InvalidOperationException(msg);
#endif

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var address = $"{Address}/dxsca-web/";
		_httpClient = new($"https://{address}") { Parent = this };

		var reconnectAttempts = ReConnectionSettings.ReAttemptCount;
		
		var sessionToken = await _httpClient.CreateSessionToken(Login, DomainAddress, Password.UnSecure(), cancellationToken);

		_tracker = new();
		_tracker.StateChanged += SendOutConnectionStateAsync;

		if (this.IsMarketData())
		{
			_publicClient = new($"wss://{address}md?format=json", reconnectAttempts, sessionToken, ReConnectionSettings.WorkingTime) { Parent = this };

			SubscribePusherClient(_publicClient);
			_tracker.Add(_publicClient);
		}

		if (this.IsTransactional())
		{
			_privateClient = new($"wss://{address}?format=json", reconnectAttempts, sessionToken, ReConnectionSettings.WorkingTime) { Parent = this };

			SubscribePusherClient(_privateClient);
			_tracker.Add(_privateClient);
		}

		await _tracker.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		await _httpClient.Logout(cancellationToken);

		_tracker.Disconnect();
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> _httpClient.Ping(cancellationToken).AsValueTask();
}