namespace StockSharp.GMTrade;

using Native;

public partial class GMTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(KeeperEndpoint, CandleEndpoint, IndexerEndpoint)
			{
				Parent = this,
			};
			_rpcClient = new(RpcEndpoint, WalletAddress) { Parent = this };
			if (_rpcClient.IsWalletAvailable)
			{
				WalletAddress = _rpcClient.WalletAddress;
				_portfolioName = CreatePortfolioName(WalletAddress);
			}
			await RefreshMarketsAsync(cancellationToken);

			_marketSocket = CreateMarketSocket();
			_candleSocket = CreateCandleSocket();
			await _marketSocket.ConnectAsync(cancellationToken);
			await _candleSocket.ConnectAsync(cancellationToken);
			_marketStreamId = await MarketSocket.SubscribeMarketsAsync(
				cancellationToken);
			var serverTime = ServerTime;
			_nextTradePoll = DateTime.UtcNow + TradePollingInterval;
			_nextBalancePoll = DateTime.UtcNow + BalancePollingInterval;
			_lastPublicTradePoll = serverTime.AddSeconds(-5);
			_lastAccountTradePoll = serverTime.AddSeconds(-5);
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
		var now = DateTime.UtcNow;
		if (_restClient is not null && now >= _nextTradePoll)
		{
			_nextTradePoll = now + TradePollingInterval;
			try
			{
				await PollTradesAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		if (_rpcClient?.IsWalletAvailable == true &&
			_portfolioSubscriptionId != 0 && now >= _nextBalancePoll)
		{
			_nextBalancePoll = now + BalancePollingInterval;
			try
			{
				await SendWalletBalancesAsync(_portfolioSubscriptionId, false,
					cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private GMTradeGraphQlWebSocketClient CreateMarketSocket()
	{
		var client = new GMTradeGraphQlWebSocketClient(KeeperSocketEndpoint,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.MarketReceived += OnMarketAsync;
		client.PositionReceived += OnPositionAsync;
		client.OrderReceived += OnOrderAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnMarketSocketStateAsync;
		return client;
	}

	private GMTradeGraphQlWebSocketClient CreateCandleSocket()
	{
		var client = new GMTradeGraphQlWebSocketClient(CandleSocketEndpoint,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.CandleReceived += OnCandleAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnCandleSocketStateAsync;
		return client;
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var source = (await RestClient.GetMarketsAsync(cancellationToken) ?? [])
			.Where(IsValidMarket)
			.GroupBy(static market => market.MarketToken, StringComparer.Ordinal)
			.Select(static group => group.Last())
			.OrderBy(static market => market.Meta.Name,
				StringComparer.OrdinalIgnoreCase)
			.ThenBy(static market => market.MarketToken, StringComparer.Ordinal)
			.ToArray();
		if (source.Length == 0)
			throw new InvalidDataException(
				"GMTrade returned no enabled perpetual markets.");

		var marketsByCode = new Dictionary<string, GMTradeMarketInfo>(
			StringComparer.OrdinalIgnoreCase);
		var marketsByToken = new Dictionary<string, GMTradeMarketInfo>(
			StringComparer.Ordinal);
		var tokensByMint = new Dictionary<string, GMTradeTokenInfo>(
			StringComparer.Ordinal)
		{
			[GMTradeRpcClient.SolMint] = new()
			{
				Mint = GMTradeRpcClient.SolMint,
				Symbol = "SOL",
				Decimals = 9,
			},
		};
		var serverTime = default(DateTime);
		foreach (var market in source)
		{
			var code = GMTradeExtensions.CreateSecurityCode(market.Meta.Name);
			if (marketsByCode.ContainsKey(code))
				code += "-" + market.MarketToken[..6].ToUpperInvariant();
			var info = new GMTradeMarketInfo
			{
				Code = code,
				Market = market,
			};
			marketsByCode.Add(code, info);
			marketsByToken.Add(market.MarketToken, info);
			AddToken(tokensByMint, market.Meta.IndexToken);
			AddToken(tokensByMint, market.Meta.LongToken);
			AddToken(tokensByMint, market.Meta.ShortToken);
			var timestamp = market.Meta.IndexToken.Price?.Timestamp ?? 0;
			if (timestamp > 0)
			{
				var time = timestamp.ToUtcTime();
				if (time > serverTime)
					serverTime = time;
			}
		}

		using (_sync.EnterScope())
		{
			_marketsByCode.Clear();
			_marketsByCode.AddRange(marketsByCode);
			_marketsByToken.Clear();
			_marketsByToken.AddRange(marketsByToken);
			_tokensByMint.Clear();
			_tokensByMint.AddRange(tokensByMint);
			if (serverTime > _serverTime)
				_serverTime = serverTime;
		}
	}

	private static bool IsValidMarket(GMTradeMarket market)
		=> market?.MarketToken.IsEmpty() == false &&
			market.Meta?.IsEnabled == true &&
			market.Meta.Name.IsEmpty() == false &&
			market.Meta.IndexToken?.PublicKey.IsEmpty() == false &&
			market.Meta.IndexToken.Meta is { Decimals: >= 0 and <= 18,
				Precision: >= 0 and <= 18 } &&
			market.Meta.IndexToken.Price is not null &&
			market.Meta.LongToken?.PublicKey.IsEmpty() == false &&
			market.Meta.LongToken.Meta is { Decimals: >= 0 and <= 18 } &&
			market.Meta.ShortToken?.PublicKey.IsEmpty() == false &&
			market.Meta.ShortToken.Meta is { Decimals: >= 0 and <= 18 };

	private static void AddToken(
		Dictionary<string, GMTradeTokenInfo> tokens, GMTradeToken token)
	{
		if (token?.PublicKey.IsEmpty() != false || token.Meta is null)
			return;
		tokens[token.PublicKey] = new()
		{
			Mint = token.PublicKey,
			Symbol = token.GetSymbol(),
			Decimals = token.Meta.Decimals,
		};
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var candleSocket = _candleSocket;
		var marketSocket = _marketSocket;
		var rpcClient = _rpcClient;
		var restClient = _restClient;
		_candleSocket = null;
		_marketSocket = null;
		_rpcClient = null;
		_restClient = null;
		foreach (var socket in new[] { candleSocket, marketSocket }
			.Where(static socket => socket is not null))
		{
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			socket.Dispose();
		}
		rpcClient?.Dispose();
		restClient?.Dispose();
		ClearState();
	}

	private async ValueTask OnMarketAsync(GMTradeMarket update,
		CancellationToken cancellationToken)
	{
		if (!IsValidMarket(update))
			return;
		GMTradeMarketInfo info;
		using (_sync.EnterScope())
		{
			if (!_marketsByToken.TryGetValue(update.MarketToken, out info))
				return;
			info.Market = update;
			if (update.Meta.IndexToken.Price.Timestamp > 0)
			{
				var time = update.Meta.IndexToken.Price.Timestamp.ToUtcTime();
				if (time > _serverTime)
					_serverTime = time;
			}
		}
		await PublishLevel1Async(info, cancellationToken);
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnMarketSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
		if (state != ConnectionStates.Restored)
			return;
		await RefreshMarketsAsync(cancellationToken);
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, true,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId,
				cancellationToken);
	}

	private async ValueTask OnCandleSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
	}
}
