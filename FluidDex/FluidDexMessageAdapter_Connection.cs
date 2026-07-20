namespace StockSharp.FluidDex;

public partial class FluidDexMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rpcClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			var rpcEndpoint = RpcEndpoint.IsEmpty()
				? Chain.GetDefaultRpcEndpoint()
				: RpcEndpoint;
			var webSocketEndpoint = WebSocketEndpoint.IsEmpty()
				? Chain.GetDefaultWebSocketEndpoint()
				: WebSocketEndpoint;
			_rpcClient = new(rpcEndpoint, Chain, FactoryAddress,
				ResolverAddress, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await RpcClient.VerifyChainAsync(cancellationToken);
			WalletAddress = RpcClient.IsWalletConfigured
				? RpcClient.WalletAddress
				: null;
			_ = await GetOrLoadTokenAsync(
				FluidDexExtensions.NativeTokenAddress, cancellationToken);
			FluidDexMarketDefinition[] definitions;
			try
			{
				definitions = ParseMarketDefinitions();
			}
			catch (Exception error)
			{
				throw new InvalidOperationException(
					"Configured Fluid DEX pool definitions are invalid.",
					error);
			}
			var errors = new List<Exception>();
			if (definitions.Length == 0)
			{
				try
				{
					var pools = await RpcClient.DiscoverPoolsAsync(
						MaximumDiscoveredPools, cancellationToken);
					foreach (var pool in pools)
						RegisterPool(pool, null);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog(
						"Fluid DEX pool discovery failed: {0}", error.Message);
				}
			}
			else
			{
				foreach (var definition in definitions)
				{
					try
					{
						await RegisterDefinitionAsync(definition,
							cancellationToken);
					}
					catch (Exception error) when (
						!cancellationToken.IsCancellationRequested)
					{
						errors.Add(error);
						this.AddWarningLog(
							"Fluid DEX pool {0} loading failed: {1}",
							definition.PoolId, error.Message);
					}
				}
			}
			FluidDexMarket[] markets;
			using (_sync.EnterScope())
				markets = [.. _markets.Values];
			if (markets.Length == 0)
			{
				if (errors.Count == 1)
					throw errors[0];
				if (errors.Count > 1)
					throw new AggregateException(
						"No Fluid DEX pools could be loaded.", errors);
				throw new InvalidOperationException(
					"The Fluid DEX factory returned no loadable pools.");
			}
			if (!webSocketEndpoint.IsEmpty())
			{
				try
				{
					_socketClient = new(webSocketEndpoint)
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
						"Fluid DEX WebSocket is unavailable; JSON-RPC log " +
						"polling remains active: {0}", error.Message);
					DisposeSocket();
				}
			}
			connectMsg.SessionId = RpcClient.IsWalletConfigured
				? $"Fluid DEX {Chain} {RpcClient.WalletAddress}"
				: $"Fluid DEX {Chain} public";
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
				_depthSubscriptions.Count > 0 ||
				_tickSubscriptions.Count > 0 ||
				_candleSubscriptions.Count > 0;
			if (_rpcClient is not null && hasMarketSubscriptions &&
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

	private async ValueTask RegisterDefinitionAsync(
		FluidDexMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var pool = await RpcClient.GetPoolAsync(definition.PoolId,
			cancellationToken);
		RegisterPool(pool, definition);
	}

	private void RegisterPool(FluidDexPool pool,
		FluidDexMarketDefinition definition)
	{
		FluidDexToken baseToken;
		FluidDexToken quoteToken;
		if (definition is not null && !definition.BaseToken.IsEmpty())
		{
			var baseAddress = definition.BaseToken.NormalizeAddress();
			var quoteAddress = definition.QuoteToken.NormalizeAddress();
			if (baseAddress.EqualsIgnoreCase(pool.Token0.Address) &&
				quoteAddress.EqualsIgnoreCase(pool.Token1.Address))
			{
				baseToken = pool.Token0;
				quoteToken = pool.Token1;
			}
			else if (baseAddress.EqualsIgnoreCase(pool.Token1.Address) &&
				quoteAddress.EqualsIgnoreCase(pool.Token0.Address))
			{
				baseToken = pool.Token1;
				quoteToken = pool.Token0;
			}
			else
			{
				throw new InvalidDataException(
					$"Configured base/quote addresses do not match pool " +
					$"'{pool.PoolId}'.");
			}
		}
		else
		{
			(baseToken, quoteToken) = OrientMarket(pool.Token0, pool.Token1);
		}
		var code = definition?.SecurityCode.IsEmpty() != false
			? CreateSecurityCode(baseToken, quoteToken, pool)
			: NormalizeSecurityCode(definition.SecurityCode);
		using (_sync.EnterScope())
		{
			if (_markets.ContainsKey(code))
				code += "-" + pool.PoolId[2..8].ToUpperInvariant();
		}
		RegisterMarket(new()
		{
			PoolId = pool.PoolId,
			DexId = pool.DexId,
			Fee = pool.Fee,
			Token0 = pool.Token0,
			Token1 = pool.Token1,
			BaseToken = baseToken,
			QuoteToken = quoteToken,
			SecurityCode = code,
		});
	}

	private async ValueTask<FluidDexToken> GetOrLoadTokenAsync(
		string address, CancellationToken cancellationToken)
	{
		address = address.NormalizeAddress();
		using (_sync.EnterScope())
			if (_tokens.TryGetValue(address, out var cached))
				return cached;
		var token = await RpcClient.GetTokenAsync(address,
			cancellationToken);
		using (_sync.EnterScope())
		{
			_tokens[token.Address] = token;
			return token;
		}
	}

	private void RegisterMarket(FluidDexMarket market)
	{
		if (market?.PoolId.IsEmpty() != false || market.BaseToken is null ||
			market.QuoteToken is null || market.Token0 is null ||
			market.Token1 is null || market.SecurityCode.IsEmpty())
			throw new InvalidDataException(
				"Fluid DEX pool metadata is incomplete.");
		using (_sync.EnterScope())
		{
			if (_marketsByPool.ContainsKey(market.PoolId))
				throw new InvalidOperationException(
					$"Fluid DEX pool '{market.PoolId}' is configured twice.");
			if (_markets.ContainsKey(market.SecurityCode))
				throw new InvalidOperationException(
					$"Fluid DEX security code '{market.SecurityCode}' is " +
					"configured twice.");
			_tokens[market.Token0.Address] = market.Token0;
			_tokens[market.Token1.Address] = market.Token1;
			_markets.Add(market.SecurityCode, market);
			_marketsByPool.Add(market.PoolId, market);
		}
	}

	private FluidDexMarketDefinition[] ParseMarketDefinitions()
	{
		if (Pools.IsEmpty())
			return [];
		var result = new List<FluidDexMarketDefinition>();
		foreach (var item in Pools.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not 1 and not 3 and not 4)
				throw new FormatException(
					"Each Fluid DEX pool must use pool or " +
					"pool|base-token|quote-token|security-code format.");
			if (fields.Length >= 3 &&
				(fields[1].IsEmpty() || fields[2].IsEmpty()))
				throw new FormatException(
					"Both Fluid DEX base and quote token addresses are " +
					"required when orientation is configured.");
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

	private static (FluidDexToken BaseToken, FluidDexToken QuoteToken)
		OrientMarket(FluidDexToken token0, FluidDexToken token1)
		=> QuotePriority(token0.Symbol) > QuotePriority(token1.Symbol)
			? (token1, token0)
			: (token0, token1);

	private static int QuotePriority(string symbol)
		=> symbol?.ToUpperInvariant() switch
		{
			"USDC" => 100,
			"USDT" => 99,
			"USDT0" => 98,
			"USDBC" => 97,
			"GHO" => 96,
			"USDE" => 95,
			"SUSDE" => 94,
			"DAI" => 93,
			"USDAI" => 92,
			"USD0" => 91,
			"USD0++" => 90,
			"WETH" => 75,
			"ETH" => 75,
			"CBBTC" => 65,
			"WBTC" => 65,
			_ => 0,
		};

	private static string CreateSecurityCode(FluidDexToken baseToken,
		FluidDexToken quoteToken, FluidDexPool pool)
		=> NormalizeSecurityCode(
			$"{baseToken.Symbol}-{quoteToken.Symbol}-F{pool.Fee}");

	private static string NormalizeSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
			throw new FormatException(
				$"Invalid Fluid DEX security code '{value}'.");
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
			_depthSubscriptions.Clear();
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
