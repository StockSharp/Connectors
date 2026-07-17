namespace StockSharp.MatchTrader;

public partial class MatchTraderMessageAdapter
{
	private MatchTraderClient _client;
	private MatchTraderAccount _account;
	private readonly CachedSynchronizedDictionary<string, MatchTraderInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<long, MatchTraderCandleSubscription> _candleSubscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastPoll;
	private int _pollCursor;

	/// <summary>Initializes a new instance.</summary>
	public MatchTraderMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames([
			TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
			TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(4),
			TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
		]);
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
		var client = new MatchTraderClient(Address, Login, Password?.UnSecure(), BrokerId,
			AccountId, ReConnectionSettings.ReAttemptCount) { Parent = this };
		_client = client;
		try
		{
			_account = await client.Login(cancellationToken);
			CacheInstruments(await client.GetInstruments(cancellationToken));
			connectMsg.SessionId = $"Match-Trader {_account.TradingAccountId}";
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
		var interval = PollingInterval < TimeSpan.FromSeconds(1)
			? TimeSpan.FromSeconds(1) : PollingInterval;
		if (_client != null && now - _lastPoll >= interval)
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
		for (var i = 0; i < 4; i++)
		{
			switch (_pollCursor++ % 4)
			{
				case 0 when _level1Subscriptions.Count > 0:
					await RefreshQuotes(cancellationToken);
					return true;
				case 1 when _orderStatusSubscriptionId != 0:
					await SendOrders(_orderStatusSubscriptionId, false, cancellationToken);
					return true;
				case 2 when _portfolioSubscriptionId != 0:
					await SendPortfolio(_portfolioSubscriptionId, cancellationToken);
					return true;
				case 3 when _candleSubscriptions.Count > 0:
					await RefreshCandles(cancellationToken);
					return true;
			}
		}
		return false;
	}

	private void CacheInstruments(IEnumerable<MatchTraderInstrument> instruments)
	{
		foreach (var instrument in instruments ?? [])
		{
			if (!instrument.Symbol.IsEmpty())
				_instruments[instrument.Symbol] = instrument;
			if (!instrument.Alias.IsEmpty())
				_instruments[instrument.Alias] = instrument;
		}
	}

	private MatchTraderInstrument ResolveInstrument(SecurityId securityId)
		=> _instruments.TryGetValue(securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode)),
			out var instrument) ? instrument : throw new InvalidOperationException(
			$"Match-Trader instrument '{securityId.SecurityCode}' was not found.");

	private static SecurityId ToSecurityId(string symbol)
		=> new() { SecurityCode = symbol, BoardCode = BoardCodes.Fxcm };

	private string PortfolioName => _account?.TradingAccountId.IsEmpty(_account?.Uuid);

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
		_candleSubscriptions.Clear();
		_orderTransactions.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastPoll = default;
		_pollCursor = 0;
	}

	private sealed class MatchTraderCandleSubscription
	{
		public string Symbol { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public string Interval { get; init; }
		public DateTimeOffset LastTime { get; set; }
	}
}
