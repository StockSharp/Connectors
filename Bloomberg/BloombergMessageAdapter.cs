namespace StockSharp.Bloomberg;

public partial class BloombergMessageAdapter
{
	private enum BloombergMarketDataKinds
	{
		Level1,
		Ticks,
	}

	private sealed class BloombergMarketSubscription
	{
		public SecurityId SecurityId { get; init; }
		public BloombergMarketDataKinds Kind { get; init; }
	}

	private readonly ConcurrentDictionary<long, BloombergMarketSubscription> _marketSubscriptions = [];
	private readonly ConcurrentDictionary<long, BloombergEmsxOrderUpdate> _orders = [];
	private readonly ConcurrentDictionary<long, long> _orderTransactions = [];
	private readonly ConcurrentDictionary<long, long> _routeIds = [];
	private readonly ConcurrentDictionary<long, decimal> _filledQuantities = [];
	private readonly ConcurrentDictionary<string, byte> _announcedPortfolios = new(StringComparer.OrdinalIgnoreCase);

	private BloombergSdkClient _client;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private int _disconnectSignaled;

	/// <summary>Initializes a new instance of the adapter.</summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public BloombergMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <summary>Supported Bloomberg candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
		TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
	];

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var (host, port) = GetServerAddress();
		var client = new BloombergSdkClient(SdkPath, host, port, IsEmsxEnabled, EmsxService);
		client.MarketDataReceived += ProcessMarketDataAsync;
		client.EmsxOrderReceived += ProcessEmsxOrderAsync;
		client.Error += SendOutErrorAsync;
		client.ConnectionLost += ProcessConnectionLostAsync;

		try
		{
			client.Connect();
			_client = client;
			Interlocked.Exchange(ref _disconnectSignaled, 0);
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			DetachClient(client);
			client.Dispose();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message, CancellationToken cancellationToken)
	{
		var client = _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		Interlocked.Exchange(ref _disconnectSignaled, 1);
		await client.DisconnectAsync();
		DetachClient(client);
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
			Interlocked.Exchange(ref _disconnectSignaled, 1);
			await client.DisconnectAsync();
			DetachClient(client);
			client.Dispose();
			_client = null;
		}

		_marketSubscriptions.Clear();
		_orders.Clear();
		_orderTransactions.Clear();
		_routeIds.Clear();
		_filledQuantities.Clear();
		_announcedPortfolios.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		await base.ResetAsync(message, cancellationToken);
	}

	private (string host, int port) GetServerAddress()
		=> ServerAddress switch
		{
			DnsEndPoint dns => (dns.Host, dns.Port),
			IPEndPoint ip => (ip.Address.ToString(), ip.Port),
			_ => throw new InvalidOperationException("Bloomberg ServerAddress must be a DNS or IP endpoint."),
		};

	private void DetachClient(BloombergSdkClient client)
	{
		client.MarketDataReceived -= ProcessMarketDataAsync;
		client.EmsxOrderReceived -= ProcessEmsxOrderAsync;
		client.Error -= SendOutErrorAsync;
		client.ConnectionLost -= ProcessConnectionLostAsync;
	}

	private ValueTask ProcessConnectionLostAsync(Exception error, CancellationToken cancellationToken)
		=> Interlocked.Exchange(ref _disconnectSignaled, 1) == 0
			? SendOutDisconnectMessageAsync(error, CancellationToken.None)
			: default;
}
