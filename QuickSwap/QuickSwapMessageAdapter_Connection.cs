namespace StockSharp.QuickSwap;

public partial class QuickSwapMessageAdapter
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
			_rpcClient = new(RpcEndpoint, Chain, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await RpcClient.VerifyChainAsync(cancellationToken);
			WalletAddress = RpcClient.WalletAddress;
			_ = await GetOrLoadTokenAsync(
				QuickSwapExtensions.NativeTokenAddress, cancellationToken);

			RegisterGraphClient(QuickSwapPoolVersions.V2,
				ResolveSubgraph(V2Subgraph,
					Chain.GetDefaultV2Subgraph()));
			RegisterGraphClient(QuickSwapPoolVersions.V3,
				ResolveSubgraph(V3Subgraph,
					Chain.GetDefaultV3Subgraph()));

			var errors = new List<Exception>();
			QuickSwapMarketDefinition[] definitions;
			try
			{
				definitions = ParseMarketDefinitions();
			}
			catch (Exception error)
			{
				throw new InvalidOperationException(
					"Configured QuickSwap market definitions are invalid.",
					error);
			}
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
						"QuickSwap {0} pool loading failed: {1}",
						definition.PoolVersion, error.Message);
				}
			}

			KeyValuePair<QuickSwapPoolVersions,
				QuickSwapGraphClient>[] graphClients;
			using (_sync.EnterScope())
				graphClients = [.. _graphClients];
			foreach (var graph in graphClients)
			{
				try
				{
					await RegisterDiscoveredPoolsAsync(graph.Key,
						await graph.Value.GetPoolsAsync(
							MaximumDiscoveredPools, cancellationToken),
						cancellationToken);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog(
						"QuickSwap {0} subgraph discovery failed: {1}",
						graph.Key, error.Message);
				}
			}

			using (_sync.EnterScope())
				if (_markets.Count == 0)
					throw errors.Count == 1
						? errors[0]
						: new AggregateException(
							"No QuickSwap markets could be loaded.", errors);

			connectMsg.SessionId = $"QuickSwap {Chain} " +
				RpcClient.WalletAddress;
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
			if (_rpcClient is not null &&
				(_level1Subscriptions.Count > 0 ||
					_tickSubscriptions.Count > 0 ||
					_candleSubscriptions.Count > 0) &&
				CurrentTime >= _nextMarketPoll)
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

	private string ResolveSubgraph(string configured, string fallback)
		=> configured.IsEmpty() ? fallback : configured.Trim();

	private void RegisterGraphClient(QuickSwapPoolVersions poolVersion,
		string source)
	{
		if (source.IsEmpty())
			return;
		if (!Uri.TryCreate(source, UriKind.Absolute, out _) &&
			GraphApiKey.IsEmpty())
		{
			this.AddWarningLog(
				"The Graph API key is not configured; QuickSwap {0} " +
				"subgraph features are disabled.", poolVersion);
			return;
		}
		var client = new QuickSwapGraphClient(GraphApiKey, source,
			poolVersion)
		{
			Parent = this,
		};
		using (_sync.EnterScope())
			_graphClients[poolVersion] = client;
	}

	private async ValueTask RegisterDefinitionAsync(
		QuickSwapMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var baseToken = await GetOrLoadTokenAsync(definition.BaseToken,
			cancellationToken);
		var quoteToken = await GetOrLoadTokenAsync(definition.QuoteToken,
			cancellationToken);
		var ordered = OrderTokens(baseToken, quoteToken);
		var poolId = await RpcClient.GetPoolAddressAsync(
			definition.PoolVersion, baseToken.Address, quoteToken.Address,
			cancellationToken);
		RegisterMarket(new()
		{
			PoolId = poolId,
			PoolVersion = definition.PoolVersion,
			Token0 = ordered.Token0,
			Token1 = ordered.Token1,
			BaseToken = baseToken,
			QuoteToken = quoteToken,
		});
	}

	private async ValueTask RegisterDiscoveredPoolsAsync(
		QuickSwapPoolVersions poolVersion, IEnumerable<QuickSwapPool> pools,
		CancellationToken cancellationToken)
	{
		var failed = 0;
		string firstError = null;
		foreach (var pool in pools ?? [])
		{
			if (pool?.Id.IsEmpty() != false || pool.Token0 is null ||
				pool.Token1 is null)
			{
				failed++;
				firstError ??= "A pool record is incomplete.";
				continue;
			}
			QuickSwapToken token0;
			QuickSwapToken token1;
			string poolId;
			try
			{
				poolId = pool.Id.NormalizeAddress();
				var token0Address = pool.Token0.Id.NormalizeAddress();
				var token1Address = pool.Token1.Id.NormalizeAddress();
				var factoryPool = await RpcClient.GetPoolAddressAsync(
					poolVersion, token0Address, token1Address,
					cancellationToken);
				if (!factoryPool.EqualsIgnoreCase(poolId))
					throw new InvalidDataException(
						$"Subgraph pool '{poolId}' does not match factory " +
						$"pool '{factoryPool}'.");
				token0 = await GetOrLoadTokenAsync(token0Address,
					cancellationToken);
				token1 = await GetOrLoadTokenAsync(token1Address,
					cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				failed++;
				firstError ??= error.Message;
				continue;
			}
			var ordered = OrderTokens(token0, token1);
			var oriented = OrientMarket(ordered.Token0, ordered.Token1);
			RegisterMarket(new()
			{
				PoolId = poolId,
				PoolVersion = poolVersion,
				Token0 = ordered.Token0,
				Token1 = ordered.Token1,
				BaseToken = oriented.BaseToken,
				QuoteToken = oriented.QuoteToken,
				TotalValueLockedUsd =
					pool.TotalValueLockedUsd.ToDecimalInvariant() ??
					pool.ReserveUsd.ToDecimalInvariant() ?? 0m,
			});
		}
		if (failed > 0)
			this.AddWarningLog(
				"QuickSwap {0} discovery skipped {1} invalid pools. " +
				"First error: {2}", poolVersion, failed, firstError);
	}

	private async ValueTask<QuickSwapToken> GetOrLoadTokenAsync(
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

	private void RegisterMarket(QuickSwapMarket market)
	{
		if (market?.PoolId.IsEmpty() != false || market.BaseToken is null ||
			market.QuoteToken is null || market.Token0 is null ||
			market.Token1 is null || market.BaseToken.Symbol.IsEmpty() ||
			market.QuoteToken.Symbol.IsEmpty())
			return;
		var pair = $"{market.BaseToken.Symbol}-{market.QuoteToken.Symbol}"
			.ToUpperInvariant();
		using (_sync.EnterScope())
		{
			_tokens[market.Token0.Address] = market.Token0;
			_tokens[market.Token1.Address] = market.Token1;
			var sameTokens = _markets.Values.FirstOrDefault(existing =>
				existing.BaseToken.Address.EqualsIgnoreCase(
					market.BaseToken.Address) &&
				existing.QuoteToken.Address.EqualsIgnoreCase(
					market.QuoteToken.Address));
			if (sameTokens is not null)
			{
				if (market.TotalValueLockedUsd >
					sameTokens.TotalValueLockedUsd)
				{
					market.SecurityCode = sameTokens.SecurityCode;
					_markets[market.SecurityCode] = market;
				}
				return;
			}

			var matches = _markets.Values.Where(existing =>
				existing.BaseToken.Symbol.Equals(
					market.BaseToken.Symbol,
					StringComparison.OrdinalIgnoreCase) &&
				existing.QuoteToken.Symbol.Equals(
					market.QuoteToken.Symbol,
					StringComparison.OrdinalIgnoreCase)).ToArray();
			if (matches.Length > 0)
			{
				foreach (var existing in matches.Where(existing =>
					existing.SecurityCode.Equals(pair,
						StringComparison.OrdinalIgnoreCase)))
				{
					_markets.Remove(existing.SecurityCode);
					existing.SecurityCode = BuildUniqueMarketCode(pair,
						existing.PoolId);
					_markets.Add(existing.SecurityCode, existing);
				}
				market.SecurityCode = BuildUniqueMarketCode(pair,
					market.PoolId);
			}
			else
				market.SecurityCode = pair;
			if (_markets.ContainsKey(market.SecurityCode))
				throw new InvalidDataException(
					$"Duplicate QuickSwap security code " +
					$"'{market.SecurityCode}'.");
			_markets.Add(market.SecurityCode, market);
		}
	}

	private string BuildUniqueMarketCode(string pair, string poolId)
	{
		poolId = poolId.NormalizeAddress()[2..];
		for (var length = 6; length <= poolId.Length; length += 2)
		{
			var code = $"{pair}-{poolId[..length].ToUpperInvariant()}";
			if (!_markets.ContainsKey(code))
				return code;
		}
		throw new InvalidDataException(
			$"Unable to create a unique QuickSwap security code for " +
			$"pool '{poolId}'.");
	}

	private QuickSwapMarketDefinition[] ParseMarketDefinitions()
	{
		if (Markets.IsEmpty())
			return [];
		var result = new List<QuickSwapMarketDefinition>();
		foreach (var item in Markets.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|',
				StringSplitOptions.TrimEntries);
			if (fields.Length != 3 ||
				!Enum.TryParse<QuickSwapPoolVersions>(fields[0], true,
					out var poolVersion) || !Enum.IsDefined(poolVersion))
				throw new FormatException(
					"Each QuickSwap market must use " +
					"version|base-token|quote-token format.");
			result.Add(new()
			{
				PoolVersion = poolVersion,
				BaseToken = fields[1].NormalizeAddress(),
				QuoteToken = fields[2].NormalizeAddress(),
			});
		}
		return [.. result];
	}

	private static (QuickSwapToken Token0, QuickSwapToken Token1)
		OrderTokens(QuickSwapToken left, QuickSwapToken right)
		=> AddressValue(left.Address) < AddressValue(right.Address)
			? (left, right)
			: (right, left);

	private static (QuickSwapToken BaseToken, QuickSwapToken QuoteToken)
		OrientMarket(QuickSwapToken token0, QuickSwapToken token1)
		=> QuotePriority(token0.Symbol) > QuotePriority(token1.Symbol)
			? (token1, token0)
			: (token0, token1);

	private static int QuotePriority(string symbol)
		=> symbol?.ToUpperInvariant() switch
		{
			"USDC" => 100,
			"USDT" => 95,
			"DAI" => 90,
			"FDUSD" => 85,
			"WPOL" => 75,
			"WMATIC" => 75,
			"POL" => 75,
			"MATIC" => 75,
			"WETH" => 70,
			"ETH" => 70,
			"BTCB" => 60,
			"WBTC" => 60,
			_ => 0,
		};

	private static BigInteger AddressValue(string address)
		=> BigInteger.Parse("0" + address.NormalizeAddress()[2..],
			NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

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
		QuickSwapGraphClient[] graphClients;
		using (_sync.EnterScope())
		{
			graphClients = [.. _graphClients.Values];
			_graphClients.Clear();
		}
		foreach (var graphClient in graphClients)
			graphClient.Dispose();
		_rpcClient?.Dispose();
		_rpcClient = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
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
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
		}
	}
}
