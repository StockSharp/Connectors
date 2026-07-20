namespace StockSharp.THORChain;

public partial class THORChainMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_apiClient is not null || _signer is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(WalletAddress, PrivateKey);
			WalletAddress = Signer.WalletAddress;
			_apiClient = new(MidgardEndpoint, ThornodeEndpoint, ClientId)
			{
				Parent = this,
			};
			var nodeInfo = await ApiClient.GetNodeInfoAsync(cancellationToken);
			_chainId = nodeInfo?.NodeInfo?.Network?.Trim();
			if (_chainId.IsEmpty())
				throw new InvalidDataException(
					"THORNode returned no Cosmos chain identifier.");

			var pools = await ApiClient.GetPoolsAsync(cancellationToken);
			RegisterMarkets(pools ?? [], ParseMarketDefinitions());
			if (_markets.Count == 0)
				throw new InvalidOperationException(
					"No available THORChain pools matched the connector " +
					"settings.");

			connectMsg.SessionId = $"THORChain {_chainId} " +
				(Signer.IsWalletAvailable
					? Signer.WalletAddress[..8]
					: "public");
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			DisposeClients();
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
		DisposeClients();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClients();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var pollMarket = false;
		var pollPrivate = false;
		using (_sync.EnterScope())
		{
			if (_apiClient is not null &&
				(_level1Subscriptions.Count > 0 ||
					_tickSubscriptions.Count > 0 ||
					_candleSubscriptions.Count > 0) &&
				CurrentTime >= _nextMarketPoll)
			{
				_nextMarketPoll = CurrentTime + PollingInterval;
				pollMarket = true;
			}
			if (_apiClient is not null && _signer?.IsWalletAvailable == true &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_trackedSwaps.Values.Any(static swap =>
						swap.State == OrderStates.Active)) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				pollPrivate = true;
			}
		}
		if (pollMarket)
			await RunSafelyAsync(PollMarketAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private THORChainMarketDefinition[] ParseMarketDefinitions()
	{
		if (Markets.IsEmpty())
			return [];
		var result = new List<THORChainMarketDefinition>();
		var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in Markets.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not (1 or 2))
				throw new FormatException(
					"Each THORChain market must use asset or " +
					"asset|security-code format.");
			var parsed = fields[0].ParseAsset();
			var asset = $"{parsed.Chain}.{parsed.Symbol}";
			if (asset.EqualsIgnoreCase(THORChainExtensions.RuneAsset))
				throw new FormatException(
					"THOR.RUNE cannot be its own destination market.");
			if (!assets.Add(asset))
				throw new FormatException(
					$"Duplicate THORChain asset '{asset}'.");
			var code = fields.Length == 2
				? fields[1].NormalizeSecurityCode()
				: null;
			if (!code.IsEmpty() && !codes.Add(code))
				throw new FormatException(
					$"Duplicate THORChain security code '{code}'.");
			result.Add(new()
			{
				Asset = asset,
				SecurityCode = code,
			});
		}
		return [.. result];
	}

	private void RegisterMarkets(IEnumerable<THORChainPool> source,
		THORChainMarketDefinition[] definitions)
	{
		var pools = new Dictionary<string, THORChainPool>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var pool in source ?? [])
		{
			if (pool is null || pool.Status != THORChainPoolStatuses.Available ||
				pool.Asset.IsEmpty())
				continue;
			var parsed = pool.Asset.ParseAsset();
			var asset = $"{parsed.Chain}.{parsed.Symbol}";
			if (asset.EqualsIgnoreCase(THORChainExtensions.RuneAsset))
				continue;
			ValidatePool(pool, asset);
			pool.Asset = asset;
			if (!pools.TryAdd(asset, pool))
				throw new InvalidDataException(
					$"Midgard returned duplicate pool '{asset}'.");
		}

		var selected = new List<(THORChainPool Pool, string Code)>();
		if (definitions.Length > 0)
		{
			foreach (var definition in definitions)
			{
				if (!pools.TryGetValue(definition.Asset, out var pool))
					throw new InvalidDataException(
						$"THORChain pool '{definition.Asset}' is not available.");
				selected.Add((pool, definition.SecurityCode));
			}
		}
		else
		{
			selected.AddRange(pools.Values
				.Where(pool => THORChainExtensions.ParseDecimal(
					pool.LiquidityUsd, "pool USD liquidity") >=
					MinimumLiquidityUsd)
				.OrderByDescending(pool => THORChainExtensions.ParseDecimal(
					pool.LiquidityUsd, "pool USD liquidity"))
				.Take(MaximumDiscoveredMarkets)
				.Select(static pool => (pool, (string)null)));
		}

		var parsedAssets = selected.Select(item => item.Pool.Asset.ParseAsset())
			.ToArray();
		var tickerCounts = parsedAssets.GroupBy(static item => item.Ticker,
			StringComparer.OrdinalIgnoreCase).ToDictionary(static group =>
				group.Key, static group => group.Count(),
				StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < selected.Count; i++)
		{
			var item = selected[i];
			var parsed = parsedAssets[i];
			var code = item.Code.IsEmpty()
				? (tickerCounts[parsed.Ticker] == 1
					? $"RUNE-{parsed.Ticker}"
					: $"RUNE-{parsed.Chain}-{parsed.Ticker}")
				: item.Code;
			code = code.NormalizeSecurityCode();
			var market = new THORChainMarket
			{
				Asset = item.Pool.Asset,
				Chain = parsed.Chain,
				Symbol = parsed.Symbol,
				Ticker = parsed.Ticker,
				SecurityCode = code,
				Pool = item.Pool,
			};
			using (_sync.EnterScope())
			{
				if (!_markets.TryAdd(code, market))
					throw new InvalidDataException(
						$"Duplicate THORChain security code '{code}'.");
				_marketsByAsset.Add(market.Asset, market);
			}
		}
	}

	private static void ValidatePool(THORChainPool pool, string asset)
	{
		var assetPrice = THORChainExtensions.ParseDecimal(pool.AssetPrice,
			"pool asset price");
		var assetPriceUsd = THORChainExtensions.ParseDecimal(pool.AssetPriceUsd,
			"pool USD price");
		var liquidity = THORChainExtensions.ParseDecimal(pool.LiquidityUsd,
			"pool USD liquidity");
		_ = THORChainExtensions.ParseInteger(pool.AssetDepth,
			"pool asset depth");
		_ = THORChainExtensions.ParseInteger(pool.RuneDepth,
			"pool RUNE depth");
		_ = THORChainExtensions.ParseInteger(pool.Volume24Hours,
			"24-hour volume");
		if (assetPrice <= 0 || assetPriceUsd <= 0 || liquidity < 0)
			throw new InvalidDataException(
				$"THORChain pool '{asset}' contains invalid market data.");
	}

	private async ValueTask RefreshPoolsAsync(
		CancellationToken cancellationToken)
	{
		var pools = await ApiClient.GetPoolsAsync(cancellationToken);
		var byAsset = (pools ?? []).Where(static pool => pool is not null &&
			!pool.Asset.IsEmpty()).ToDictionary(static pool =>
				pool.Asset.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
		THORChainMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		foreach (var market in markets)
		{
			if (!byAsset.TryGetValue(market.Asset, out var pool) ||
				pool.Status != THORChainPoolStatuses.Available)
				continue;
			ValidatePool(pool, market.Asset);
			pool.Asset = market.Asset;
			using (_sync.EnterScope())
				market.Pool = pool;
		}
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

	private void DisposeClients()
	{
		_apiClient?.Dispose();
		_apiClient = null;
		_signer?.Dispose();
		_signer = null;
		_chainId = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByAsset.Clear();
			_level1Subscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_seenTrades.Clear();
			_tradeDeliveryOrder.Clear();
			_level1Fingerprints.Clear();
			_candleFingerprints.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedSwaps.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
		}
	}
}
