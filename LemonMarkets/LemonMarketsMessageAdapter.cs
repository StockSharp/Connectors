namespace StockSharp.LemonMarkets;

public partial class LemonMarketsMessageAdapter
{
	private LemonClient _client;
	private LemonAccount _account;
	private LemonSecuritiesAccount[] _securitiesAccounts = [];
	private readonly CachedSynchronizedDictionary<string, LemonInstrument> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, SecurityId> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedDictionary<string, long> _cancelTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _orderSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _executedValues =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _processedTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _reportedTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _processedEvents =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly CachedSynchronizedSet<string> _positionIsins =
		new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private string _idempotencyPrefix;
	private DateTime _eventSince;
	private DateTime _lastInstrumentRefresh;
	private DateTime _lastLevel1Refresh;
	private DateTime _lastEventRefresh;
	private DateTime _lastPortfolioRefresh;
	private DateTime _lastConnectionCheck;

	/// <summary>Initializes a new instance of the <see cref="LemonMarketsMessageAdapter"/> class.</summary>
	public LemonMarketsMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
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
	public override string[] AssociatedBoards { get; } = [LemonMarketsExtensions.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ClearState();

		var apiKey = ApiKey?.UnSecure().ThrowIfEmpty(nameof(ApiKey));
		var principal = DataPrivacyPrincipal.ThrowIfEmpty(nameof(DataPrivacyPrincipal));
		var justification = DataPrivacyJustification.ThrowIfEmpty(nameof(DataPrivacyJustification));
		var client = new LemonClient(apiKey, IsDemo, principal, justification,
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
			_securitiesAccounts =
			[
				.. (await client.GetSecuritiesAccounts(_account.Id, cancellationToken) ?? [])
					.Where(account => account?.Id.IsEmpty() == false),
			];
			ValidateDefaultSecuritiesAccount();
			_idempotencyPrefix = Guid.NewGuid().ToString("N");
			_eventSince = CurrentTime;
			_lastConnectionCheck = CurrentTime;
			connectMsg.SessionId = $"lemon.markets {_account.Id}";
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
		if (_client != null && _level1Subscriptions.Count > 0 &&
			now - _lastLevel1Refresh >= PollingInterval)
		{
			try
			{
				await RefreshLevel1(cancellationToken);
			}
			catch (Exception error)
			{
				_lastLevel1Refresh = now;
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		if (_client != null && _orderStatusSubscriptionId != 0 &&
			now - _lastEventRefresh >= PollingInterval)
		{
			try
			{
				await RefreshEvents(cancellationToken);
			}
			catch (Exception error)
			{
				_lastEventRefresh = now;
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
				_account = await _client.GetAccount(_account.Id, cancellationToken);
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

	private LemonAccount SelectAccount(LemonAccount[] accounts)
	{
		if (!AccountId.IsEmpty())
			return accounts.FirstOrDefault(account => account.Id.EqualsIgnoreCase(AccountId))
				?? throw new InvalidOperationException($"lemon.markets account '{AccountId}' was not found.");

		var opened = accounts.Where(account => account.Status.EqualsIgnoreCase("opened")).ToArray();
		if (opened.Length == 1)
			return opened[0];
		if (accounts.Length == 1)
			return accounts[0];
		if (accounts.Length == 0)
			throw new InvalidOperationException("The lemon.markets API key exposes no customer accounts.");
		throw new InvalidOperationException(
			"The lemon.markets API key exposes multiple customer accounts. Configure AccountId explicitly.");
	}

	private void ValidateDefaultSecuritiesAccount()
	{
		if (SecuritiesAccountId.IsEmpty())
			return;
		ResolveSecuritiesAccount(SecuritiesAccountId, true);
	}

	private string ResolvePortfolio(string portfolioName)
	{
		var accountId = _account?.Id
			?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(accountId))
			throw new InvalidOperationException(
				$"lemon.markets account '{portfolioName}' is not available in this adapter session.");
		return accountId;
	}

	private string ResolveSecuritiesAccount(string requested, bool isRequired)
	{
		requested = requested.IsEmpty(SecuritiesAccountId);
		if (!requested.IsEmpty())
		{
			var match = _securitiesAccounts.FirstOrDefault(account =>
				account.Id.EqualsIgnoreCase(requested) ||
				account.AccountNumber.EqualsIgnoreCase(requested) ||
				account.Number.EqualsIgnoreCase(requested));
			return match?.Id ?? throw new InvalidOperationException(
				$"lemon.markets securities account '{requested}' was not found.");
		}

		if (_securitiesAccounts.Length == 1)
			return _securitiesAccounts[0].Id;
		if (_securitiesAccounts.Length == 0 &&
			_account?.SecuritiesAccount?.Id.IsEmpty() == false)
			return _account.SecuritiesAccount.Id;
		if (isRequired && _securitiesAccounts.Length > 1)
			throw new InvalidOperationException(
				"This lemon.markets customer has multiple securities accounts. Specify SecuritiesAccountId.");
		return null;
	}

	private async Task<LemonInstrument> ResolveInstrument(string isin,
		CancellationToken cancellationToken)
	{
		isin = isin?.ToUpperInvariant().ThrowIfEmpty(nameof(isin));
		if (!isin.IsIsin())
			throw new ArgumentException($"'{isin}' is not a valid ISIN.", nameof(isin));
		if (_instruments.TryGetValue(isin, out var instrument))
			return instrument;
		instrument = await _client.GetInstrument(isin, cancellationToken)
			?? throw new InvalidOperationException($"lemon.markets instrument '{isin}' was not found.");
		_instruments[isin] = instrument;
		return instrument;
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
		_account = null;
		_securitiesAccounts = [];
	}

	private void ClearState()
	{
		_instruments.Clear();
		_level1Subscriptions.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_cancelTransactions.Clear();
		_orderSignatures.Clear();
		_executedValues.Clear();
		_processedTrades.Clear();
		_reportedTrades.Clear();
		_processedEvents.Clear();
		_positionIsins.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioFilter = null;
		_idempotencyPrefix = null;
		_eventSince = default;
		_lastInstrumentRefresh = default;
		_lastLevel1Refresh = default;
		_lastEventRefresh = default;
		_lastPortfolioRefresh = default;
		_lastConnectionCheck = default;
	}
}
