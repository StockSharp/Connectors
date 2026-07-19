namespace StockSharp.PancakeSwap;

public partial class PancakeSwapMessageAdapter
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
				PancakeSwapExtensions.NativeTokenAddress, cancellationToken);

			RegisterGraphClient(PancakeSwapPoolVersions.V2,
				ResolveSubgraph(V2Subgraph,
					Chain.GetDefaultV2Subgraph()));
			var v3Source = V3Subgraph;
			if (Chain != PancakeSwapChains.BnbSmartChain &&
				v3Source.EqualsIgnoreCase(_defaultV3SubgraphId))
				v3Source = null;
			RegisterGraphClient(PancakeSwapPoolVersions.V3,
				ResolveSubgraph(v3Source,
					Chain.GetDefaultV3Subgraph()));

			var errors = new List<Exception>();
			PancakeSwapMarketDefinition[] definitions;
			try
			{
				definitions = ParseMarketDefinitions();
			}
			catch (Exception error)
			{
				throw new InvalidOperationException(
					"Configured PancakeSwap market definitions are invalid.",
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
						"PancakeSwap {0} pool loading failed: {1}",
						definition.PoolVersion, error.Message);
				}
			}

			KeyValuePair<PancakeSwapPoolVersions,
				PancakeSwapGraphClient>[] graphClients;
			using (_sync.EnterScope())
				graphClients = [.. _graphClients];
			foreach (var graph in graphClients)
			{
				try
				{
					RegisterDiscoveredPools(graph.Key,
						await graph.Value.GetPoolsAsync(
							MaximumDiscoveredPools, cancellationToken));
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog(
						"PancakeSwap {0} subgraph discovery failed: {1}",
						graph.Key, error.Message);
				}
			}

			using (_sync.EnterScope())
				if (_markets.Count == 0)
					throw errors.Count == 1
						? errors[0]
						: new AggregateException(
							"No PancakeSwap markets could be loaded.", errors);

			connectMsg.SessionId = $"PancakeSwap {Chain} " +
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

	private void RegisterGraphClient(PancakeSwapPoolVersions poolVersion,
		string source)
	{
		if (source.IsEmpty())
			return;
		if (!Uri.TryCreate(source, UriKind.Absolute, out _) &&
			GraphApiKey.IsEmpty())
		{
			this.AddWarningLog(
				"The Graph API key is not configured; PancakeSwap {0} " +
				"subgraph features are disabled.", poolVersion);
			return;
		}
		var client = new PancakeSwapGraphClient(GraphApiKey, source,
			poolVersion)
		{
			Parent = this,
		};
		using (_sync.EnterScope())
			_graphClients[poolVersion] = client;
	}

	private async ValueTask RegisterDefinitionAsync(
		PancakeSwapMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var baseToken = await GetOrLoadTokenAsync(definition.BaseToken,
			cancellationToken);
		var quoteToken = await GetOrLoadTokenAsync(definition.QuoteToken,
			cancellationToken);
		var ordered = OrderTokens(baseToken, quoteToken);
		var poolId = await RpcClient.GetPoolAddressAsync(
			definition.PoolVersion, baseToken.Address, quoteToken.Address,
			definition.Fee, cancellationToken);
		RegisterMarket(new()
		{
			PoolId = poolId,
			PoolVersion = definition.PoolVersion,
			Fee = definition.Fee,
			Token0 = ordered.Token0,
			Token1 = ordered.Token1,
			BaseToken = baseToken,
			QuoteToken = quoteToken,
		});
	}

	private void RegisterDiscoveredPools(PancakeSwapPoolVersions poolVersion,
		IEnumerable<PancakeSwapPool> pools)
	{
		foreach (var pool in pools ?? [])
		{
			if (pool?.Id.IsEmpty() != false || pool.Token0 is null ||
				pool.Token1 is null)
				continue;
			if (!int.TryParse(pool.Token0.Decimals,
					NumberStyles.Integer, CultureInfo.InvariantCulture,
					out var token0Decimals) ||
				!int.TryParse(pool.Token1.Decimals,
					NumberStyles.Integer, CultureInfo.InvariantCulture,
					out var token1Decimals) ||
				token0Decimals is < 0 or > 255 ||
				token1Decimals is < 0 or > 255)
				continue;
			var fee = 0;
			if (poolVersion == PancakeSwapPoolVersions.V3 &&
				(!int.TryParse(pool.FeeTier, NumberStyles.Integer,
					CultureInfo.InvariantCulture, out fee) ||
					fee is <= 0 or > 1_000_000))
				continue;
			PancakeSwapToken token0;
			PancakeSwapToken token1;
			string poolId;
			try
			{
				poolId = pool.Id.NormalizeAddress();
				token0 = new()
				{
					Address = pool.Token0.Id.NormalizeAddress(),
					Symbol = pool.Token0.Symbol?.Trim().ToUpperInvariant(),
					Name = pool.Token0.Name?.Trim(),
					Decimals = token0Decimals,
				};
				token1 = new()
				{
					Address = pool.Token1.Id.NormalizeAddress(),
					Symbol = pool.Token1.Symbol?.Trim().ToUpperInvariant(),
					Name = pool.Token1.Name?.Trim(),
					Decimals = token1Decimals,
				};
			}
			catch (ArgumentException)
			{
				continue;
			}
			if (token0.Symbol.IsEmpty() || token1.Symbol.IsEmpty())
				continue;
			var oriented = OrientMarket(token0, token1);
			RegisterMarket(new()
			{
				PoolId = poolId,
				PoolVersion = poolVersion,
				Fee = fee,
				Token0 = token0,
				Token1 = token1,
				BaseToken = oriented.BaseToken,
				QuoteToken = oriented.QuoteToken,
				TotalValueLockedUsd =
					pool.TotalValueLockedUsd.ToDecimalInvariant() ??
					pool.ReserveUsd.ToDecimalInvariant() ?? 0m,
			});
		}
	}

	private async ValueTask<PancakeSwapToken> GetOrLoadTokenAsync(
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

	private void RegisterMarket(PancakeSwapMarket market)
	{
		if (market?.PoolId.IsEmpty() != false || market.BaseToken is null ||
			market.QuoteToken is null || market.Token0 is null ||
			market.Token1 is null || market.BaseToken.Symbol.IsEmpty() ||
			market.QuoteToken.Symbol.IsEmpty())
			return;
		using (_sync.EnterScope())
		{
			_tokens[market.Token0.Address] = market.Token0;
			_tokens[market.Token1.Address] = market.Token1;
			if (_markets.TryGetValue(market.SecurityCode, out var existing))
			{
				if (existing.BaseToken.Address.EqualsIgnoreCase(
						market.BaseToken.Address) &&
					existing.QuoteToken.Address.EqualsIgnoreCase(
						market.QuoteToken.Address) &&
					market.TotalValueLockedUsd >
						existing.TotalValueLockedUsd)
					_markets[market.SecurityCode] = market;
				return;
			}
			_markets.Add(market.SecurityCode, market);
		}
	}

	private PancakeSwapMarketDefinition[] ParseMarketDefinitions()
	{
		if (Markets.IsEmpty() ||
			(Chain != PancakeSwapChains.BnbSmartChain &&
				Markets.EqualsIgnoreCase(_defaultMarkets)))
			return [];
		var result = new List<PancakeSwapMarketDefinition>();
		foreach (var item in Markets.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|',
				StringSplitOptions.TrimEntries);
			if (fields.Length != 4 ||
				!Enum.TryParse<PancakeSwapPoolVersions>(fields[0], true,
					out var poolVersion) ||
				!int.TryParse(fields[3], NumberStyles.Integer,
					CultureInfo.InvariantCulture, out var fee))
				throw new FormatException(
					"Each PancakeSwap market must use " +
					"version|base-token|quote-token|fee format.");
			if (poolVersion == PancakeSwapPoolVersions.V2 && fee != 0 ||
				poolVersion == PancakeSwapPoolVersions.V3 &&
					fee is <= 0 or > 1_000_000)
				throw new FormatException(
					"V2 fee must be zero and v3 fee must be positive.");
			result.Add(new()
			{
				PoolVersion = poolVersion,
				BaseToken = fields[1].NormalizeAddress(),
				QuoteToken = fields[2].NormalizeAddress(),
				Fee = fee,
			});
		}
		return [.. result];
	}

	private static (PancakeSwapToken Token0, PancakeSwapToken Token1)
		OrderTokens(PancakeSwapToken left, PancakeSwapToken right)
		=> AddressValue(left.Address) < AddressValue(right.Address)
			? (left, right)
			: (right, left);

	private static (PancakeSwapToken BaseToken, PancakeSwapToken QuoteToken)
		OrientMarket(PancakeSwapToken token0, PancakeSwapToken token1)
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
			"WBNB" => 75,
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
		PancakeSwapGraphClient[] graphClients;
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
