namespace StockSharp.TradeLocker;

public partial class TradeLockerMessageAdapter
{
	private TradeLockerClient _client;
	private TradeLockerAccount _account;
	private readonly CachedSynchronizedDictionary<long, TradeLockerInstrument> _instruments = [];
	private readonly SynchronizedDictionary<long, long> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<long, long> _orderTransactions = [];
	private readonly SynchronizedDictionary<long, decimal> _filledQuantities = [];
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastPoll;
	private int _pollCursor;

	/// <summary>Initializes a new instance.</summary>
	public TradeLockerMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5),
			TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
			TimeSpan.FromHours(4), TimeSpan.FromDays(1), TimeSpan.FromDays(7)]);
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Fxcm];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		var client = new TradeLockerClient(IsDemo, Login, Password?.UnSecure(), Server,
			DeveloperApiKey?.UnSecure(), ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		_client = client;
		try
		{
			_account = await client.Login(AccountId, cancellationToken);
			CacheInstruments(await client.GetInstruments(cancellationToken));
			connectMsg.SessionId = $"TradeLocker {_account.Id}";
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeClient();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var now = CurrentTime;
		if (_client != null && now - _lastPoll >= PollingInterval.Max(TimeSpan.FromSeconds(1)))
		{
			try
			{
				if (!await RunNextPollingJob(cancellationToken))
					await _client.Ping(cancellationToken);
			}
			catch (Exception error)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			_lastPoll = now;
		}
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async Task<bool> RunNextPollingJob(CancellationToken cancellationToken)
	{
		for (var i = 0; i < 3; i++)
		{
			switch (_pollCursor++ % 3)
			{
				case 0 when _level1Subscriptions.Count > 0:
					await RefreshLevel1(cancellationToken);
					return true;
				case 1 when _orderStatusSubscriptionId != 0:
					await SendOrders(_orderStatusSubscriptionId, false, cancellationToken);
					return true;
				case 2 when _portfolioSubscriptionId != 0:
					await SendPortfolio(_portfolioSubscriptionId, cancellationToken);
					return true;
			}
		}
		return false;
	}

	private void CacheInstruments(IEnumerable<TradeLockerInstrument> instruments)
	{
		foreach (var instrument in instruments ?? [])
			_instruments[instrument.TradableId] = instrument;
	}

	private TradeLockerInstrument ResolveInstrument(SecurityId securityId)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		return _instruments.CachedValues.FirstOrDefault(i => i.Name.EqualsIgnoreCase(code) ||
			i.TradableId.ToString(CultureInfo.InvariantCulture).EqualsIgnoreCase(code))
			?? throw new InvalidOperationException($"TradeLocker instrument '{code}' was not found.");
	}

	private static long GetRoute(TradeLockerInstrument instrument, string type)
		=> instrument.Routes?.FirstOrDefault(r => r.Type.EqualsIgnoreCase(type))?.Id
			?? throw new InvalidOperationException(
				$"TradeLocker instrument '{instrument.Name}' has no {type} route.");

	private static SecurityId ToSecurityId(TradeLockerInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Name,
			BoardCode = instrument.TradingExchange.IsEmpty(
				instrument.MarketDataExchange.IsEmpty(BoardCodes.Fxcm)),
		};

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
		_account = null;
	}

	private void ClearState()
	{
		_instruments.Clear();
		_level1Subscriptions.Clear();
		_orderTransactions.Clear();
		_filledQuantities.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastPoll = default;
		_pollCursor = 0;
	}
}
