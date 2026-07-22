namespace StockSharp.StocksTrader;

public partial class StocksTraderMessageAdapter
{
	private sealed class NativeTarget
	{
		public StocksTraderOrder Order { get; init; }
		public StocksTraderDeal Deal { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, StocksTraderInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _transactionOrders = [];
	private readonly Dictionary<string, long> _cancelTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _orderSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _reportedTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownDeals =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _positionTickers =
		new(StringComparer.OrdinalIgnoreCase);
	private StocksTraderClient _client;
	private StocksTraderAccount _account;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastInstrumentRefresh;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;
	private DateTime _lastConnectionCheck;

	/// <summary>Initializes a new instance of the <see cref="StocksTraderMessageAdapter"/> class.</summary>
	public StocksTraderMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [StocksTraderExtensions.BoardCode];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(StocksTraderExtensions.BoardCode) ||
			securityId.IsAssociated(StocksTraderExtensions.BoardCode);

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		var client = new StocksTraderClient(Address,
			Token ?? throw new InvalidOperationException("StocksTrader access token is required."),
			Math.Max(1, ReConnectionSettings.ReAttemptCount))
		{
			Parent = this,
		};
		_client = client;
		try
		{
			var accounts = await client.GetAccountsAsync(cancellationToken) ?? [];
			_account = SelectAccount(accounts);
			await RefreshInstrumentsAsync(cancellationToken);
			connectMsg.SessionId = $"StocksTrader {_account.Id}";
			_lastConnectionCheck = DateTime.UtcNow;
			this.AddInfoLog("Connected to StocksTrader {0} account {1} with {2} instruments.",
				_account.Type, _account.Id, GetInstrumentCount());
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
		if (_client is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeClient();
		ClearState();
		this.AddInfoLog("StocksTrader disconnected without invalidating the API token.");
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
		var now = DateTime.UtcNow;
		if (_client is not null && _orderStatusSubscriptionId != 0 &&
			now - _lastOrderRefresh >= PollingInterval)
		{
			try
			{
				await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, false,
					cancellationToken);
			}
			catch (Exception error)
			{
				_lastOrderRefresh = now;
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		if (_client is not null && _portfolioSubscriptionId != 0 &&
			now - _lastPortfolioRefresh >= PollingInterval)
		{
			try
			{
				await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
					_portfolioFilter, false, cancellationToken);
			}
			catch (Exception error)
			{
				_lastPortfolioRefresh = now;
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		if (_client is not null && now - _lastConnectionCheck >= TimeSpan.FromSeconds(30) &&
			now - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
		{
			try
			{
				await _client.GetAccountStateAsync(PortfolioName, cancellationToken);
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

	private StocksTraderAccount SelectAccount(StocksTraderAccount[] accounts)
	{
		accounts = [.. accounts.Where(static account => account?.Id.IsEmpty() == false)];
		var expectedType = IsDemo ? "demo" : "real";
		if (!AccountId.IsEmpty())
		{
			var selected = accounts.FirstOrDefault(account =>
				account.Id.EqualsIgnoreCase(AccountId))
				?? throw new InvalidOperationException(
					$"StocksTrader account '{AccountId}' was not found.");
			if (!selected.Type.EqualsIgnoreCase(expectedType))
				throw new InvalidOperationException(
					$"StocksTrader account '{selected.Id}' is {selected.Type}, " +
					$"but the adapter is configured for {expectedType}.");
			if (selected.Status.EqualsIgnoreCase("disabled"))
				throw new InvalidOperationException(
					$"StocksTrader account '{selected.Id}' is disabled.");
			return selected;
		}

		var matching = accounts.Where(account =>
			account.Type.EqualsIgnoreCase(expectedType) &&
			!account.Status.EqualsIgnoreCase("disabled")).ToArray();
		if (matching.Length == 1)
			return matching[0];
		if (matching.Length == 0)
			throw new InvalidOperationException(
				$"The StocksTrader token exposes no available {expectedType} accounts.");
		throw new InvalidOperationException(
			$"The StocksTrader token exposes multiple {expectedType} accounts. " +
			"Configure AccountId explicitly.");
	}

	private async Task RefreshInstrumentsAsync(CancellationToken cancellationToken)
	{
		var instruments = await Client.GetInstrumentsAsync(PortfolioName,
			cancellationToken) ?? [];
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			foreach (var instrument in instruments)
			{
				if (instrument?.Ticker.IsEmpty() == false)
					_instruments[instrument.Ticker] = instrument;
			}
			_lastInstrumentRefresh = DateTime.UtcNow;
		}
		this.AddInfoLog("StocksTrader cached {0} account-specific instruments.",
			instruments.Length);
	}

	private async Task EnsureInstrumentsAsync(CancellationToken cancellationToken)
	{
		bool refresh;
		using (_sync.EnterScope())
			refresh = _instruments.Count == 0 ||
				DateTime.UtcNow - _lastInstrumentRefresh >= TimeSpan.FromDays(1);
		if (refresh)
			await RefreshInstrumentsAsync(cancellationToken);
	}

	private async Task<StocksTraderInstrument> ResolveInstrumentAsync(SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var ticker = (securityId.Native as string).IsEmpty(securityId.SecurityCode);
		ticker.ThrowIfEmpty(nameof(securityId));
		await EnsureInstrumentsAsync(cancellationToken);
		using (_sync.EnterScope())
		{
			return _instruments.TryGetValue(ticker, out var instrument)
				? instrument
				: throw new InvalidOperationException(
					$"StocksTrader instrument '{ticker}' was not found for account {PortfolioName}.");
		}
	}

	private StocksTraderInstrument[] GetInstruments()
	{
		using (_sync.EnterScope())
			return [.. _instruments.Values];
	}

	private int GetInstrumentCount()
	{
		using (_sync.EnterScope())
			return _instruments.Count;
	}

	private StocksTraderClient Client
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private string PortfolioName
		=> _account?.Id ?? throw new InvalidOperationException(
			LocalizedStrings.AccountNotFound);

	private string ResolvePortfolio(string portfolioName)
	{
		var accountId = PortfolioName;
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(accountId))
			throw new InvalidOperationException(
				$"StocksTrader account '{portfolioName}' is unavailable in this session.");
		return accountId;
	}

	private string ResolveNativeId(string stringId, long? numericId,
		long originalTransactionId)
	{
		if (!stringId.IsEmpty())
			return stringId;

		using (_sync.EnterScope())
		{
			if (_transactionOrders.TryGetValue(originalTransactionId, out var mapped))
				return mapped;
			if (numericId is > 0)
			{
				var matches = _knownOrders.Concat(_knownDeals)
					.Where(id => id.ToNumericId() == numericId)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.Take(2).ToArray();
				if (matches.Length == 1)
					return matches[0];
				return numericId.Value.ToString(CultureInfo.InvariantCulture);
			}
		}

		throw new InvalidOperationException(
			LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
	}

	private async Task<NativeTarget> ResolveTargetAsync(string nativeId,
		CancellationToken cancellationToken)
	{
		var orders = await Client.GetOrdersAsync(PortfolioName, null,
			cancellationToken) ?? [];
		RegisterKnownOrders(orders);
		var order = orders.FirstOrDefault(item => item?.Id.EqualsIgnoreCase(nativeId) == true);
		if (order is not null)
			return new() { Order = order };

		var deals = await Client.GetDealsAsync(PortfolioName, null,
			cancellationToken) ?? [];
		RegisterKnownDeals(deals);
		var deal = deals.FirstOrDefault(item => item?.Id.EqualsIgnoreCase(nativeId) == true);
		return deal is not null
			? new() { Deal = deal }
			: throw new InvalidOperationException(
				$"StocksTrader active order or deal '{nativeId}' was not found.");
	}

	private void RegisterKnownOrders(IEnumerable<StocksTraderOrder> orders)
	{
		using (_sync.EnterScope())
		{
			foreach (var order in orders)
			{
				if (order?.Id.IsEmpty() == false)
					_knownOrders.Add(order.Id);
			}
		}
	}

	private void RegisterKnownDeals(IEnumerable<StocksTraderDeal> deals)
	{
		using (_sync.EnterScope())
		{
			foreach (var deal in deals)
			{
				if (deal?.Id.IsEmpty() == false)
					_knownDeals.Add(deal.Id);
			}
		}
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
		_account = null;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			_orderTransactions.Clear();
			_transactionOrders.Clear();
			_cancelTransactions.Clear();
			_orderSignatures.Clear();
			_reportedTrades.Clear();
			_knownOrders.Clear();
			_knownDeals.Clear();
			_positionTickers.Clear();
			_orderStatusSubscriptionId = 0;
			_portfolioSubscriptionId = 0;
			_portfolioFilter = null;
			_lastInstrumentRefresh = default;
			_lastOrderRefresh = default;
			_lastPortfolioRefresh = default;
			_lastConnectionCheck = default;
		}
	}
}
