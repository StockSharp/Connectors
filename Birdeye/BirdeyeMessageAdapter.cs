namespace StockSharp.Birdeye;

public partial class BirdeyeMessageAdapter
{
	private sealed class Level1Subscription
	{
		public BirdeyeToken Token { get; init; }
		public DateTime LastUpdate { get; set; }
	}

	private sealed class CandleSubscription
	{
		public BirdeyeToken Token { get; init; }
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, BirdeyeToken>
		_tokensByAddress =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BirdeyeToken>
		_tokensByCode =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private readonly SemaphoreSlim _streamSync = new(1, 1);
	private BirdeyeRestClient _restClient;
	private BirdeyeWebSocketClient _webSocketClient;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="BirdeyeMessageAdapter"/>.
	/// </summary>
	public BirdeyeMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.Birdeye];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> StreamingEnabled;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.IsAssociated(BoardCodes.Birdeye);

	private BirdeyeRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void RememberTokens(
		IEnumerable<BirdeyeToken> tokens)
	{
		using (_sync.EnterScope())
		{
			foreach (var token in tokens ?? [])
			{
				if (token?.Address.IsEmpty() != false)
					continue;
				_tokensByAddress[token.Address] = token;
				var securityCode =
					token.ToStockSharp().SecurityCode;
				if (!securityCode.IsEmpty())
					_tokensByCode[securityCode] = token;
				if (!token.Symbol.IsEmpty())
					_tokensByCode.TryAdd(
						token.Symbol, token);
			}
		}
	}

	private BirdeyeToken GetToken(SecurityId securityId)
	{
		using (_sync.EnterScope())
		{
			if (securityId.Native is string address &&
				!address.IsEmpty() &&
				_tokensByAddress.TryGetValue(
					address, out var token))
				return token;
			if (!securityId.SecurityCode.IsEmpty() &&
				_tokensByCode.TryGetValue(
					securityId.SecurityCode, out token))
				return token;
		}
		throw new InvalidOperationException(
			$"Unknown Birdeye token " +
				$"'{securityId.SecurityCode}'. Run security lookup first.");
	}

	private (
		string Address,
		string Interval)[] GetStreamSubscriptions()
	{
		using (_sync.EnterScope())
			return [..
				_level1Subscriptions.Values
					.Select(subscription => (
						subscription.Token.Address,
						TimeSpan.FromMinutes(1).ToInterval()))
				.Concat(
					_candleSubscriptions.Values.Select(
						subscription => (
							subscription.Token.Address,
							subscription.TimeFrame.ToInterval())))
				.Distinct()];
	}

	private async ValueTask RefreshStreamSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		if (!StreamingEnabled ||
			_webSocketClient is null)
			return;
		await _streamSync.WaitAsync(cancellationToken);
		try
		{
			await _webSocketClient.SubscribeAsync(
				GetStreamSubscriptions(),
				PriceInUsd,
				cancellationToken);
		}
		finally
		{
			_streamSync.Release();
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_tokensByAddress.Clear();
			_tokensByCode.Clear();
			_level1Subscriptions.Clear();
			_candleSubscriptions.Clear();
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_webSocketClient?.Dispose();
		_webSocketClient = null;
		_restClient?.Dispose();
		_restClient = null;
		_pollSync.Dispose();
		_streamSync.Dispose();
		base.DisposeManaged();
	}
}
