namespace StockSharp.Curve;

public partial class CurveMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rpcClient is not null || _apiClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_rpcClient = new(RpcEndpoint, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			_apiClient = new(ApiEndpoint, PricesEndpoint)
			{
				Parent = this,
			};
			await RpcClient.VerifyChainAsync(cancellationToken);
			RouterAddress = RouterAddress.NormalizeAddress();
			await RpcClient.VerifyContractAsync(RouterAddress,
				"Curve Router NG", cancellationToken);
			WalletAddress = RpcClient.IsWalletConfigured
				? RpcClient.WalletAddress
				: null;
			using (_sync.EnterScope())
				_tokens[CurveExtensions.NativeTokenAddress] =
					CurveRpcClient.CreateNativeToken();

			CurveMarketDefinition[] definitions;
			try
			{
				definitions = ParseMarketDefinitions();
			}
			catch (Exception error)
			{
				throw new InvalidOperationException(
					"Configured Curve pool definitions are invalid.", error);
			}

			var largePools = await ApiClient.GetLargePoolsAsync(
				cancellationToken);
			var poolsById = largePools
				.Where(static pool => pool?.Address.IsEmpty() == false)
				.GroupBy(static pool => pool.Address,
					StringComparer.OrdinalIgnoreCase)
				.ToDictionary(static group => group.Key.NormalizeAddress(),
					static group => group.First(),
					StringComparer.OrdinalIgnoreCase);
			var missing = definitions.Select(static item => item.PoolId)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Where(poolId => !poolsById.ContainsKey(poolId)).ToArray();
			if (missing.Length > 0)
			{
				var allPools = await ApiClient.GetAllPoolsAsync(
					cancellationToken);
				foreach (var pool in allPools)
				{
					if (pool?.Address.IsEmpty() != false)
						continue;
					var poolId = pool.Address.NormalizeAddress();
					if (!poolsById.ContainsKey(poolId))
						poolsById.Add(poolId, pool);
				}
			}

			var selected = new List<CurveApiPool>();
			foreach (var poolId in definitions.Select(static item => item.PoolId)
				.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (!poolsById.TryGetValue(poolId, out var pool))
					throw new InvalidDataException(
						$"Curve API has no Ethereum pool '{poolId}'.");
				selected.Add(pool);
			}
			selected.AddRange(largePools
				.Where(IsDiscoverablePool)
				.OrderByDescending(static pool => pool.TotalValueLocked ?? 0m)
				.Where(pool => !selected.Any(existing =>
					existing.Address.EqualsIgnoreCase(pool.Address)))
				.Take(MaximumDiscoveredPools));

			var errors = new List<Exception>();
			foreach (var source in selected)
			{
				try
				{
					var poolDefinitions = definitions.Where(item =>
						item.PoolId.EqualsIgnoreCase(source.Address)).ToArray();
					await RegisterPoolAsync(source, poolDefinitions,
						cancellationToken);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog(
						"Curve pool {0} loading failed: {1}",
						source.Address, error.Message);
				}
			}

			CurveMarket[] markets;
			using (_sync.EnterScope())
				markets = [.. _markets.Values];
			if (markets.Length == 0)
				throw errors.Count == 1
					? errors[0]
					: new AggregateException(
						"No Curve markets could be loaded.", errors);

			if (!WebSocketEndpoint.IsEmpty())
			{
				try
				{
					_socketClient = new(WebSocketEndpoint)
					{
						Parent = this,
					};
					_socketClient.LogReceived += OnSocketLog;
					await _socketClient.ConnectAsync(markets,
						cancellationToken);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					this.AddWarningLog(
						"Curve WebSocket is unavailable; Curve Prices " +
						"polling remains active: {0}", error.Message);
					DisposeSocket();
				}
			}

			connectMsg.SessionId = RpcClient.IsWalletConfigured
				? $"Curve Ethereum {RpcClient.WalletAddress}"
				: "Curve Ethereum public";
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
			var hasMarketSubscriptions =
				_level1Subscriptions.Count > 0 ||
				_tickSubscriptions.Count > 0 ||
				_candleSubscriptions.Count > 0;
			if (_rpcClient is not null && _apiClient is not null &&
				hasMarketSubscriptions &&
				(_realtimeLogs.Count > 0 || CurrentTime >= _nextMarketPoll))
			{
				_nextMarketPoll = CurrentTime + PollingInterval;
				pollMarket = true;
			}
			if (_rpcClient is not null &&
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

	private bool IsDiscoverablePool(CurveApiPool pool)
		=> pool is not null && !pool.IsBroken &&
		(pool.TotalValueLocked ?? 0m) >= MinimumPoolTvl &&
		pool.Coins is { Length: >= 2 and <= 8 } &&
		pool.RegistryId.TryToRegistryType(out _) &&
		pool.Coins.All(static coin => coin is not null &&
			!coin.Address.IsEmpty() && !coin.Address.IsNativeToken());

	private async ValueTask RegisterPoolAsync(CurveApiPool source,
		CurveMarketDefinition[] definitions,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		var poolId = source.Address.NormalizeAddress();
		if (!source.RegistryId.TryToRegistryType(out var registryType))
			throw new NotSupportedException(
				$"Curve registry '{source.RegistryId}' is not supported.");
		if (source.Coins is not { Length: >= 2 and <= 8 })
			throw new InvalidDataException(
				$"Curve pool '{poolId}' must contain between two and eight " +
				"coins.");
		if (source.Coins.Any(static coin => coin is null ||
			coin.Address.IsEmpty() || coin.Address.IsNativeToken()))
			throw new NotSupportedException(
				$"Curve pool '{poolId}' uses a native-asset sentinel; only " +
				"ERC-20 direct routes are supported.");
		await RpcClient.VerifyContractAsync(poolId, "Curve pool",
			cancellationToken);

		var coins = new CurveToken[source.Coins.Length];
		for (var index = 0; index < source.Coins.Length; index++)
		{
			var apiAddress = source.Coins[index].Address.NormalizeAddress();
			var contractAddress = await RpcClient.GetPoolCoinAddressAsync(
				poolId, index, cancellationToken);
			if (!apiAddress.EqualsIgnoreCase(contractAddress))
				throw new InvalidDataException(
					$"Curve API coin {index} for pool '{poolId}' does not " +
					"match the on-chain pool contract.");
			coins[index] = await RpcClient.GetTokenAsync(source.Coins[index],
				index, cancellationToken);
		}
		if (coins.Select(static coin => coin.Address).Distinct(
			StringComparer.OrdinalIgnoreCase).Count() != coins.Length)
			throw new InvalidDataException(
				$"Curve pool '{poolId}' contains duplicate coins.");

		var gauge = source.GaugeAddress.IsEmpty() ||
			source.GaugeAddress.IsZeroAddress()
			? null
			: source.GaugeAddress.NormalizeAddress();
		var pool = new CurvePool
		{
			PoolId = poolId,
			Name = source.Name?.Trim() ?? source.Symbol?.Trim() ?? poolId,
			RegistryType = registryType,
			PoolType = registryType.ToPoolType(),
			Coins = coins,
			GaugeAddress = gauge,
			TotalValueLocked = source.TotalValueLocked ?? 0m,
			IsMetaPool = source.IsMetaPool,
		};

		var oriented = definitions is { Length: > 0 }
			? definitions
			: [new CurveMarketDefinition { PoolId = poolId }];
		foreach (var definition in oriented)
		{
			if (definition.BaseToken.IsEmpty())
			{
				for (var left = 0; left < coins.Length - 1; left++)
					for (var right = left + 1; right < coins.Length; right++)
					{
						var pair = OrientMarket(coins[left], coins[right]);
						RegisterMarket(CreateMarket(pool, pair.BaseToken,
							pair.QuoteToken, null));
					}
				continue;
			}

			var baseToken = coins.FirstOrDefault(coin =>
				coin.Address.EqualsIgnoreCase(definition.BaseToken));
			var quoteToken = coins.FirstOrDefault(coin =>
				coin.Address.EqualsIgnoreCase(definition.QuoteToken));
			if (baseToken is null || quoteToken is null ||
				ReferenceEquals(baseToken, quoteToken))
				throw new InvalidDataException(
					$"Configured pair does not match Curve pool '{poolId}'.");
			RegisterMarket(CreateMarket(pool, baseToken, quoteToken,
				definition.SecurityCode));
		}
	}

	private CurveMarket CreateMarket(CurvePool pool, CurveToken baseToken,
		CurveToken quoteToken, string securityCode)
		=> new()
		{
			PoolId = pool.PoolId,
			PoolName = pool.Name,
			RegistryType = pool.RegistryType,
			PoolType = pool.PoolType,
			RouterAddress = RouterAddress,
			BaseToken = baseToken,
			QuoteToken = quoteToken,
			PoolCoinCount = pool.Coins.Length,
			GaugeAddress = pool.GaugeAddress,
			TotalValueLocked = pool.TotalValueLocked,
			SecurityCode = securityCode.IsEmpty()
				? CreateSecurityCode(baseToken, quoteToken)
				: NormalizeSecurityCode(securityCode),
		};

	private void RegisterMarket(CurveMarket market)
	{
		if (market?.PoolId.IsEmpty() != false || market.BaseToken is null ||
			market.QuoteToken is null || market.SecurityCode.IsEmpty())
			throw new InvalidDataException(
				"Curve pool metadata is incomplete.");
		using (_sync.EnterScope())
		{
			if (_markets.Values.Any(existing =>
				existing.PoolId.EqualsIgnoreCase(market.PoolId) &&
				existing.BaseToken.Address.EqualsIgnoreCase(
					market.BaseToken.Address) &&
				existing.QuoteToken.Address.EqualsIgnoreCase(
					market.QuoteToken.Address)))
				return;
			var code = market.SecurityCode;
			if (_markets.ContainsKey(code))
				code = NormalizeSecurityCode(code + "-" +
					market.PoolId[2..8]);
			if (_markets.ContainsKey(code))
				return;
			if (!code.Equals(market.SecurityCode, StringComparison.Ordinal))
				market = CopyWithSecurityCode(market, code);
			_tokens.TryAdd(market.BaseToken.Address, market.BaseToken);
			_tokens.TryAdd(market.QuoteToken.Address, market.QuoteToken);
			_markets.Add(code, market);
			if (!_marketsByPool.TryGetValue(market.PoolId, out var poolMarkets))
				_marketsByPool.Add(market.PoolId, poolMarkets = []);
			poolMarkets.Add(market);
		}
	}

	private static CurveMarket CopyWithSecurityCode(CurveMarket market,
		string securityCode)
		=> new()
		{
			PoolId = market.PoolId,
			PoolName = market.PoolName,
			RegistryType = market.RegistryType,
			PoolType = market.PoolType,
			RouterAddress = market.RouterAddress,
			BaseToken = market.BaseToken,
			QuoteToken = market.QuoteToken,
			PoolCoinCount = market.PoolCoinCount,
			GaugeAddress = market.GaugeAddress,
			TotalValueLocked = market.TotalValueLocked,
			SecurityCode = securityCode,
		};

	private CurveMarketDefinition[] ParseMarketDefinitions()
	{
		if (Pools.IsEmpty())
			return [];
		var result = new List<CurveMarketDefinition>();
		foreach (var item in Pools.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not 1 and not 3 and not 4)
				throw new FormatException(
					"Each Curve pool must use pool or " +
					"pool|base-token|quote-token|security-code format.");
			if (fields.Length >= 3 &&
				(fields[1].IsEmpty() || fields[2].IsEmpty()))
				throw new FormatException(
					"Both Curve base and quote token addresses are required.");
			result.Add(new()
			{
				PoolId = fields[0].NormalizeAddress(),
				BaseToken = fields.Length >= 3
					? fields[1].NormalizeAddress()
					: null,
				QuoteToken = fields.Length >= 3
					? fields[2].NormalizeAddress()
					: null,
				SecurityCode = fields.Length == 4
					? NormalizeSecurityCode(fields[3])
					: null,
			});
		}
		return [.. result];
	}

	private static (CurveToken BaseToken, CurveToken QuoteToken)
		OrientMarket(CurveToken left, CurveToken right)
		=> QuotePriority(left.Symbol) > QuotePriority(right.Symbol)
			? (right, left)
			: (left, right);

	private static int QuotePriority(string symbol)
		=> symbol?.ToUpperInvariant() switch
		{
			"USDC" => 100,
			"USDT" => 98,
			"USDS" => 96,
			"DAI" => 94,
			"FRAX" => 90,
			"CRVUSD" => 88,
			"WETH" => 75,
			"ETH" => 75,
			"CBBTC" => 65,
			"WBTC" => 65,
			_ => 0,
		};

	private static string CreateSecurityCode(CurveToken baseToken,
		CurveToken quoteToken)
		=> NormalizeSecurityCode(
			$"{baseToken.Symbol}-{quoteToken.Symbol}");

	private static string NormalizeSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
			throw new FormatException(
				$"Invalid Curve security code '{value}'.");
		return value;
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

	private void DisposeSocket()
	{
		if (_socketClient is null)
			return;
		_socketClient.LogReceived -= OnSocketLog;
		_socketClient.Dispose();
		_socketClient = null;
	}

	private void DisposeClients()
	{
		DisposeSocket();
		_rpcClient?.Dispose();
		_rpcClient = null;
		_apiClient?.Dispose();
		_apiClient = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByPool.Clear();
			_tokens.Clear();
			_level1Subscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_seenTrades.Clear();
			_tradeDeliveryOrder.Clear();
			_candleFingerprints.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedSwaps.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
			_realtimeLogs.Clear();
			_blockTimes.Clear();
			_blockTimeOrder.Clear();
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
		}
	}
}
