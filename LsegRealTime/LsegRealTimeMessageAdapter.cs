namespace StockSharp.LsegRealTime;

public partial class LsegRealTimeMessageAdapter
{
	private enum LsegMarketDataKinds
	{
		Level1,
		Ticks,
		MarketDepth,
	}

	private sealed class LsegMarketSubscription
	{
		public SecurityId SecurityId { get; init; }
		public LsegMarketDataKinds Kind { get; init; }
	}

	private readonly ConcurrentDictionary<long, LsegMarketSubscription> _marketSubscriptions = [];
	private readonly ConcurrentDictionary<long, LsegDepthBook> _depthBooks = [];

	private LsegRealTimeClient _client;
	private int _disconnectSignaled;

	/// <summary>Initializes a new instance of the adapter.</summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public LsegRealTimeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var client = new LsegRealTimeClient(new LsegClientConfiguration
		{
			AuthenticationMode = AuthenticationMode,
			Address = Address,
			StandbyAddress = StandbyAddress,
			IsHotStandby = IsHotStandby,
			Login = Login,
			Password = Password,
			ClientId = ClientId,
			Secret = Secret,
			ApplicationId = ApplicationId,
			Position = Position,
			Service = Service,
			Region = Region,
			Scope = Scope,
			AuthUrl = AuthUrl,
			DiscoveryUrl = DiscoveryUrl,
		});
		client.MarketPriceReceived += ProcessMarketPriceAsync;
		client.DepthReceived += ProcessDepthAsync;
		client.Error += SendOutErrorAsync;
		client.ConnectionLost += ProcessConnectionLostAsync;

		try
		{
			await client.ConnectAsync(cancellationToken);
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
		ClearRuntimeState();
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

		ClearRuntimeState();
		await base.ResetAsync(message, cancellationToken);
	}

	private void ClearRuntimeState()
	{
		_marketSubscriptions.Clear();
		_depthBooks.Clear();
	}

	private void DetachClient(LsegRealTimeClient client)
	{
		client.MarketPriceReceived -= ProcessMarketPriceAsync;
		client.DepthReceived -= ProcessDepthAsync;
		client.Error -= SendOutErrorAsync;
		client.ConnectionLost -= ProcessConnectionLostAsync;
	}

	private ValueTask ProcessConnectionLostAsync(Exception error, CancellationToken cancellationToken)
		=> Interlocked.Exchange(ref _disconnectSignaled, 1) == 0
			? SendOutDisconnectMessageAsync(error, CancellationToken.None)
			: default;

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		var client = _client;
		if (client != null)
		{
			Interlocked.Exchange(ref _disconnectSignaled, 1);
			DetachClient(client);
			client.Dispose();
			_client = null;
		}
		ClearRuntimeState();
		base.DisposeManaged();
	}
}
