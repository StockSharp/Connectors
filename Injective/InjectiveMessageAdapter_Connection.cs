namespace StockSharp.Injective;

public partial class InjectiveMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _grpcClient is not null ||
			_chainSocketClient is not null || _signer is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (!System.Enum.IsDefined(Environment))
			throw new ArgumentOutOfRangeException(nameof(Environment), Environment,
				null);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(WalletAddress, PrivateKey)
			{
				CurrentSubaccount = checked((byte)SubaccountIndex),
			};
			WalletAddress = Signer.WalletAddress;
			if (Signer.IsWalletAvailable)
			{
				_subaccountId = Signer.CreateSubaccountId(
					checked((byte)SubaccountIndex));
				_portfolioName = "Injective_" +
					Signer.WalletAddress[4..12];
			}
			_restClient = new(
				IndexerEndpoint.IsEmpty()
					? Environment.IndexerEndpoint() : IndexerEndpoint,
				ChainEndpoint.IsEmpty()
					? Environment.ChainEndpoint() : ChainEndpoint)
			{
				Parent = this,
			};
			await RefreshMarketsAsync(cancellationToken);
			await RefreshBlockAsync(cancellationToken);
			_grpcClient = new(GrpcEndpoint.IsEmpty()
				? Environment.GrpcEndpoint() : GrpcEndpoint)
			{
				Parent = this,
			};
			GrpcClient.DepthReceived += OnDepthAsync;
			GrpcClient.TradeReceived += OnTradeAsync;
			GrpcClient.OrderReceived += OnOrderAsync;
			GrpcClient.PositionReceived += OnPositionAsync;
			GrpcClient.OraclePriceReceived += OnOraclePriceAsync;
			GrpcClient.PortfolioReceived += OnPortfolioUpdateAsync;
			GrpcClient.Error += OnGrpcErrorAsync;
			await GrpcClient.ConnectAsync(cancellationToken);
			_chainSocketClient = new(ChainSocketEndpoint.IsEmpty()
				? Environment.ChainSocketEndpoint() : ChainSocketEndpoint)
			{
				Parent = this,
			};
			_chainSocketClient.BlockReceived += OnChainBlockAsync;
			_chainSocketClient.Error += OnGrpcErrorAsync;
			_chainSocketClient.StateChanged += OnChainSocketStateAsync;
			await _chainSocketClient.ConnectAsync(cancellationToken);
			if (Signer.IsWalletAvailable)
			{
				await GrpcClient.SubscribeOrdersAsync(InjectiveMarketKinds.Spot,
					_subaccountId, cancellationToken);
				await GrpcClient.SubscribeOrdersAsync(
					InjectiveMarketKinds.Derivative, _subaccountId,
					cancellationToken);
				await GrpcClient.SubscribePositionsAsync(_subaccountId,
					cancellationToken);
				await GrpcClient.SubscribeAccountTradesAsync(
					InjectiveMarketKinds.Spot, _subaccountId,
					cancellationToken);
				await GrpcClient.SubscribeAccountTradesAsync(
					InjectiveMarketKinds.Derivative, _subaccountId,
					cancellationToken);
				await GrpcClient.SubscribePortfolioAsync(Signer.WalletAddress,
					_subaccountId, cancellationToken);
			}
			connectMsg.SessionId = $"Injective {Environment} " +
				(Signer.IsWalletAvailable
					? Signer.WalletAddress[..12] : "public");
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
		var refreshBlock = false;
		CandleSubscription[] candles;
		using (_sync.EnterScope())
		{
			refreshBlock = _restClient is not null &&
				CurrentTime >= _nextBlockRefresh;
			candles = [.. _candleSubscriptions.Values.Where(candle =>
				CurrentTime >= candle.NextPollTime)];
			foreach (var candle in candles)
				candle.NextPollTime = CurrentTime + GetCandlePollInterval(
					candle.TimeFrame);
		}
		if (refreshBlock)
			await RunSafelyAsync(RefreshBlockAsync, cancellationToken);
		foreach (var candle in candles)
			await RunSafelyAsync(ct => PollCandleAsync(candle, ct),
				cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var spotTask = RestClient.GetSpotMarketsAsync(cancellationToken).AsTask();
		var derivativeTask = RestClient.GetDerivativeMarketsAsync(
			cancellationToken).AsTask();
		await Task.WhenAll(spotTask, derivativeTask);
		var spots = await spotTask;
		var derivatives = await derivativeTask;
		var markets = new List<InjectiveMarket>(spots.Length +
			derivatives.Length);
		foreach (var source in spots.Where(static item => item is not null))
			markets.Add(source.ToMarket());
		foreach (var source in derivatives.Where(static item => item is not null))
			markets.Add(source.ToMarket());
		if (markets.Count == 0)
			throw new InvalidDataException(
				"Injective returned no usable markets.");
		foreach (var group in markets.GroupBy(static market => market.Code,
			StringComparer.OrdinalIgnoreCase).Where(static group =>
				group.Count() > 1))
			foreach (var market in group)
				market.Code += "-" + market.MarketId[2..10].ToUpperInvariant();
		using (_sync.EnterScope())
		{
			_marketsByCode.Clear();
			_marketsById.Clear();
			_tokensByDenom.Clear();
			foreach (var market in markets)
			{
				if (!_marketsByCode.TryAdd(market.Code, market) ||
					!_marketsById.TryAdd(market.MarketId, market))
					throw new InvalidDataException(
						$"Injective returned duplicate market '{market.Code}'.");
			}
			foreach (var source in spots)
			{
				if (source?.BaseTokenMeta is not null)
					_tokensByDenom[source.BaseDenom] = source.BaseTokenMeta;
				if (source?.QuoteTokenMeta is not null)
					_tokensByDenom[source.QuoteDenom] = source.QuoteTokenMeta;
			}
			foreach (var source in derivatives)
				if (source?.QuoteTokenMeta is not null)
					_tokensByDenom[source.QuoteDenom] = source.QuoteTokenMeta;
		}
	}

	private async ValueTask RefreshBlockAsync(
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetLatestBlockAsync(cancellationToken);
		var header = response?.Block?.Header ?? throw new InvalidDataException(
			"Injective returned no latest block header.");
		if (!long.TryParse(header.Height, NumberStyles.None,
			CultureInfo.InvariantCulture, out var height) || height <= 0 ||
			!DateTime.TryParse(header.Time, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
				out var time))
			throw new InvalidDataException(
				"Injective returned an invalid latest block header.");
		UpdateServerTime(time, height);
		using (_sync.EnterScope())
			_nextBlockRefresh = DateTime.UtcNow.AddSeconds(5);
	}

	private ValueTask OnGrpcErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private ValueTask OnChainBlockAsync(InjectiveBlockHeader header,
		CancellationToken cancellationToken)
	{
		if (!long.TryParse(header?.Height, NumberStyles.None,
			CultureInfo.InvariantCulture, out var height) || height <= 0 ||
			!DateTime.TryParse(header.Time, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
				out var time))
			return default;
		UpdateServerTime(time, height);
		return default;
	}

	private ValueTask OnChainSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
		=> state is ConnectionStates.Failed or ConnectionStates.Restored
			? SendOutConnectionStateAsync(state, cancellationToken)
			: default;

	private async ValueTask RunSafelyAsync(
		Func<CancellationToken, ValueTask> action,
		CancellationToken cancellationToken)
	{
		try
		{
			await action(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var grpc = _grpcClient;
		var chainSocket = _chainSocketClient;
		var rest = _restClient;
		var signer = _signer;
		_grpcClient = null;
		_chainSocketClient = null;
		_restClient = null;
		_signer = null;
		if (chainSocket is not null)
		{
			chainSocket.BlockReceived -= OnChainBlockAsync;
			chainSocket.Error -= OnGrpcErrorAsync;
			chainSocket.StateChanged -= OnChainSocketStateAsync;
			try
			{
				await chainSocket.DisconnectAsync(cancellationToken);
			}
			finally
			{
				chainSocket.Dispose();
			}
		}
		if (grpc is not null)
		{
			grpc.DepthReceived -= OnDepthAsync;
			grpc.TradeReceived -= OnTradeAsync;
			grpc.OrderReceived -= OnOrderAsync;
			grpc.PositionReceived -= OnPositionAsync;
			grpc.OraclePriceReceived -= OnOraclePriceAsync;
			grpc.PortfolioReceived -= OnPortfolioUpdateAsync;
			grpc.Error -= OnGrpcErrorAsync;
			try
			{
				await grpc.DisconnectAsync(cancellationToken);
			}
			finally
			{
				grpc.Dispose();
			}
		}
		rest?.Dispose();
		signer?.Dispose();
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_marketsByCode.Clear();
			_marketsById.Clear();
			_tokensByDenom.Clear();
			_lastPrices.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_knownOrders.Clear();
			_seenTrades.Clear();
			_subaccountId = null;
			_portfolioName = null;
			_serverTime = default;
			_nextBlockRefresh = default;
			_currentHeight = 0;
			_accountNumber = null;
			_nextSequence = null;
		}
	}

	private static TimeSpan GetCandlePollInterval(TimeSpan timeFrame)
		=> timeFrame <= TimeSpan.FromMinutes(1)
			? TimeSpan.FromSeconds(5)
			: timeFrame <= TimeSpan.FromHours(1)
				? TimeSpan.FromSeconds(15)
				: TimeSpan.FromMinutes(1);
}
