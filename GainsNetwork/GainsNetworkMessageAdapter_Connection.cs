namespace StockSharp.GainsNetwork;

public partial class GainsNetworkMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_apiClient is not null || _rpcClient is not null ||
			_socketClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_deployment = Environment.GetDeployment();
			var rpcEndpoint = RpcEndpoint.IsEmpty()
				? _deployment.RpcEndpoint
				: NormalizeEndpoint(RpcEndpoint, false, nameof(RpcEndpoint));
			var backendEndpoint = BackendEndpoint.IsEmpty()
				? _deployment.BackendEndpoint
				: NormalizeEndpoint(BackendEndpoint, false,
					nameof(BackendEndpoint));
			var globalEndpoint = NormalizeEndpoint(GlobalEndpoint, false,
				nameof(GlobalEndpoint));
			var pricingEndpoint = NormalizeEndpoint(PricingEndpoint, false,
				nameof(PricingEndpoint));
			var socketEndpoint = NormalizeEndpoint(PriceSocketEndpoint, true,
				nameof(PriceSocketEndpoint));

			_rpcClient = new(_deployment, rpcEndpoint, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			if (RpcClient.IsWalletConfigured)
				await RpcClient.VerifyChainAsync(cancellationToken);
			_apiClient = new(backendEndpoint, globalEndpoint, pricingEndpoint)
			{
				Parent = this,
			};
			await RefreshMarketsAsync(cancellationToken);
			await RefreshChartsAsync(cancellationToken);
			_socketClient = new(socketEndpoint,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_socketClient.PriceReceived += OnPriceFrameAsync;
			_socketClient.Error += OnSocketErrorAsync;
			_socketClient.StateChanged += OnSocketStateAsync;
			await SocketClient.ConnectAsync(cancellationToken);
			_portfolioName = RpcClient.IsWalletConfigured
				? "Gains_" + _deployment.Environment + "_" +
					RpcClient.WalletAddress
				: null;
			using (_sync.EnterScope())
			{
				_nextAccountRefresh = CurrentTime + AccountRefreshInterval;
				_nextMarketRefresh = CurrentTime + TimeSpan.FromMinutes(5);
			}
			connectMsg.SessionId = "Gains " + _deployment.Name + " " +
				(RpcClient.IsWalletConfigured
					? RpcClient.WalletAddress
					: "public");
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
		var isAccountRefresh = false;
		var isMarketRefresh = false;
		using (_sync.EnterScope())
		{
			if (_apiClient is not null &&
				_rpcClient?.IsWalletConfigured == true &&
				(_portfolioSubscriptionId != 0 ||
					_orderStatusSubscriptionId != 0) &&
				CurrentTime >= _nextAccountRefresh)
			{
				_nextAccountRefresh = CurrentTime + AccountRefreshInterval;
				isAccountRefresh = true;
			}
			if (_apiClient is not null && CurrentTime >= _nextMarketRefresh)
			{
				_nextMarketRefresh = CurrentTime + TimeSpan.FromMinutes(5);
				isMarketRefresh = true;
			}
		}
		if (isMarketRefresh)
			await RunSafelyAsync(RefreshPublicAsync, cancellationToken);
		if (isAccountRefresh)
			await RunSafelyAsync(PollAccountAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshPublicAsync(
		CancellationToken cancellationToken)
	{
		await RefreshMarketsAsync(cancellationToken);
		await RefreshChartsAsync(cancellationToken);
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var variables = await ApiClient.GetTradingVariablesAsync(cancellationToken);
		var pairs = variables?.Pairs ?? [];
		var groups = variables?.Groups ?? [];
		if (pairs.Length == 0 || groups.Length == 0)
			throw new InvalidDataException(
				"Gains returned no usable trading variables.");
		if (!Enum.IsDefined(variables.TradingState))
			throw new InvalidDataException(
				"Gains returned an unsupported trading state.");
		foreach (var collateral in variables.Collaterals ?? [])
			if (collateral?.Symbol.IsEmpty() != false ||
				collateral.Address.IsEmpty() || collateral.CollateralIndex <= 0 ||
				collateral.Config is null ||
				collateral.Config.Decimals is < 0 or > 28)
				throw new InvalidDataException(
					"Gains returned an invalid collateral definition.");
		var maximumLeverages = variables.PairInfos?.MaximumLeverages ?? [];
		var activeSplits = FindActiveSplits(pairs, maximumLeverages);
		var defaultCollateral = (variables.Collaterals ?? []).FirstOrDefault(
			item => item is not null && item.IsActive && item.Symbol.Equals(
				DefaultCollateral, StringComparison.OrdinalIgnoreCase));
		var volumeStep = defaultCollateral?.Config?.Decimals is >= 0 and <= 28
			? BigInteger.One.FromBaseUnits(defaultCollateral.Config.Decimals)
			: 0.000001m;
		var markets = new List<GainsMarket>(pairs.Length);
		for (var pairIndex = 0; pairIndex < pairs.Length; pairIndex++)
		{
			var pair = pairs[pairIndex];
			if (pair?.From.IsEmpty() != false || pair.To.IsEmpty())
				continue;
			var rawMaximum = pairIndex < maximumLeverages.Length
				? ParseOptionalDecimal(maximumLeverages[pairIndex])
				: 0m;
			var isDisabled = rawMaximum == 1m;
			var baseAsset = GetDisplayAsset(pair.From, activeSplits,
				pairIndex, out var isSuperseded);
			if (isSuperseded)
				continue;
			var groupIndex = pair.GroupIndex.ParseIndex("group index");
			if (groupIndex >= groups.Length || groups[groupIndex] is null)
				throw new InvalidDataException("Gains pair " + pairIndex +
					" references unknown group " + groupIndex + ".");
			var group = groups[groupIndex];
			var feeIndex = pair.FeeIndex.ParseIndex("fee index");
			if (feeIndex >= (variables.Fees?.Length ?? 0) ||
				variables.Fees[feeIndex] is null)
				throw new InvalidDataException("Gains pair " + pairIndex +
					" references unknown fee " + feeIndex + ".");
			var fee = variables.Fees[feeIndex];
			var minimumLeverage = group.MinimumLeverage.ParseScaled(3,
				"minimum leverage");
			var maximumLeverage = rawMaximum > 1m
				? rawMaximum / GainsNetworkExtensions.LeveragePrecision
				: group.MaximumLeverage.ParseScaled(3, "maximum leverage");
			var market = new GainsMarket
			{
				PairIndex = pairIndex,
				BaseAsset = baseAsset,
				QuoteAsset = pair.To.Trim().ToUpperInvariant(),
				Symbol = baseAsset + "/" +
					pair.To.Trim().ToUpperInvariant(),
				Group = group.Name,
				MinimumLeverage = minimumLeverage,
				MaximumLeverage = maximumLeverage,
				SpreadPercentage = pair.SpreadPercentage.ParseScaled(10,
					"spread percentage"),
				MinimumPositionSizeUsd = fee.MinimumPositionSizeUsd.ParseScaled(3,
					"minimum position size"),
				VolumeStep = volumeStep,
				IsEnabled = !isDisabled,
				IsMarketOpen = !isDisabled &&
					variables.TradingState != GainsTradingStates.Paused &&
					IsGroupOpen(group.Name, variables),
			};
			markets.Add(market);
		}
		if (markets.Count == 0)
			throw new InvalidDataException("Gains returned no usable markets.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByIndex.Clear();
			foreach (var market in markets)
			{
				if (!_markets.TryAdd(market.Symbol, market))
					throw new InvalidDataException(
						"Gains returned duplicate market '" + market.Symbol + "'.");
				_marketsByIndex.Add(market.PairIndex, market);
			}
		}
		_variables = variables;
		if (!variables.LastRefreshed.IsEmpty())
			UpdateServerTime(variables.LastRefreshed.ParseTime(
				"trading-variable refresh time"));
	}

	private async ValueTask RefreshChartsAsync(
		CancellationToken cancellationToken)
	{
		var charts = await ApiClient.GetChartsAsync(cancellationToken);
		if (charts is null)
			throw new InvalidDataException("Gains returned no price snapshot.");
		var time = charts.Time > 0
			? charts.Time.FromUnix(false).EnsureUtc()
			: DateTime.UtcNow;
		using (_sync.EnterScope())
		{
			foreach (var market in _marketsByIndex.Values)
			{
				var index = market.PairIndex;
				var price = _prices.TryGetValue(index, out var existing)
					? existing
					: _prices[index] = new() { PairIndex = index };
				price.OpenPrice = GetArrayValue(charts.Opens, index);
				price.HighPrice = GetArrayValue(charts.Highs, index);
				price.LowPrice = GetArrayValue(charts.Lows, index);
				price.ClosePrice = GetArrayValue(charts.Closes, index);
				if (price.MarkPrice <= 0)
					price.MarkPrice = price.ClosePrice;
				var indexPrice = GetArrayValue(charts.IndexPrices, index);
				if (indexPrice > 0)
					price.IndexPrice = indexPrice;
				price.Time = time;
			}
		}
		UpdateServerTime(time);
	}

	private async ValueTask OnPriceFrameAsync(GainsPriceFrame frame,
		CancellationToken cancellationToken)
	{
		if (frame is null)
			return;
		var time = frame.Timestamp > 0
			? frame.Timestamp.FromUnix(false).EnsureUtc()
			: ServerTime;
		if (frame.IsHeartbeat)
		{
			UpdateServerTime(time);
			return;
		}
		var changed = new HashSet<int>();
		using (_sync.EnterScope())
		{
			foreach (var point in frame.MarkPrices ?? [])
			{
				if (!_marketsByIndex.ContainsKey(point.PairIndex) ||
					point.Price <= 0)
					continue;
				var price = _prices.TryGetValue(point.PairIndex, out var existing)
					? existing
					: _prices[point.PairIndex] = new()
					{
						PairIndex = point.PairIndex,
					};
				price.MarkPrice = point.Price;
				price.ClosePrice = point.Price;
				price.Time = time;
				changed.Add(point.PairIndex);
			}
			foreach (var point in frame.IndexPrices ?? [])
			{
				if (!_marketsByIndex.ContainsKey(point.PairIndex) ||
					point.Price <= 0)
					continue;
				var price = _prices.TryGetValue(point.PairIndex, out var existing)
					? existing
					: _prices[point.PairIndex] = new()
					{
						PairIndex = point.PairIndex,
					};
				price.IndexPrice = point.Price;
				price.Time = time;
				changed.Add(point.PairIndex);
			}
		}
		UpdateServerTime(time);
		foreach (var pairIndex in changed)
			await PublishLevel1Async(pairIndex, cancellationToken);
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
		if (state == ConnectionStates.Restored)
			await RefreshPublicAsync(cancellationToken);
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
			socket.PriceReceived -= OnPriceFrameAsync;
			socket.Error -= OnSocketErrorAsync;
			socket.StateChanged -= OnSocketStateAsync;
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			finally
			{
				socket.Dispose();
			}
		}
		api?.Dispose();
		rpc?.Dispose();
		ClearState();
	}

	private static Dictionary<string, int> FindActiveSplits(GainsPair[] pairs,
		string[] maximumLeverages)
	{
		var baseAssets = pairs.Where(static pair => pair?.From.IsEmpty() == false)
			.Select(static pair => pair.From.Trim().ToUpperInvariant())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var result = new Dictionary<string, (int Suffix, int PairIndex)>(
			StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < pairs.Length; i++)
		{
			var asset = pairs[i]?.From;
			if (!TryParseSplit(asset, out var baseAsset, out var suffix) ||
				!baseAssets.Contains(baseAsset) ||
				(i < maximumLeverages.Length &&
					ParseOptionalDecimal(maximumLeverages[i]) == 1m))
				continue;
			if (!result.TryGetValue(baseAsset, out var current) ||
				suffix > current.Suffix)
				result[baseAsset] = (suffix, i);
		}
		return result.ToDictionary(static pair => pair.Key,
			static pair => pair.Value.PairIndex, StringComparer.OrdinalIgnoreCase);
	}

	private static string GetDisplayAsset(string asset,
		Dictionary<string, int> activeSplits, int pairIndex,
		out bool isSuperseded)
	{
		asset = asset.Trim().ToUpperInvariant();
		if (activeSplits.TryGetValue(asset, out _))
		{
			isSuperseded = true;
			return asset;
		}
		if (TryParseSplit(asset, out var baseAsset, out _) &&
			activeSplits.TryGetValue(baseAsset, out var activeIndex))
		{
			isSuperseded = activeIndex != pairIndex;
			return baseAsset;
		}
		isSuperseded = false;
		return asset;
	}

	private static bool TryParseSplit(string asset, out string baseAsset,
		out int suffix)
	{
		baseAsset = null;
		suffix = 0;
		if (asset.IsEmpty())
			return false;
		asset = asset.Trim().ToUpperInvariant();
		var separator = asset.LastIndexOf('_');
		if (separator <= 0 || separator >= asset.Length - 1 ||
			!int.TryParse(asset[(separator + 1)..], NumberStyles.None,
				CultureInfo.InvariantCulture, out suffix) || suffix <= 0)
			return false;
		baseAsset = asset[..separator];
		return true;
	}

	private static decimal ParseOptionalDecimal(string value)
	{
		if (value.IsEmpty())
			return 0m;
		var raw = value.ParseInteger("numeric value");
		if (raw < (BigInteger)decimal.MinValue ||
			raw > (BigInteger)decimal.MaxValue)
			throw new InvalidDataException(
				"Gains numeric value exceeds decimal range.");
		return (decimal)raw;
	}

	private static bool IsGroupOpen(string group,
		GainsTradingVariables variables)
	{
		group = group?.ToLowerInvariant() ?? string.Empty;
		if (group.StartsWith("forex", StringComparison.Ordinal))
			return variables.IsForexOpen;
		if (group.StartsWith("stocks", StringComparison.Ordinal))
			return variables.IsStocksOpen;
		if (group.StartsWith("indices", StringComparison.Ordinal))
			return variables.IsIndicesOpen;
		if (group.StartsWith("commodities", StringComparison.Ordinal))
			return variables.IsCommoditiesOpen;
		return true;
	}

	private static decimal GetArrayValue(decimal?[] values, int index)
		=> values is not null && index >= 0 && index < values.Length
			? values[index] ?? 0m
			: 0m;
}
