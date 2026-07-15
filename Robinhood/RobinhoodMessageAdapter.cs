namespace StockSharp.Robinhood;

public partial class RobinhoodMessageAdapter
{
	private RobinhoodMcpClient _client;
	private RobinhoodAccount[] _accounts = [];
	private CancellationTokenSource _pollingCts;
	private Task _pollingTask;
	private readonly SynchronizedDictionary<long, SecurityId> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _executedQuantities = new(StringComparer.OrdinalIgnoreCase);
	private long _portfolioSubscriptionId;
	private long _orderSubscriptionId;

	/// <summary>
	/// Initializes a new instance of the adapter.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public RobinhoodMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>
	/// Supported candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1), TimeSpan.FromHours(4), TimeSpan.FromDays(1),
		TimeSpan.FromDays(7), TimeSpan.FromDays(30),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Address is null)
			throw new InvalidOperationException("Robinhood MCP address is not specified.");
		if (PollingInterval <= TimeSpan.Zero)
			throw new InvalidOperationException(LocalizedStrings.InvalidValue.Put(PollingInterval));

		_client = new(Address, Token);
		await _client.Initialize(cancellationToken);
		_accounts = await _client.GetAccounts(cancellationToken) ?? [];
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
		foreach (var group in subscriptions
			.GroupBy(p => p.Value.SecurityCode, StringComparer.OrdinalIgnoreCase)
			.Select((p, index) => new { Item = p, Index = index })
			.GroupBy(p => p.Index / 20))
		{
			var items = group.SelectMany(p => p.Item).ToArray();
			foreach (var quote in await _client.GetQuotes(items.Select(p => p.Value.SecurityCode).Distinct(StringComparer.OrdinalIgnoreCase), cancellationToken) ?? [])
				await ProcessQuote(quote, items, cancellationToken);
		}
	}

	private async Task PollAccounts(CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 && _orderSubscriptionId == 0)
			return;

		foreach (var account in _accounts)
		{
			if (_portfolioSubscriptionId != 0)
			{
				await ProcessPortfolio(account, _portfolioSubscriptionId, cancellationToken);
				foreach (var position in await _client.GetPositions(account.AccountNumber, cancellationToken) ?? [])
					await ProcessPosition(account.AccountNumber, position, _portfolioSubscriptionId, cancellationToken);
			}

			if (_orderSubscriptionId != 0)
			{
				foreach (var order in await _client.GetOrders(account.AccountNumber, cancellationToken) ?? [])
					await ProcessOrder(account.AccountNumber, order, _orderSubscriptionId, cancellationToken);
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
