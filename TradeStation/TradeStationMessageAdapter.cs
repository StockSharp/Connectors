namespace StockSharp.TradeStation;

public partial class TradeStationMessageAdapter
{
	private TradeStationClient _client;
	private TradeStationAccount[] _accounts = [];
	private CancellationTokenSource _streamCts;
	private CancellationTokenSource _quoteCts;
	private Task _orderStreamTask;
	private Task _positionStreamTask;
	private Task _quoteStreamTask;
	private readonly SynchronizedDictionary<long, SecurityId> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<string, decimal> _executedQuantities = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Initializes a new instance of the adapter.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public TradeStationMessageAdapter(IdGenerator transactionIdGenerator)
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
		TimeSpan.FromDays(1), TimeSpan.FromDays(7),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_client = new(IsDemo, Token);
		_accounts = (await _client.GetAccounts(cancellationToken))?.Accounts ?? [];
		_streamCts = new CancellationTokenSource();

		if (this.IsTransactional() && _accounts.Length > 0)
		{
			var accountIds = _accounts.Select(a => a.AccountId).ToArray();
			_orderStreamTask = RunStream(
				ct => _client.StreamOrders(accountIds, ProcessOrder, ct),
				_streamCts.Token);
			_positionStreamTask = RunStream(
				ct => _client.StreamPositions(accountIds, ProcessPosition, ct),
				_streamCts.Token);
		}

		await base.ConnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage message, CancellationToken cancellationToken)
	{
		CancelStreams();
		return base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
	{
		CancelStreams();
		_level1Subscriptions.Clear();
		_executedQuantities.Clear();
		_orderTransactions.Clear();
		_accounts = [];
		_client?.Dispose();
		_client = null;
		await base.ResetAsync(message, cancellationToken);
	}

	private Task RunStream(Func<CancellationToken, Task> stream, CancellationToken cancellationToken)
		=> RunStreamCore(stream, cancellationToken);

	private async Task RunStreamCore(Func<CancellationToken, Task> stream, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await stream(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
			{
				await SendOutErrorAsync(ex, CancellationToken.None);
				break;
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, CancellationToken.None);
			}

			if (!cancellationToken.IsCancellationRequested)
				await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
		}
	}

	private void CancelStreams()
	{
		_quoteCts?.Cancel();
		_quoteCts?.Dispose();
		_quoteCts = null;
		_streamCts?.Cancel();
		_streamCts?.Dispose();
		_streamCts = null;
		_quoteStreamTask = null;
		_orderStreamTask = null;
		_positionStreamTask = null;
	}
}
