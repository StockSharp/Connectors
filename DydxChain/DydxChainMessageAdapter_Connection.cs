namespace StockSharp.DydxChain;

public partial class DydxChainMessageAdapter
{
	private static readonly DydxChainSocketSubscriptionKey _marketsStream =
		new(DydxChainSocketChannels.Markets, null);

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_apiClient is not null || _socketClient is not null ||
			_signer is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(WalletAddress, PrivateKey);
			_apiClient = new(IndexerEndpoint, ValidatorEndpoint)
			{
				Parent = this,
			};
			await VerifyServicesAsync(cancellationToken);
			await RefreshMarketsAsync(cancellationToken);

			if (Signer.IsWalletAvailable)
			{
				WalletAddress = Signer.WalletAddress;
				_portfolioName = CreatePortfolioName(Signer.WalletAddress,
					SubaccountNumber);
			}

			_socketClient = CreateSocket();
			await SocketClient.ConnectAsync(cancellationToken);
			await SocketClient.SubscribeAsync(_marketsStream,
				cancellationToken);
			if (Signer.IsWalletAvailable)
				await SocketClient.SubscribeAsync(CreateSubaccountStream(),
					cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
			cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		EnsureConnected();
		await SocketClient.PingAsync(cancellationToken);
		await RefreshChainTipAsync(cancellationToken);
	}

	private DydxChainSocketClient CreateSocket()
	{
		var socket = new DydxChainSocketClient(WebSocketEndpoint,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		socket.MarketsSnapshotReceived += OnMarketsSnapshotAsync;
		socket.MarketsUpdateReceived += OnMarketsUpdateAsync;
		socket.OrderbookSnapshotReceived += OnOrderbookSnapshotAsync;
		socket.OrderbookUpdateReceived += OnOrderbookUpdateAsync;
		socket.TradesReceived += OnTradesAsync;
		socket.CandlesReceived += OnCandlesAsync;
		socket.SubaccountSnapshotReceived += OnSubaccountSnapshotAsync;
		socket.SubaccountUpdateReceived += OnSubaccountUpdateAsync;
		socket.Error += OnSocketErrorAsync;
		socket.StateChanged += OnSocketStateAsync;
		return socket;
	}

	private async ValueTask VerifyServicesAsync(
		CancellationToken cancellationToken)
	{
		var status = await ApiClient.GetValidatorStatusAsync(
			cancellationToken);
		if (status?.NodeInfo?.Network != DydxChainExtensions.ChainId)
			throw new InvalidDataException(
				$"dYdX validator belongs to chain " +
				$"'{status?.NodeInfo?.Network}', expected " +
				$"'{DydxChainExtensions.ChainId}'.");
		if (status.SyncInfo?.IsCatchingUp == true)
			throw new InvalidOperationException(
				"The configured dYdX validator is still catching up.");
		var height = status.SyncInfo?.LatestBlockHeight.ParseUInt32(
			"validator height") ?? throw new InvalidDataException(
				"dYdX validator returned no latest block height.");
		var time = status.SyncInfo.LatestBlockTime.ParseUtcTime(
			"validator block time");
		UpdateServer(time, height);
		var indexerTime = await ApiClient.GetTimeAsync(cancellationToken);
		UpdateServer(indexerTime.Iso.ParseUtcTime("Indexer time"));
	}

	private async ValueTask RefreshChainTipAsync(
		CancellationToken cancellationToken)
	{
		var height = await ApiClient.GetHeightAsync(cancellationToken);
		UpdateServer(height.Time.ParseUtcTime("Indexer block time"),
			height.Height.ParseUInt32("Indexer height"));
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var response = await ApiClient.GetMarketsAsync(cancellationToken);
		var markets = new List<DydxChainMarket>();
		foreach (var market in response ?? [])
		{
			if (market is null || market.Ticker.IsEmpty() ||
				market.ClobPairId.IsEmpty() ||
				market.TickSize.TryParseDecimal() is not > 0 ||
				market.StepSize.TryParseDecimal() is not > 0 ||
				market.StepBaseQuantums <= 0 || market.SubticksPerTick <= 0)
				continue;
			market.Ticker = market.Ticker.NormalizeTicker();
			_ = market.ClobPairId.ParseUInt32("CLOB pair ID");
			markets.Add(market);
		}
		if (markets.Count == 0)
			throw new InvalidDataException(
				"dYdX Indexer returned no usable perpetual markets.");
		var tickerDuplicate = markets.GroupBy(static market => market.Ticker,
			StringComparer.OrdinalIgnoreCase).FirstOrDefault(static group =>
				group.Count() > 1);
		if (tickerDuplicate is not null)
			throw new InvalidDataException(
				$"dYdX returned duplicate ticker '{tickerDuplicate.Key}'.");
		var pairDuplicate = markets.GroupBy(static market =>
			market.ClobPairId.ParseUInt32("CLOB pair ID")).FirstOrDefault(
				static group => group.Count() > 1);
		if (pairDuplicate is not null)
			throw new InvalidDataException(
				$"dYdX returned duplicate CLOB pair ID '{pairDuplicate.Key}'.");

		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByPairId.Clear();
			foreach (var market in markets)
			{
				_markets.Add(market.Ticker, market);
				_marketsByPairId.Add(market.ClobPairId.ParseUInt32(
					"CLOB pair ID"), market);
				if (market.OraclePrice.TryParseDecimal() is decimal oracle &&
					oracle > 0)
					_oraclePrices[market.Ticker] = oracle;
			}
		}
	}

	private DydxChainSocketSubscriptionKey CreateSubaccountStream()
		=> new(DydxChainSocketChannels.Subaccounts,
			Signer.WalletAddress + "/" + SubaccountNumber.ToString(
				CultureInfo.InvariantCulture));

	private static string CreatePortfolioName(string address,
		int subaccountNumber)
	{
		address = address.NormalizeAddress();
		return "dYdX_" + address[..address.Length.Min(12)] + "_" +
			subaccountNumber.ToString(CultureInfo.InvariantCulture);
	}

	private async ValueTask OnSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
		if (state != ConnectionStates.Restored)
			return;
		await RefreshChainTipAsync(cancellationToken);
		await RefreshMarketsAsync(cancellationToken);
		if (Signer.IsWalletAvailable)
			await SendAccountSnapshotToSubscribersAsync(cancellationToken);
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socketClient;
		var api = _apiClient;
		var signer = _signer;
		_socketClient = null;
		_apiClient = null;
		_signer = null;
		if (socket is not null)
		{
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			socket.Dispose();
		}
		api?.Dispose();
		signer?.Dispose();
		ClearState();
	}
}
