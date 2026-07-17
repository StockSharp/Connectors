namespace StockSharp.TradingTechnologies;

using Native;

public partial class TradingTechnologiesMessageAdapter
{
	private sealed class MarketSubscription
	{
		public SecurityId SecurityId { get; init; }
		public TradingTechnologiesMarketDataKinds Kind { get; init; }
	}

	private readonly ConcurrentDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly ConcurrentDictionary<ulong, SecurityId> _securityIds = [];
	private readonly ConcurrentDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, byte> _fills = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, byte> _announcedPortfolios = new(StringComparer.OrdinalIgnoreCase);

	private TradingTechnologiesSdkClient _client;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private int _isReconnecting;

	/// <summary>Initializes a new instance of the adapter.</summary>
	public TradingTechnologiesMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.TradingTechnologies, "CME", "CBOT", "COMEX", "NYMEX", "ICE"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var appSecretKey = AppSecretKey?.UnSecure();
		if (appSecretKey.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.PasswordNotSpecified);
		if (InitializationTimeout <= 0)
			throw new ArgumentOutOfRangeException(nameof(InitializationTimeout));
		if (MarketDepth <= 0)
			throw new ArgumentOutOfRangeException(nameof(MarketDepth));

		var client = new TradingTechnologiesSdkClient(
			SdkPath,
			appSecretKey,
			Environment.ToString(),
			InitializationTimeout,
			MarketDepth,
			IsBinaryProtocol,
			IsOptionsEnabled);
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
		_securityIds.Clear();
		_orderTransactions.Clear();
		_fills.Clear();
		_announcedPortfolios.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		Interlocked.Exchange(ref _isReconnecting, 0);
		await base.ResetAsync(message, cancellationToken);
	}

	private TradingTechnologiesSdkClient EnsureClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void AttachClient(TradingTechnologiesSdkClient client)
	{
		client.Level1Received += ProcessLevel1Async;
		client.DepthReceived += ProcessDepthAsync;
		client.TickReceived += ProcessTickAsync;
		client.OrderReceived += ProcessOrderAsync;
		client.FillReceived += ProcessFillAsync;
		client.PositionReceived += ProcessPositionAsync;
		client.Error += ProcessErrorAsync;
		client.ConnectionStateChanged += ProcessConnectionStateAsync;
	}

	private void DetachClient(TradingTechnologiesSdkClient client)
	{
		client.Level1Received -= ProcessLevel1Async;
		client.DepthReceived -= ProcessDepthAsync;
		client.TickReceived -= ProcessTickAsync;
		client.OrderReceived -= ProcessOrderAsync;
		client.FillReceived -= ProcessFillAsync;
		client.PositionReceived -= ProcessPositionAsync;
		client.Error -= ProcessErrorAsync;
		client.ConnectionStateChanged -= ProcessConnectionStateAsync;
	}

	private ValueTask ProcessErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private ValueTask ProcessConnectionStateAsync(bool isReady, string status, CancellationToken cancellationToken)
	{
		if (!isReady)
		{
			if (Interlocked.Exchange(ref _isReconnecting, 1) == 0)
				return SendOutConnectionStateAsync(ConnectionStates.Reconnecting, cancellationToken);
		}
		else if (Interlocked.Exchange(ref _isReconnecting, 0) != 0)
			return SendOutConnectionStateAsync(ConnectionStates.Restored, cancellationToken);

		return default;
	}

	private SecurityId ResolveSecurityId(TradingTechnologiesInstrument instrument)
		=> _securityIds.GetOrAdd(instrument.Id, _ => instrument.ToSecurityId());
}
