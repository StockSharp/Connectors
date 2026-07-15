namespace StockSharp.Public;

public partial class PublicMessageAdapter
{
	private readonly record struct Level1Subscription(SecurityId SecurityId, PublicInstrumentTypes InstrumentType);

	private PublicClient _client;
	private PublicAccount[] _accounts = [];
	private CancellationTokenSource _pollingCts;
	private Task _pollingTask;
	private readonly SynchronizedDictionary<long, Level1Subscription> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _trackedOrders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _executedQuantities = new(StringComparer.OrdinalIgnoreCase);
	private long _portfolioSubscriptionId;
	private long _orderSubscriptionId;

	/// <summary>
	/// Initializes a new instance of the adapter.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public PublicMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>
	/// Supported candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
		TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles ||
			dataType == DataType.Transactions || dataType == DataType.PositionChanges;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (PollingInterval <= TimeSpan.Zero)
			throw new InvalidOperationException(LocalizedStrings.InvalidValue.Put(PollingInterval));

		_client = new(Token);
		await _client.Authenticate(cancellationToken);
		_accounts = await _client.GetAccounts(cancellationToken);
		_pollingCts = new CancellationTokenSource();
		_pollingTask = Poll(_pollingCts.Token);
		await base.ConnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage message, CancellationToken cancellationToken)
	{
		StopPolling();
		return base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
	{
		StopPolling();
		_level1Subscriptions.Clear();
		_orderTransactions.Clear();
		_trackedOrders.Clear();
		_executedQuantities.Clear();
		_portfolioSubscriptionId = 0;
		_orderSubscriptionId = 0;
		_accounts = [];
		_client?.Dispose();
		_client = null;
		await base.ResetAsync(message, cancellationToken);
	}

	private async Task Poll(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(PollingInterval, cancellationToken);
				await PollQuotes(cancellationToken);
				await PollAccounts(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, CancellationToken.None);
			}
		}
	}

	private async Task PollQuotes(CancellationToken cancellationToken)
	{
		var subscriptions = _level1Subscriptions.ToArray();
		if (subscriptions.Length == 0)
			return;

		var account = ResolveAccount(null);
		foreach (var group in subscriptions.Select((item, index) => new { Item = item, Index = index }).GroupBy(p => p.Index / 100))
		{
			var items = group.Select(p => p.Item).ToArray();
			var instruments = items.Select(p => p.Value).DistinctBy(p => (p.SecurityId.SecurityCode.ToUpperInvariant(), p.InstrumentType))
				.Select(p => new PublicInstrumentKey { Symbol = p.SecurityId.SecurityCode, Type = p.InstrumentType }).ToArray();
			foreach (var quote in await _client.GetQuotes(account.AccountId, instruments, cancellationToken))
				await ProcessQuote(quote, items, cancellationToken);
		}
	}

	private async Task PollAccounts(CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 && _orderSubscriptionId == 0 && _trackedOrders.Count == 0)
			return;

		foreach (var account in _accounts)
		{
			var portfolio = await _client.GetPortfolio(account.AccountId, cancellationToken);
			if (_portfolioSubscriptionId != 0)
				await ProcessPortfolio(portfolio, _portfolioSubscriptionId, cancellationToken);

			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var order in portfolio?.Orders ?? [])
			{
				seen.Add(order.OrderId);
				await ProcessOrder(account.AccountId, order, _orderSubscriptionId, cancellationToken);
			}

			foreach (var tracked in _trackedOrders.Where(p => p.Value.EqualsIgnoreCase(account.AccountId)).ToArray())
			{
				if (seen.Contains(tracked.Key))
					continue;
				try
				{
					await ProcessOrder(account.AccountId, await _client.GetOrder(account.AccountId, tracked.Key, cancellationToken), _orderSubscriptionId, cancellationToken);
				}
				catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
				{
					continue;
				}
			}
		}
	}

	private void StopPolling()
	{
		_pollingCts?.Cancel();
		_pollingCts?.Dispose();
		_pollingCts = null;
		_pollingTask = null;
	}
}
