namespace StockSharp.OpenMarkets;

public partial class OpenMarketsMessageAdapter
{
	private sealed class DepthSubscription
	{
		public SecurityId SecurityId { get; set; }
		public int Depth { get; set; }
	}

	private OpenMarketsClient _client;
	private OpenMarketsStreamingClient _streams;
	private OpenMarketsAccount[] _accounts = [];
	private OpenMarketsPortfolioLink[] _portfolioLinks = [];
	private readonly SynchronizedDictionary<long, SecurityId> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<long, SecurityId> _tickSubscriptions = [];
	private readonly SynchronizedDictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly SynchronizedDictionary<long, string> _orderStatusSubscriptions = [];
	private readonly SynchronizedDictionary<long, string> _portfolioSubscriptions = [];
	private readonly SynchronizedDictionary<long, long> _orderTransactions = [];
	private readonly SynchronizedSet<long> _reportedTrades = [];
	private readonly SynchronizedSet<string> _reportedMarketTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly CachedSynchronizedDictionary<string, decimal> _priceMultipliers =
		new(StringComparer.OrdinalIgnoreCase);
	private DateTime _lastDepthPoll;
	private int _depthCursor;

	/// <summary>Initializes a new instance of the <see cref="OpenMarketsMessageAdapter"/> class.</summary>
	public OpenMarketsMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <summary>Supported REST time-series intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(6),
		TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
		TimeSpan.FromDays(1), TimeSpan.FromDays(7),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Asx];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		var client = new OpenMarketsClient(IsTest,
			Key?.UnSecure().ThrowIfEmpty(nameof(Key)),
			Secret?.UnSecure().ThrowIfEmpty(nameof(Secret)));
		_client = client;
		try
		{
			var accounts = await client.GetAccounts(cancellationToken) ?? [];
			if (!AccountCode.IsEmpty())
				accounts = accounts.Where(account => account.AccountCode.EqualsIgnoreCase(AccountCode)).ToArray();
			if (accounts.Length == 0)
				throw new InvalidOperationException(AccountCode.IsEmpty()
					? "OpenMarkets exposes no order accounts to this API client."
					: $"OpenMarkets account '{AccountCode}' was not found.");

			_accounts = accounts;
			var accountCodes = accounts.Select(account => account.AccountCode)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			_portfolioLinks = (await client.GetPortfolioLinks(cancellationToken) ?? [])
				.Where(link => !link.IsLinkRemoved && accountCodes.Contains(link.AccountCode) &&
					!link.PortfolioCode.IsEmpty())
				.ToArray();
			_streams = new(client, IsTest, ProcessStreamQuotes, ProcessStreamMarketTrades,
				ProcessStreamOrders, ProcessStreamTrades, ProcessStreamPositions,
				ProcessStreamCash, SendStreamError);

			message.SessionId = $"OpenMarkets {accounts.Select(a => a.AccountCode).JoinComma()}";
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		try
		{
			if (_streams != null)
				await _streams.StopAsync(cancellationToken);
		}
		finally
		{
			DisposeClients();
			ClearState();
		}
		await base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message,
		CancellationToken cancellationToken)
	{
		if (_streams != null)
		{
			try
			{
				await _streams.StopAsync(cancellationToken);
			}
			catch (Exception error)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
		DisposeClients();
		ClearState();
		await base.ResetAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage message,
		CancellationToken cancellationToken)
	{
		if (_client != null && _depthSubscriptions.Count > 0 &&
			CurrentTime - _lastDepthPoll >= DepthPollingInterval)
		{
			var subscriptions = _depthSubscriptions.ToArray();
			if (subscriptions.Length > 0)
			{
				var subscription = subscriptions[_depthCursor++ % subscriptions.Length];
				try
				{
					await RefreshDepth(subscription.Key, subscription.Value, cancellationToken);
				}
				catch (Exception error)
				{
					await SendOutErrorAsync(error, cancellationToken);
				}
			}
			_lastDepthPoll = CurrentTime;
		}
		await base.TimeAsync(message, cancellationToken);
	}

	private string ResolveAccount(string portfolioName)
	{
		if (!portfolioName.IsEmpty())
			return _accounts.FirstOrDefault(account =>
				account.AccountCode.EqualsIgnoreCase(portfolioName))?.AccountCode
				?? throw new InvalidOperationException($"OpenMarkets account '{portfolioName}' was not found.");
		if (_accounts.Length == 1)
			return _accounts[0].AccountCode;
		throw new InvalidOperationException("PortfolioName must identify an OpenMarkets account when several accounts are accessible.");
	}

	private string ResolveAccountForPortfolio(string portfolioCode, string accountCode)
	{
		if (!accountCode.IsEmpty() && _accounts.Any(account =>
			account.AccountCode.EqualsIgnoreCase(accountCode)))
			return accountCode;
		return _portfolioLinks.FirstOrDefault(link =>
			link.PortfolioCode.EqualsIgnoreCase(portfolioCode))?.AccountCode;
	}

	private decimal GetMultiplier(SecurityId securityId)
	{
		var key = GetSecurityKey(securityId.SecurityCode,
			securityId.BoardCode.IsEmpty(DefaultExchange));
		return _priceMultipliers.TryGetValue2(key)
			.NormalizeMultiplier(DefaultPriceMultiplier > 0 ? DefaultPriceMultiplier : 0.01m);
	}

	private void SetMultiplier(string code, string exchange, decimal? multiplier)
	{
		if (code.IsEmpty())
			return;

		var key = GetSecurityKey(code, exchange.IsEmpty(DefaultExchange));
		if (multiplier is > 0)
			_priceMultipliers[key] = multiplier.Value;
		else if (!_priceMultipliers.ContainsKey(key))
			_priceMultipliers[key] = DefaultPriceMultiplier > 0 ? DefaultPriceMultiplier : 0.01m;
	}

	private static string GetSecurityKey(string code, string exchange)
		=> $"{code}.{exchange}";

	private void DisposeClients()
	{
		_streams?.Dispose();
		_streams = null;
		_client?.Dispose();
		_client = null;
	}

	private void ClearState()
	{
		_accounts = [];
		_portfolioLinks = [];
		_level1Subscriptions.Clear();
		_tickSubscriptions.Clear();
		_depthSubscriptions.Clear();
		_orderStatusSubscriptions.Clear();
		_portfolioSubscriptions.Clear();
		_orderTransactions.Clear();
		_reportedTrades.Clear();
		_reportedMarketTrades.Clear();
		_priceMultipliers.Clear();
		_lastDepthPoll = default;
		_depthCursor = 0;
	}

	private ValueTask SendStreamError(Exception error, CancellationToken cancellationToken)
		=> error == null ? default : SendOutErrorAsync(error, cancellationToken);
}
