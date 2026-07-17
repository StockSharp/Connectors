namespace StockSharp.Qmt;

using Native;
using Native.Model;

public partial class QmtMessageAdapter
{
	private sealed class MarketSubscription
	{
		public SecurityId SecurityId { get; init; }
		public DataType DataType { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public int MaxDepth { get; init; }
		public QmtCandle ActiveCandle { get; set; }
	}

	private readonly ConcurrentDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly ConcurrentDictionary<long, long> _orderTransactions = [];
	private readonly ConcurrentDictionary<string, byte> _tradeIds = new(StringComparer.OrdinalIgnoreCase);

	private QmtGatewayClient _client;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioName;
	private DateTime _lastPortfolioRefresh;
	private int _isReconnecting;

	/// <summary>Initializes a new instance of the adapter.</summary>
	public QmtMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(QmtExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [1, 5, 10];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Sse, BoardCodes.Szse, BoardCodes.Bse];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (RequestTimeout <= 0)
			throw new ArgumentOutOfRangeException(nameof(RequestTimeout));

		var client = new QmtGatewayClient(
			GatewayHost,
			GatewayPort,
			GatewayToken?.UnSecure(),
			ReconnectAttempts,
			TimeSpan.FromSeconds(RequestTimeout));
		AttachClient(client);

		try
		{
			await client.ConnectAsync(cancellationToken);
			_client = client;
			Interlocked.Exchange(ref _isReconnecting, 0);
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			DetachClient(client);
			await client.DisconnectAsync();
			client.Dispose();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message, CancellationToken cancellationToken)
	{
		var client = _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DetachClient(client);
		await client.DisconnectAsync();
		client.Dispose();
		_client = null;
		await base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
	{
		var client = _client;
		if (client != null)
		{
			DetachClient(client);
			await client.DisconnectAsync();
			client.Dispose();
			_client = null;
		}

		_marketSubscriptions.Clear();
		_orderTransactions.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioName = null;
		_lastPortfolioRefresh = default;
		Interlocked.Exchange(ref _isReconnecting, 0);
		await base.ResetAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage message, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId != 0 &&
			DateTime.UtcNow - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
		{
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, _portfolioName, cancellationToken);
			_lastPortfolioRefresh = DateTime.UtcNow;
		}

		await base.TimeAsync(message, cancellationToken);
	}

	private QmtGatewayClient EnsureClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void AttachClient(QmtGatewayClient client)
	{
		client.Level1Received += ProcessLevel1Async;
		client.DepthReceived += ProcessDepthAsync;
		client.TradeReceived += ProcessMarketTradeAsync;
		client.CandleReceived += ProcessCandleAsync;
		client.OrderReceived += ProcessOrderAsync;
		client.FillReceived += ProcessFillAsync;
		client.AssetReceived += ProcessAssetAsync;
		client.PositionReceived += ProcessPositionAsync;
		client.Error += SendOutErrorAsync;
		client.StateChanged += ProcessConnectionStateAsync;
	}

	private void DetachClient(QmtGatewayClient client)
	{
		client.Level1Received -= ProcessLevel1Async;
		client.DepthReceived -= ProcessDepthAsync;
		client.TradeReceived -= ProcessMarketTradeAsync;
		client.CandleReceived -= ProcessCandleAsync;
		client.OrderReceived -= ProcessOrderAsync;
		client.FillReceived -= ProcessFillAsync;
		client.AssetReceived -= ProcessAssetAsync;
		client.PositionReceived -= ProcessPositionAsync;
		client.Error -= SendOutErrorAsync;
		client.StateChanged -= ProcessConnectionStateAsync;
	}

	private ValueTask ProcessConnectionStateAsync(ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Disconnected)
		{
			if (Interlocked.Exchange(ref _isReconnecting, 1) == 0)
				return SendOutConnectionStateAsync(ConnectionStates.Reconnecting, cancellationToken);
		}
		else if (state == ConnectionStates.Connected && Interlocked.Exchange(ref _isReconnecting, 0) != 0)
			return SendOutConnectionStateAsync(ConnectionStates.Restored, cancellationToken);

		return default;
	}
}
