namespace StockSharp.SnapTrade;

public partial class SnapTradeMessageAdapter
{
	private SnapTradeClient _client;
	private SnapTradeAccount _account;
	private readonly CachedSynchronizedDictionary<string, SnapTradeUniversalSymbol> _symbols =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, SecurityId> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedDictionary<string, long> _cancelTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _orderSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _filledQuantities =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly CachedSynchronizedDictionary<string, SecurityId> _positionIds =
		new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastPoll;
	private DateTime _lastConnectionCheck;
	private int _pollCursor;
	private int _quoteCursor;
	private int _orderPollCursor;

	/// <summary>Initializes a new instance of the <see cref="SnapTradeMessageAdapter"/> class.</summary>
	public SnapTradeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [SnapTradeExtensions.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ClearState();

		var clientId = ClientId.ThrowIfEmpty(nameof(ClientId));
		var consumerKey = ConsumerKey?.UnSecure().ThrowIfEmpty(nameof(ConsumerKey));
		var userSecret = UserSecret?.UnSecure();
		var client = new SnapTradeClient(clientId, consumerKey, UserId, userSecret,
			Math.Max(1, ReConnectionSettings.ReAttemptCount))
		{
			Parent = this,
		};
		_client = client;
		try
		{
			var accounts = (await client.GetAccounts(cancellationToken) ?? [])
				.Where(account => account?.Id.IsEmpty() == false)
				.ToArray();
			_account = SelectAccount(accounts);
			_lastConnectionCheck = CurrentTime;
			connectMsg.SessionId = $"SnapTrade {_account.Id}";
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
		if (_client != null && now - _lastPoll >= PollingInterval)
		{
			try
			{
				if (await RunNextPollingJob(cancellationToken))
					_lastConnectionCheck = now;
				else if (now - _lastConnectionCheck >= TimeSpan.FromMinutes(2))
				{
					_account = await _client.GetAccount(_account.Id, cancellationToken)
						?? throw new InvalidOperationException("SnapTrade returned no account detail.");
					_lastConnectionCheck = now;
				}
			}
			catch (Exception error)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				_lastPoll = now;
			}
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async Task<bool> RunNextPollingJob(CancellationToken cancellationToken)
	{
		for (var i = 0; i < 3; i++)
		{
			var job = _pollCursor++ % 3;
			switch (job)
			{
				case 0 when _orderStatusSubscriptionId != 0:
					await SendOrderSnapshot(_orderStatusSubscriptionId, null, false,
						cancellationToken);
					return true;
				case 1 when _portfolioSubscriptionId != 0:
					await SendPortfolioSnapshot(_portfolioSubscriptionId, _portfolioFilter, false,
						cancellationToken);
					return true;
				case 2 when _level1Subscriptions.Count > 0:
					await RefreshLevel1(cancellationToken);
					return true;
			}
		}
		return false;
	}

	private SnapTradeAccount SelectAccount(SnapTradeAccount[] accounts)
	{
		if (!AccountId.IsEmpty())
			return accounts.FirstOrDefault(account => account.Id.EqualsIgnoreCase(AccountId) ||
				account.Number.EqualsIgnoreCase(AccountId))
				?? throw new InvalidOperationException($"SnapTrade account '{AccountId}' was not found.");

		var usable = accounts.Where(account =>
			!account.Status.EqualsIgnoreCase("closed") &&
			!account.Status.EqualsIgnoreCase("archived") &&
			!account.Status.EqualsIgnoreCase("unavailable") &&
			(account.AccountCategory.IsEmpty() || account.AccountCategory.EqualsIgnoreCase("INVESTMENT")))
			.ToArray();
		if (usable.Length == 1)
			return usable[0];
		if (accounts.Length == 1)
			return accounts[0];
		if (accounts.Length == 0)
			throw new InvalidOperationException("SnapTrade exposes no brokerage accounts for this user.");
		throw new InvalidOperationException(
			"SnapTrade exposes multiple usable accounts. Configure AccountId explicitly.");
	}

	private string ResolvePortfolio(string portfolioName)
	{
		var accountId = _account?.Id
			?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(accountId) &&
			!portfolioName.EqualsIgnoreCase(_account.Number))
			throw new InvalidOperationException(
				$"SnapTrade account '{portfolioName}' is not available in this adapter session.");
		return accountId;
	}

	private async Task<SnapTradeUniversalSymbol> ResolveSymbol(string code,
		CancellationToken cancellationToken)
	{
		code = code.ThrowIfEmpty(nameof(code));
		if (_symbols.TryGetValue(code, out var symbol))
			return symbol;

		var symbols = await _client.SearchSymbols(ResolvePortfolio(null), code,
			cancellationToken) ?? [];
		CacheSymbols(symbols);
		symbol = symbols.FirstOrDefault(item => item?.Symbol.EqualsIgnoreCase(code) == true ||
			item?.RawSymbol.EqualsIgnoreCase(code) == true) ?? symbols.FirstOrDefault();
		return symbol ?? throw new InvalidOperationException(
			$"SnapTrade found no brokerage-supported symbol for '{code}'.");
	}

	private void CacheSymbols(IEnumerable<SnapTradeUniversalSymbol> symbols)
	{
		foreach (var symbol in symbols ?? [])
		{
			if (symbol?.Symbol.IsEmpty() == false)
				_symbols[symbol.Symbol] = symbol;
			if (symbol?.RawSymbol.IsEmpty() == false)
				_symbols[symbol.RawSymbol] = symbol;
			if (symbol?.Id.IsEmpty() == false)
				_symbols[symbol.Id] = symbol;
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
		_symbols.Clear();
		_level1Subscriptions.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_cancelTransactions.Clear();
		_orderSignatures.Clear();
		_filledQuantities.Clear();
		_positionIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioFilter = null;
		_lastPoll = default;
		_lastConnectionCheck = default;
		_pollCursor = 0;
		_quoteCursor = 0;
		_orderPollCursor = 0;
	}
}
