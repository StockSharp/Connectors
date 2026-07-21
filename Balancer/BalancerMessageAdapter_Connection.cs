namespace StockSharp.Balancer;

public partial class BalancerMessageAdapter
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
			_deployment = Network.GetDeployment();
			RpcEndpoint = NormalizeEndpoint(RpcEndpoint, "https");
			WebSocketEndpoint = NormalizeEndpoint(WebSocketEndpoint, "wss");
			ApiEndpoint = NormalizeEndpoint(ApiEndpoint, "https");
			_rpcClient = new(_deployment, RpcEndpoint, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			_apiClient = new(ApiEndpoint)
			{
				Parent = this,
			};
			await RpcClient.VerifyChainAsync(cancellationToken);
			await RpcClient.VerifyContractAsync(_deployment.V2Vault,
				"Balancer V2 Vault", cancellationToken);
			if (!_deployment.V3Vault.IsEmpty())
				await RpcClient.VerifyContractAsync(_deployment.V3Vault,
					"Balancer V3 Vault", cancellationToken);
			if (!_deployment.V3Router.IsEmpty())
			{
				await RpcClient.VerifyContractAsync(_deployment.V3Router,
					"Balancer V3 Router", cancellationToken);
				await RpcClient.VerifyPermit2Async(_deployment.V3Router,
					cancellationToken);
			}
			WalletAddress = RpcClient.IsWalletConfigured
				? RpcClient.WalletAddress
				: null;
			using (_sync.EnterScope())
				_tokens[BalancerExtensions.ZeroAddress] =
					RpcClient.CreateNativeToken();

			BalancerMarketDefinition[] definitions;
			try
			{
				definitions = ParseMarketDefinitions();
			}
			catch (Exception error)
			{
				throw new InvalidOperationException(
					"Configured Balancer pool definitions are invalid.", error);
			}

			var selected = new List<BalancerPool>();
			foreach (var poolId in definitions.Select(static item => item.PoolId)
				.Distinct(StringComparer.OrdinalIgnoreCase))
				selected.Add(await ApiClient.GetPoolAsync(_deployment, poolId,
					cancellationToken));
			var discovered = await ApiClient.GetPoolsAsync(_deployment,
				MaximumDiscoveredPools, MinimumPoolTvl, cancellationToken);
			selected.AddRange(discovered.Where(pool => !selected.Any(existing =>
				existing.Id.EqualsIgnoreCase(pool.Id))));

			var errors = new List<Exception>();
			foreach (var pool in selected)
			{
				try
				{
					var poolDefinitions = definitions.Where(item =>
						item.PoolId.EqualsIgnoreCase(pool.Id)).ToArray();
					await RegisterPoolAsync(pool, poolDefinitions,
						cancellationToken);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog("Balancer pool {0} loading failed: {1}",
						pool.Id, error.Message);
				}
			}

			BalancerMarket[] markets;
			using (_sync.EnterScope())
				markets = [.. _markets.Values];
			if (markets.Length == 0)
				throw errors.Count == 1
					? errors[0]
					: new AggregateException(
						"No Balancer markets could be loaded.", errors);

			if (!WebSocketEndpoint.IsEmpty())
				await TryConnectSocketAsync(cancellationToken);

			connectMsg.SessionId = RpcClient.IsWalletConfigured
				? $"Balancer {_deployment.Name} {RpcClient.WalletAddress}"
				: $"Balancer {_deployment.Name} public";
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
		var reconnectSocket = false;
		using (_sync.EnterScope())
		{
			var hasMarketSubscriptions = _level1Subscriptions.Count > 0 ||
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
			if (_rpcClient is not null && !WebSocketEndpoint.IsEmpty() &&
				(_socketClient is null || !_socketClient.IsConnected) &&
				CurrentTime >= _nextSocketReconnect)
			{
				_nextSocketReconnect = CurrentTime + TimeSpan.FromSeconds(15);
				reconnectSocket = true;
			}
		}
		if (reconnectSocket)
			await TryConnectSocketAsync(cancellationToken);
		if (pollMarket)
			await RunSafelyAsync(PollMarketAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RegisterPoolAsync(BalancerPool source,
		BalancerMarketDefinition[] definitions,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (!BalancerApiClient.IsSupported(source))
			throw new NotSupportedException(
				$"Balancer pool '{source.Id}' is not directly tradable.");
		if (source.ProtocolVersion == 3 && _deployment.V3Router.IsEmpty())
			throw new NotSupportedException(
				$"Balancer V3 is not deployed on {_deployment.Name}.");
		await RpcClient.VerifyContractAsync(source.Address, "Balancer pool",
			cancellationToken);
		var tokens = new BalancerToken[source.Tokens.Length];
		for (var index = 0; index < source.Tokens.Length; index++)
			tokens[index] = await RpcClient.VerifyTokenAsync(source.Tokens[index],
				cancellationToken);
		if (tokens.Select(static token => token.Address).Distinct(
			StringComparer.OrdinalIgnoreCase).Count() != tokens.Length)
			throw new InvalidDataException(
				$"Balancer pool '{source.Id}' contains duplicate tokens.");
		var pool = CopyWithTokens(source, tokens);
		var oriented = definitions is { Length: > 0 }
			? definitions
			: [new BalancerMarketDefinition { PoolId = pool.Id }];
		foreach (var definition in oriented)
		{
			if (definition.BaseToken.IsEmpty())
			{
				for (var left = 0; left < tokens.Length - 1; left++)
					for (var right = left + 1; right < tokens.Length; right++)
					{
						var pair = OrientMarket(tokens[left], tokens[right]);
						RegisterMarket(BalancerExtensions.CreateMarket(pool,
							pair.BaseToken, pair.QuoteToken, null));
					}
				continue;
			}
			var baseToken = tokens.FirstOrDefault(token =>
				token.Address.EqualsIgnoreCase(definition.BaseToken));
			var quoteToken = tokens.FirstOrDefault(token =>
				token.Address.EqualsIgnoreCase(definition.QuoteToken));
			if (baseToken is null || quoteToken is null ||
				ReferenceEquals(baseToken, quoteToken))
				throw new InvalidDataException(
					$"Configured pair does not match Balancer pool '{pool.Id}'.");
			RegisterMarket(BalancerExtensions.CreateMarket(pool, baseToken,
				quoteToken, definition.SecurityCode));
		}
	}

	private void RegisterMarket(BalancerMarket market)
	{
		if (market?.Pool is null || market.BaseToken is null ||
			market.QuoteToken is null || market.SecurityCode.IsEmpty())
			throw new InvalidDataException(
				"Balancer pool metadata is incomplete.");
		using (_sync.EnterScope())
		{
			if (_markets.Values.Any(existing =>
				existing.Key.EqualsIgnoreCase(market.Key)))
				return;
			var code = market.SecurityCode;
			if (_markets.ContainsKey(code))
				code = BalancerExtensions.NormalizeSecurityCode(code + "-" +
					market.Pool.Address[2..8]);
			if (_markets.ContainsKey(code))
				return;
			if (!code.Equals(market.SecurityCode, StringComparison.Ordinal))
				market = CopyWithSecurityCode(market, code);
			_tokens.TryAdd(market.BaseToken.Address, market.BaseToken);
			_tokens.TryAdd(market.QuoteToken.Address, market.QuoteToken);
			_markets.Add(code, market);
			if (!_marketsByPool.TryGetValue(market.Pool.Id, out var poolMarkets))
				_marketsByPool.Add(market.Pool.Id, poolMarkets = []);
			poolMarkets.Add(market);
		}
	}

	private static BalancerPool CopyWithTokens(BalancerPool pool,
		BalancerToken[] tokens)
		=> new()
		{
			Id = pool.Id,
			Address = pool.Address,
			Name = pool.Name,
			Symbol = pool.Symbol,
			Type = pool.Type,
			PoolVersion = pool.PoolVersion,
			ProtocolVersion = pool.ProtocolVersion,
			TotalLiquidity = pool.TotalLiquidity,
			Volume24Hours = pool.Volume24Hours,
			Fees24Hours = pool.Fees24Hours,
			SwapFee = pool.SwapFee,
			IsSwapEnabled = pool.IsSwapEnabled,
			Tokens = tokens,
		};

	private static BalancerMarket CopyWithSecurityCode(BalancerMarket market,
		string securityCode)
		=> new()
		{
			Key = market.Key,
			SecurityCode = securityCode,
			Pool = market.Pool,
			BaseToken = market.BaseToken,
			QuoteToken = market.QuoteToken,
		};

	private BalancerMarketDefinition[] ParseMarketDefinitions()
	{
		if (Pools.IsEmpty())
			return [];
		var result = new List<BalancerMarketDefinition>();
		foreach (var item in Pools.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not 1 and not 3 and not 4)
				throw new FormatException(
					"Each Balancer pool must use pool or " +
					"pool|base-token|quote-token|security-code format.");
			if (fields.Length >= 3 &&
				(fields[1].IsEmpty() || fields[2].IsEmpty()))
				throw new FormatException(
					"Both Balancer base and quote token addresses are required.");
			result.Add(new()
			{
				PoolId = NormalizeConfiguredPoolId(fields[0]),
				BaseToken = fields.Length >= 3
					? fields[1].NormalizeAddress()
					: null,
				QuoteToken = fields.Length >= 3
					? fields[2].NormalizeAddress()
					: null,
				SecurityCode = fields.Length == 4
					? BalancerExtensions.NormalizeSecurityCode(fields[3])
					: null,
			});
		}
		return [.. result];
	}

	private static string NormalizeConfiguredPoolId(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		return value.Length switch
		{
			42 => value.NormalizePoolId(3),
			66 => value.NormalizePoolId(2),
			_ => throw new FormatException(
				$"Invalid Balancer pool id '{value}'."),
		};
	}

	private static (BalancerToken BaseToken, BalancerToken QuoteToken)
		OrientMarket(BalancerToken left, BalancerToken right)
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
			"GHO" => 88,
			"WETH" => 75,
			"ETH" => 75,
			"CBBTC" => 65,
			"WBTC" => 65,
			_ => 0,
		};

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

	private async ValueTask TryConnectSocketAsync(
		CancellationToken cancellationToken)
	{
		DisposeSocket();
		try
		{
			_socketClient = new(WebSocketEndpoint)
			{
				Parent = this,
			};
			_socketClient.LogReceived += OnSocketLog;
			await _socketClient.ConnectAsync(_deployment, cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			this.AddWarningLog(
				"Balancer WebSocket is unavailable; GraphQL polling remains " +
				"active: {0}", error.Message);
			DisposeSocket();
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
		_deployment = null;
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
			_nextSocketReconnect = default;
		}
	}
}
