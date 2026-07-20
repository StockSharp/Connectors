namespace StockSharp.Gmx;

public partial class GmxMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_apiClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			ApiEndpoint = NormalizeEndpoint(ApiEndpoint, nameof(ApiEndpoint));
			SecondaryApiEndpoint = NormalizeEndpoint(SecondaryApiEndpoint,
				nameof(SecondaryApiEndpoint));
			_apiClient = new(ApiEndpoint, SecondaryApiEndpoint) { Parent = this };

			if (!PrivateKey.IsEmpty())
			{
				_signer = new(PrivateKey.UnSecure());
				_walletAddress = WalletAddress.IsEmpty()
					? _signer.Address
					: WalletAddress.NormalizeGmxAddress("wallet address");
				if (!_walletAddress.Equals(_signer.Address,
					StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException(
						"GMX express orders require the wallet and signer addresses " +
						"to match. Subaccounts are not configured by this connector.");
			}
			else if (!WalletAddress.IsEmpty())
				_walletAddress = WalletAddress.NormalizeGmxAddress("wallet address");

			if (!_walletAddress.IsEmpty())
			{
				WalletAddress = _walletAddress;
				_portfolioName = "GMX_" + Network.NetworkName() + "_" +
					_walletAddress[2..10];
			}

			await RefreshReferenceDataAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			DisposeClient();
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
		DisposeClient();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		EnsureConnected();
		using (_sync.EnterScope())
		{
			if (_isPolling)
				return;
			_isPolling = true;
		}

		try
		{
			var now = DateTime.UtcNow;
			bool hasLevel1;
			bool hasTicks;
			bool hasCandles;
			bool hasAccount;
			bool hasPending;
			using (_sync.EnterScope())
			{
				hasLevel1 = _level1Subscriptions.Count > 0;
				hasTicks = _tickSubscriptions.Count > 0;
				hasCandles = _candleSubscriptions.Count > 0;
				hasAccount = _portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0;
				hasPending = _pendingRequests.Count > 0;
			}

			if (hasLevel1 && now - _lastMarketRefresh >= PollingInterval)
			{
				_lastMarketRefresh = now;
				await RefreshLevel1SubscriptionsAsync(cancellationToken);
			}
			if (hasTicks && now - _lastTradeRefresh >= PollingInterval)
			{
				_lastTradeRefresh = now;
				await RefreshTradeSubscriptionsAsync(cancellationToken);
			}
			if (hasCandles && now - _lastCandleRefresh >= PollingInterval)
			{
				_lastCandleRefresh = now;
				await RefreshCandleSubscriptionsAsync(cancellationToken);
			}
			if (hasAccount && now - _lastAccountRefresh >= PollingInterval)
			{
				_lastAccountRefresh = now;
				await RefreshAccountSubscriptionsAsync(cancellationToken);
			}
			if (hasPending && now - _lastStatusRefresh >= PollingInterval)
			{
				_lastStatusRefresh = now;
				await RefreshPendingRequestsAsync(cancellationToken);
			}
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_isPolling = false;
		}
	}

	private async ValueTask RefreshReferenceDataAsync(
		CancellationToken cancellationToken)
	{
		var marketsTask = ApiClient.GetMarketsAsync(cancellationToken).AsTask();
		var tokensTask = ApiClient.GetTokensAsync(cancellationToken).AsTask();
		var tickersTask = ApiClient.GetTickersAsync(cancellationToken).AsTask();
		await Task.WhenAll(marketsTask, tokensTask, tickersTask);

		var tokens = await tokensTask;
		var tokenAddresses = new Dictionary<string, GmxToken>(
			StringComparer.OrdinalIgnoreCase);
		var tokenSymbols = new Dictionary<string, GmxToken>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var token in tokens)
		{
			if (token?.Address.IsEmpty() != false || token.Symbol.IsEmpty() ||
				token.Decimals is < 0 or > 36)
				continue;
			tokenAddresses.TryAdd(token.Address, token);
			if (!tokenSymbols.TryGetValue(token.Symbol, out var current) ||
				current.IsNative && !token.IsNative)
				tokenSymbols[token.Symbol] = token;
		}
		if (tokenAddresses.Count == 0)
			throw new InvalidDataException("GMX returned no valid tokens.");

		var tickers = (await tickersTask)
			.Where(static ticker => ticker?.MarketTokenAddress.IsEmpty() == false)
			.GroupBy(static ticker => ticker.MarketTokenAddress,
				StringComparer.OrdinalIgnoreCase)
			.ToDictionary(static group => group.Key, static group => group.First(),
				StringComparer.OrdinalIgnoreCase);
		var markets = new List<GmxMarket>();
		foreach (var source in await marketsTask)
		{
			if (source?.Symbol.IsEmpty() != false ||
				source.MarketTokenAddress.IsEmpty() || !source.IsListed)
				continue;
			if (!tokenAddresses.TryGetValue(source.LongTokenAddress,
				out var longToken) ||
				!tokenAddresses.TryGetValue(source.ShortTokenAddress,
					out var shortToken))
				continue;
			var indexToken = source.IsSpotOnly
				? longToken
				: tokenAddresses.TryGetValue(source.IndexTokenAddress,
					out var index) ? index : null;
			if (indexToken is null)
				continue;
			var (baseAsset, quoteAsset) = ParseAssets(source.Symbol,
				source.IsSpotOnly, longToken.Symbol, shortToken.Symbol);
			var maximumLeverage = (source.LeverageTiers ?? [])
				.Select(static tier => tier?.MaxLeverage.TryParseGmxScaled(4))
				.Where(static value => value is > 0)
				.DefaultIfEmpty(1m)
				.Max() ?? 1m;
			tickers.TryGetValue(source.MarketTokenAddress, out var ticker);
			markets.Add(new()
			{
				Symbol = source.Symbol.Trim(),
				MarketAddress = source.MarketTokenAddress,
				IndexToken = indexToken,
				LongToken = longToken,
				ShortToken = shortToken,
				BaseAsset = baseAsset,
				QuoteAsset = quoteAsset,
				IsSpotOnly = source.IsSpotOnly,
				IsListed = source.IsListed,
				ListingDate = source.ListingDate is > 0
					? source.ListingDate.Value.FromGmxSeconds()
					: null,
				MinimumPositionUsd = source.MinimumPositionSizeUsd
					.TryParseGmxUsd() ?? 0m,
				MinimumCollateralUsd = source.MinimumCollateralUsd
					.TryParseGmxUsd() ?? 0m,
				MaximumLeverage = maximumLeverage,
				PriceStep = GmxExtensions.VolumeStep(indexToken.Decimals),
				VolumeStep = GmxExtensions.VolumeStep(indexToken.Decimals),
				Ticker = ticker,
			});
		}
		if (markets.Count == 0)
			throw new InvalidDataException("GMX returned no listed markets.");
		var duplicate = markets.GroupBy(static market => market.Symbol,
			StringComparer.OrdinalIgnoreCase).FirstOrDefault(
			static group => group.Count() > 1);
		if (duplicate is not null)
			throw new InvalidDataException("GMX returned duplicate symbol '" +
				duplicate.Key + "'.");

		using (_sync.EnterScope())
		{
			_tokensByAddress.Clear();
			_tokensBySymbol.Clear();
			foreach (var pair in tokenAddresses)
				_tokensByAddress.Add(pair.Key, pair.Value);
			foreach (var pair in tokenSymbols)
				_tokensBySymbol.Add(pair.Key, pair.Value);
			_markets.Clear();
			_marketsByAddress.Clear();
			foreach (var market in markets)
			{
				_markets.Add(market.Symbol, market);
				_marketsByAddress.Add(market.MarketAddress, market);
			}
		}
		UpdateServerTime(DateTime.UtcNow);
	}

	private static (string BaseAsset, string QuoteAsset) ParseAssets(
		string symbol, bool isSpotOnly, string longToken, string shortToken)
	{
		if (isSpotOnly)
			return (longToken, shortToken);
		var pair = symbol.Split(' ', 2)[0].Split('/', 2);
		if (pair.Length != 2 || pair[0].IsEmpty() || pair[1].IsEmpty())
			throw new InvalidDataException(
				"GMX returned an unsupported market symbol '" + symbol + "'.");
		return (pair[0], pair[1]);
	}

	private void DisposeClient()
	{
		var client = _apiClient;
		_apiClient = null;
		_signer = null;
		client?.Dispose();
		ClearState();
	}
}
