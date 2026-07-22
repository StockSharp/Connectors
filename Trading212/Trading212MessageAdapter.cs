namespace StockSharp.Trading212;

public partial class Trading212MessageAdapter
{
	private Trading212Client _client;
	private Trading212AccountSummary _account;
	private readonly CachedSynchronizedDictionary<string, Trading212TradableInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _scheduleExchanges = [];
	private readonly SynchronizedDictionary<long, long> _orderTransactions = [];
	private readonly SynchronizedDictionary<long, long> _cancelTransactions = [];
	private readonly SynchronizedDictionary<long, string> _orderSignatures = [];
	private readonly SynchronizedSet<long> _activeOrders = [];
	private readonly SynchronizedSet<long> _ordersAwaitingHistory = [];
	private readonly SynchronizedSet<long> _reportedFills = [];
	private readonly CachedSynchronizedSet<string> _positionTickers =
		new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastMetadataRefresh;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;
	private DateTime _lastConnectionCheck;

	/// <summary>Initializes a new instance of the <see cref="Trading212MessageAdapter"/> class.</summary>
	public Trading212MessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [Trading212Extensions.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var apiKey = Key?.UnSecure().ThrowIfEmpty(nameof(Key));
		var apiSecret = Secret?.UnSecure().ThrowIfEmpty(nameof(Secret));
		var client = new Trading212Client(apiKey, apiSecret, IsDemo,
			Math.Max(1, ReConnectionSettings.ReAttemptCount))
		{
			Parent = this,
		};
		_client = client;
		try
		{
			_account = await client.GetAccountSummary(cancellationToken)
				?? throw new InvalidOperationException("Trading 212 returned no account summary.");
			await RefreshMetadata(cancellationToken);
			connectMsg.SessionId = $"Trading 212 {_account.Id.ToString(CultureInfo.InvariantCulture)}";
			_lastConnectionCheck = CurrentTime;
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
		if (_client != null && (_orderStatusSubscriptionId != 0 || _activeOrders.Count > 0 ||
			_ordersAwaitingHistory.Count > 0) &&
			now - _lastOrderRefresh >= PollingInterval)
		{
			try
			{
				await SendOrderSnapshot(_orderStatusSubscriptionId, null, false, cancellationToken);
			}
			catch (Exception error)
			{
				_lastOrderRefresh = now;
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		if (_client != null && _portfolioSubscriptionId != 0 &&
			now - _lastPortfolioRefresh >= PollingInterval)
		{
			try
			{
				await SendPortfolioSnapshot(_portfolioSubscriptionId, _portfolioFilter, false,
					cancellationToken);
			}
			catch (Exception error)
			{
				_lastPortfolioRefresh = now;
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		if (_client != null && now - _lastConnectionCheck >= TimeSpan.FromSeconds(30) &&
			now - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
		{
			try
			{
				_account = await _client.GetAccountSummary(cancellationToken);
				_lastConnectionCheck = now;
			}
			catch (Exception error)
			{
				_lastConnectionCheck = now;
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async Task RefreshMetadata(CancellationToken cancellationToken)
	{
		var exchangesTask = _client.GetExchanges(cancellationToken);
		var instrumentsTask = _client.GetInstruments(cancellationToken);
		await Task.WhenAll(exchangesTask, instrumentsTask);

		_scheduleExchanges.Clear();
		foreach (var exchange in await exchangesTask ?? [])
		{
			foreach (var schedule in exchange?.WorkingSchedules ?? [])
			{
				if (schedule != null && schedule.Id != 0)
					_scheduleExchanges[schedule.Id] = exchange.Name;
			}
		}

		_instruments.Clear();
		foreach (var instrument in await instrumentsTask ?? [])
		{
			if (instrument?.Ticker.IsEmpty() == false)
				_instruments[instrument.Ticker] = instrument;
		}
		_lastMetadataRefresh = CurrentTime;
	}

	private async Task EnsureMetadata(CancellationToken cancellationToken)
	{
		if (_instruments.Count == 0 || CurrentTime - _lastMetadataRefresh >= TimeSpan.FromMinutes(10))
			await RefreshMetadata(cancellationToken);
	}

	private async Task<Trading212TradableInstrument> ResolveInstrument(string ticker,
		CancellationToken cancellationToken)
	{
		ticker.ThrowIfEmpty(nameof(ticker));
		if (_instruments.TryGetValue(ticker, out var instrument))
			return instrument;
		await RefreshMetadata(cancellationToken);
		return _instruments.TryGetValue(ticker, out instrument)
			? instrument
			: throw new InvalidOperationException($"Trading 212 instrument '{ticker}' was not found.");
	}

	private string GetExchange(long workingScheduleId)
		=> _scheduleExchanges.TryGetValue(workingScheduleId, out var exchange)
			? exchange
			: Trading212Extensions.BoardCode;

	private SecurityId GetSecurityId(string ticker, Trading212Instrument instrument = null)
	{
		if (!ticker.IsEmpty() && _instruments.TryGetValue(ticker, out var metadata))
			return metadata.ToSecurityId();
		var securityId = new SecurityId
		{
			SecurityCode = ticker.IsEmpty(instrument?.Ticker),
			BoardCode = Trading212Extensions.BoardCode,
		};
		if (instrument?.Isin.IsEmpty() == false)
			securityId.Isin = instrument.Isin;
		return securityId;
	}

	private string ResolvePortfolio(string portfolioName)
	{
		var account = _account?.Id.ToString(CultureInfo.InvariantCulture)
			?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(account))
			throw new InvalidOperationException($"Trading 212 account '{portfolioName}' is not available.");
		return account;
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
		_account = null;
	}

	private void ClearState()
	{
		_instruments.Clear();
		_scheduleExchanges.Clear();
		_orderTransactions.Clear();
		_cancelTransactions.Clear();
		_orderSignatures.Clear();
		_activeOrders.Clear();
		_ordersAwaitingHistory.Clear();
		_reportedFills.Clear();
		_positionTickers.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioFilter = null;
		_lastMetadataRefresh = default;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		_lastConnectionCheck = default;
	}
}
