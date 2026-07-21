namespace StockSharp.SynFutures;

public partial class SynFuturesMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_apiClient is not null || _rpcClient is not null ||
			_socketClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			ApiEndpoint = NormalizeEndpoint(ApiEndpoint, false,
				nameof(ApiEndpoint));
			WebSocketEndpoint = NormalizeEndpoint(WebSocketEndpoint, true,
				nameof(WebSocketEndpoint));
			RpcEndpoint = NormalizeEndpoint(RpcEndpoint, false,
				nameof(RpcEndpoint));
			_apiClient = new(ApiEndpoint) { Parent = this };
			_rpcClient = new(RpcEndpoint, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await RpcClient.VerifyChainAsync(cancellationToken);
			if (RpcClient.IsWalletConfigured)
			{
				WalletAddress = RpcClient.WalletAddress;
				_portfolioName = "SynFutures_Base_" +
					RpcClient.WalletAddress[2..10];
			}
			await RefreshMarketsAsync(cancellationToken);
			_socketClient = new(WebSocketEndpoint) { Parent = this };
			SocketClient.MarketChanged += OnMarketChangedAsync;
			SocketClient.DepthReceived += OnDepthAsync;
			SocketClient.TradesReceived += OnTradesAsync;
			SocketClient.KlineReceived += OnKlineAsync;
			SocketClient.PortfolioChanged += OnPortfolioChangedAsync;
			SocketClient.Error += OnSocketErrorAsync;
			SocketClient.StateChanged += OnSocketStateChangedAsync;
			await SocketClient.ConnectAsync(cancellationToken);
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
		CandleSubscription[] candles;
		var refreshAccount = false;
		using (_sync.EnterScope())
		{
			var now = DateTime.UtcNow;
			candles = [.. _candleSubscriptions.Values.Where(candle =>
				now >= candle.NextPollTime)];
			foreach (var candle in candles)
				candle.NextPollTime = now + GetCandlePollInterval(
					candle.TimeFrame);
			refreshAccount = _rpcClient?.IsWalletConfigured == true &&
				(_portfolioSubscriptionId != 0 ||
					_orderStatusSubscriptionId != 0) &&
				now - _lastAccountRefresh >= AccountRefreshInterval;
			if (refreshAccount)
				_lastAccountRefresh = now;
		}
		foreach (var candle in candles)
			await RunSafelyAsync(ct => PollCandleAsync(candle, ct),
				cancellationToken);
		if (refreshAccount)
			await RunSafelyAsync(RefreshAccountSubscriptionsAsync,
				cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var markets = (await ApiClient.GetMarketsAsync(cancellationToken) ?? [])
			.Where(static market => market is not null &&
				!market.Symbol.IsEmpty() &&
				!market.InstrumentAddress.IsEmpty() && market.Expiry > 0 &&
				market.BaseToken is not null && market.QuoteToken is not null)
			.ToArray();
		if (markets.Length == 0)
			throw new InvalidDataException(
				"SynFutures returned no usable perpetual markets.");
		foreach (var market in markets)
		{
			market.Symbol = market.Symbol.Trim().ToUpperInvariant();
			market.InstrumentAddress = market.InstrumentAddress.NormalizeAddress();
			if (market.UpdateTime > 0)
				UpdateServerTime(market.UpdateTime.ToUtc());
		}
		var duplicate = markets.GroupBy(static market => market.Symbol,
			StringComparer.OrdinalIgnoreCase).FirstOrDefault(
			static group => group.Count() > 1);
		if (duplicate is not null)
			throw new InvalidDataException(
				"SynFutures returned duplicate market symbol '" +
				duplicate.Key + "'.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByPair.Clear();
			foreach (var market in markets)
			{
				_markets.Add(market.Symbol, market);
				_marketsByPair.Add(PairKey(market.InstrumentAddress,
					market.Expiry), market);
			}
		}
	}

	private SynFuturesMarket StoreMarket(SynFuturesMarket market)
	{
		if (market?.InstrumentAddress.IsEmpty() != false || market.Expiry == 0)
			return null;
		market.InstrumentAddress = market.InstrumentAddress.NormalizeAddress();
		if (!market.Symbol.IsEmpty())
			market.Symbol = market.Symbol.Trim().ToUpperInvariant();
		if (market.UpdateTime > 0)
			UpdateServerTime(market.UpdateTime.ToUtc());
		using (_sync.EnterScope())
		{
			var key = PairKey(market.InstrumentAddress, market.Expiry);
			if (!_marketsByPair.TryGetValue(key, out var existing))
				return null;
			if (market.Symbol.IsEmpty())
				market.Symbol = existing.Symbol;
			market.BaseToken ??= existing.BaseToken;
			market.QuoteToken ??= existing.QuoteToken;
			market.FullSymbol ??= existing.FullSymbol;
			market.MarketType ??= existing.MarketType;
			if (market.MaximumLeverage <= 0)
				market.MaximumLeverage = existing.MaximumLeverage;
			_marketsByPair[key] = market;
			_markets[existing.Symbol] = market;
			return market;
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socketClient;
		var api = _apiClient;
		var rpc = _rpcClient;
		_socketClient = null;
		_apiClient = null;
		_rpcClient = null;
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
		rpc?.Dispose();
		ClearState();
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private ValueTask OnSocketStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
		=> state is ConnectionStates.Failed
			? SendOutConnectionStateAsync(state, cancellationToken)
			: default;

	private async ValueTask OnPortfolioChangedAsync(
		SynFuturesPortfolioNotification notification,
		CancellationToken cancellationToken)
	{
		if (notification is null ||
			!notification.UserAddress.IsEmpty() &&
			!notification.UserAddress.Equals(RpcClient.WalletAddress,
				StringComparison.OrdinalIgnoreCase))
			return;
		await RefreshAccountSubscriptionsAsync(cancellationToken);
	}

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

	private static TimeSpan GetCandlePollInterval(TimeSpan timeFrame)
		=> timeFrame <= TimeSpan.FromMinutes(1)
			? TimeSpan.FromSeconds(10)
			: timeFrame <= TimeSpan.FromHours(1)
				? TimeSpan.FromSeconds(30)
				: TimeSpan.FromMinutes(1);
}
