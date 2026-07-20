namespace StockSharp.Ostium;

public partial class OstiumMessageAdapter
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
			var network = Environment.GetNetwork();
			_apiClient = new(BuilderEndpoint,
				SubgraphEndpoint.IsEmpty()
					? network.SubgraphEndpoint
					: SubgraphEndpoint)
			{
				Parent = this,
			};
			_rpcClient = new(network,
				RpcEndpoint.IsEmpty() ? network.RpcEndpoint : RpcEndpoint,
				WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await RpcClient.VerifyChainAsync(cancellationToken);
			if (RpcClient.IsWalletConfigured)
			{
				WalletAddress = RpcClient.WalletAddress;
				_portfolioName = "Ostium_" +
					(Environment == OstiumEnvironments.Mainnet
						? "Arbitrum_"
						: "Sepolia_") + RpcClient.WalletAddress[2..10];
			}
			await RefreshMarketsAsync(cancellationToken);
			_socketClient = new(PriceStreamEndpoint) { Parent = this };
			SocketClient.PriceReceived += OnPriceAsync;
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
		var pairsTask = ApiClient.GetPairsAsync(cancellationToken).AsTask();
		var pricesTask = ApiClient.GetPricesAsync(cancellationToken).AsTask();
		await Task.WhenAll(pairsTask, pricesTask);
		var prices = await pricesTask;
		var markets = new List<OstiumMarket>();
		foreach (var pair in await pairsTask)
		{
			if (pair is null || pair.Id.IsEmpty() || pair.From.IsEmpty() ||
				pair.To.IsEmpty() || !int.TryParse(pair.Id, NumberStyles.None,
					CultureInfo.InvariantCulture, out var pairIndex) ||
				pairIndex < 0)
				continue;
			var rawFrom = pair.From.Trim().ToUpperInvariant();
			var rawTo = pair.To.Trim().ToUpperInvariant();
			var from = rawFrom.NormalizePairName();
			var to = rawTo.NormalizePairName();
			var maximumLeverageRaw = pair.MaximumLeverage.IsEmpty() ||
				pair.MaximumLeverage == "0"
				? pair.Group?.MaximumLeverage
				: pair.MaximumLeverage;
			var longQuantity = pair.LongOpenInterest.TryParseScaled(18) ?? 0m;
			var shortQuantity = pair.ShortOpenInterest.TryParseScaled(18) ?? 0m;
			markets.Add(new()
			{
				PairIndex = pairIndex,
				RawFrom = rawFrom,
				RawTo = rawTo,
				BaseAsset = from,
				QuoteAsset = to,
				Symbol = from + "/" + to,
				ApiPair = rawFrom + "-" + rawTo,
				PricePair = from + "-" + to,
				Category = pair.Group?.Name?.Trim().ToUpperInvariant() ??
					"PERPETUAL",
				MaximumLeverage = maximumLeverageRaw.TryParseScaled(2) ?? 0m,
				OvernightMaximumLeverage =
					pair.OvernightMaximumLeverage.TryParseScaled(2) ?? 0m,
				TakerFeePercent = ParseRawDecimal(pair.TakerFeePercent,
					1_000_000m),
				MaximumOpenInterest =
					pair.MaximumOpenInterest.TryParseScaled(6) ?? 0m,
				LongOpenInterest = longQuantity,
				ShortOpenInterest = shortQuantity,
				PriceStep = 0.000001m,
				VolumeStep = 0.000001m,
			});
		}
		if (markets.Count == 0)
			throw new InvalidDataException(
				"Ostium returned no usable perpetual markets.");
		var duplicate = markets.GroupBy(static market => market.Symbol,
			StringComparer.OrdinalIgnoreCase).FirstOrDefault(
			static group => group.Count() > 1);
		if (duplicate is not null)
			throw new InvalidDataException(
				"Ostium returned duplicate market symbol '" +
				duplicate.Key + "'.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByIndex.Clear();
			_marketsByPricePair.Clear();
			foreach (var market in markets)
			{
				_markets.Add(market.Symbol, market);
				_marketsByIndex.Add(market.PairIndex, market);
				_marketsByPricePair.Add(market.PricePair, market);
			}
		}
		foreach (var price in prices?.Prices ?? [])
			if (price is not null)
				StorePrice(price);
	}

	private OstiumPrice StorePrice(OstiumPrice price)
	{
		if (price is null)
			return null;
		var key = price.Pair.IsEmpty()
			? price.From.NormalizePairName() + "-" +
				price.To.NormalizePairName()
			: price.Pair.Trim().ToUpperInvariant();
		if (key.IsEmpty())
			return null;
		if (price.TimestampSeconds > 0)
			UpdateServerTime(price.TimestampSeconds.FromUnix().EnsureOstiumUtc());
		using (_sync.EnterScope())
			_prices[key] = price;
		return price;
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
		=> SendOutConnectionStateAsync(state, cancellationToken);

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

	private static decimal ParseRawDecimal(string value, decimal scale)
	{
		if (value.IsEmpty() || !decimal.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var raw))
			return 0m;
		return raw / scale;
	}

	private static TimeSpan GetCandlePollInterval(TimeSpan timeFrame)
		=> timeFrame <= TimeSpan.FromMinutes(1)
			? TimeSpan.FromSeconds(5)
			: timeFrame <= TimeSpan.FromHours(1)
				? TimeSpan.FromSeconds(15)
				: TimeSpan.FromMinutes(1);
}
