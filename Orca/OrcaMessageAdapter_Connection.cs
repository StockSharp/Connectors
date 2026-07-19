namespace StockSharp.Orca;

public partial class OrcaMessageAdapter
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
			RpcEndpoint = RpcEndpoint.IsEmpty()
				? Cluster.GetRpcEndpoint()
				: RpcEndpoint.Trim();
			StreamingEndpoint = StreamingEndpoint.IsEmpty()
				? Cluster.GetSocketEndpoint()
				: StreamingEndpoint.Trim();
			ApiEndpoint = ApiEndpoint.IsEmpty()
				? _defaultApiEndpoint
				: ApiEndpoint.Trim();
			_rpcClient = new(RpcEndpoint, Cluster, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await RpcClient.VerifyAsync(cancellationToken);
			WalletAddress = RpcClient.WalletAddress;
			if (Cluster == OrcaClusters.Mainnet)
				_apiClient = new(ApiEndpoint) { Parent = this };

			var definitions = await LoadMarketDefinitionsAsync(
				cancellationToken);
			if (definitions.Length == 0)
				throw new InvalidOperationException(
					"At least one Orca pool must be configured or discovered.");
			var errors = new List<Exception>();
			foreach (var definition in definitions)
			{
				try
				{
					RegisterMarket(await LoadMarketAsync(definition,
						cancellationToken));
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog("Orca pool {0} loading failed: {1}",
						definition.PoolAddress, error.Message);
				}
			}
			using (_sync.EnterScope())
				if (_markets.Count == 0)
					throw errors.Count == 1
						? errors[0]
						: new AggregateException(
							"No Orca pools could be loaded.", errors);

			await TryConnectSocketAsync(cancellationToken);
			connectMsg.SessionId = $"Orca {Cluster} " +
				(RpcClient.IsWalletAvailable
					? RpcClient.WalletAddress[..8]
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
		var reconnectSocket = false;
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
			if (_rpcClient is not null && RpcClient.IsWalletAvailable &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_trackedSwaps.Values.Any(static swap =>
						swap.State == OrderStates.Active)) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				pollPrivate = true;
			}
			reconnectSocket = _rpcClient is not null &&
				(_socketClient is null || !_socketClient.IsConnected) &&
				CurrentTime >= _nextSocketReconnect;
			if (reconnectSocket)
				_nextSocketReconnect = CurrentTime + TimeSpan.FromSeconds(15);
		}
		if (reconnectSocket)
			await RunSafelyAsync(TryConnectSocketAsync, cancellationToken);
		if (pollMarket)
			await RunSafelyAsync(PollMarketAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask<OrcaMarketDefinition[]>
		LoadMarketDefinitionsAsync(CancellationToken cancellationToken)
	{
		var explicitDefinitions = ParseMarketDefinitions();
		var definitions = new Dictionary<string, OrcaMarketDefinition>(
			StringComparer.Ordinal);
		if (_apiClient is not null && MaximumDiscoveredPools > 0)
		{
			try
			{
				foreach (var pool in await _apiClient.GetPoolsAsync(
					MaximumDiscoveredPools, cancellationToken))
					definitions[pool.Address.NormalizePublicKey()] = new()
					{
						PoolAddress = pool.Address.NormalizePublicKey(),
						ApiPool = pool,
					};
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested &&
				explicitDefinitions.Length > 0)
			{
				this.AddWarningLog(
					"Orca pool discovery failed; configured pools remain active: {0}",
					error.Message);
			}
		}
		foreach (var definition in explicitDefinitions)
		{
			OrcaApiPool apiPool = null;
			if (_apiClient is not null)
			{
				try
				{
					apiPool = await _apiClient.GetPoolAsync(
						definition.PoolAddress, cancellationToken);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					this.AddWarningLog(
						"Orca metadata for configured pool {0} is unavailable: {1}",
						definition.PoolAddress, error.Message);
				}
			}
			definitions[definition.PoolAddress] = new()
			{
				PoolAddress = definition.PoolAddress,
				BaseSymbol = definition.BaseSymbol,
				QuoteSymbol = definition.QuoteSymbol,
				ApiPool = apiPool ?? definitions.GetValueOrDefault(
					definition.PoolAddress)?.ApiPool,
			};
		}
		return [.. definitions.Values];
	}

	private async ValueTask<OrcaMarket> LoadMarketAsync(
		OrcaMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var poolAccount = await RpcClient.GetAccountAsync(
			definition.PoolAddress, cancellationToken);
		if (poolAccount is null)
			throw new InvalidDataException(
				$"Orca pool '{definition.PoolAddress}' was not found.");
		var market = OrcaExtensions.DecodeWhirlpool(definition.PoolAddress,
			poolAccount);
		ValidateApiPool(market, definition.ApiPool);
		var accounts = await RpcClient.GetAccountsAsync(
		[
			market.TokenA.Mint,
			market.TokenB.Mint,
			OrcaExtensions.MetadataAddress(market.TokenA.Mint),
			OrcaExtensions.MetadataAddress(market.TokenB.Mint),
		], cancellationToken);
		if (accounts.Length != 4 || accounts[0] is null || accounts[1] is null)
			throw new InvalidDataException(
				$"Orca pool '{definition.PoolAddress}' has missing mint accounts.");
		var metadataA = OrcaExtensions.DecodeMetadata(accounts[2],
			market.TokenA.Mint);
		var metadataB = OrcaExtensions.DecodeMetadata(accounts[3],
			market.TokenB.Mint);
		market.TokenA = OrcaExtensions.DecodeMint(market.TokenA.Mint,
			accounts[0], definition.BaseSymbol ?? metadataA.Symbol,
			metadataA.Name, definition.ApiPool?.TokenA);
		market.TokenB = OrcaExtensions.DecodeMint(market.TokenB.Mint,
			accounts[1], definition.QuoteSymbol ?? metadataB.Symbol,
			metadataB.Name, definition.ApiPool?.TokenB);
		return market;
	}

	private static void ValidateApiPool(OrcaMarket market, OrcaApiPool pool)
	{
		if (pool is null)
			return;
		if (!pool.Address.Equals(market.PoolAddress, StringComparison.Ordinal) ||
			!pool.WhirlpoolsConfig.Equals(market.WhirlpoolsConfig,
				StringComparison.Ordinal) ||
			pool.TickSpacing != market.TickSpacing ||
			pool.FeeRate != market.FeeRate ||
			!pool.TokenMintA.Equals(market.TokenA.Mint,
				StringComparison.Ordinal) ||
			!pool.TokenMintB.Equals(market.TokenB.Mint,
				StringComparison.Ordinal) ||
			!pool.TokenVaultA.Equals(market.TokenVaultA,
				StringComparison.Ordinal) ||
			!pool.TokenVaultB.Equals(market.TokenVaultB,
				StringComparison.Ordinal) ||
			pool.IsAdaptiveFeeEnabled != market.IsAdaptiveFee)
			throw new InvalidDataException(
				$"Orca API metadata for pool '{market.PoolAddress}' does not " +
				"match the on-chain account.");
	}

	private void RegisterMarket(OrcaMarket market)
	{
		var pair = $"{market.TokenA.Symbol}-{market.TokenB.Symbol}"
			.ToUpperInvariant();
		using (_sync.EnterScope())
		{
			if (_marketsByPool.ContainsKey(market.PoolAddress))
				return;
			var matches = _marketsByPool.Values.Where(existing =>
				existing.TokenA.Symbol.Equals(market.TokenA.Symbol,
					StringComparison.OrdinalIgnoreCase) &&
				existing.TokenB.Symbol.Equals(market.TokenB.Symbol,
					StringComparison.OrdinalIgnoreCase)).ToArray();
			if (matches.Length > 0)
			{
				foreach (var existing in matches.Where(existing =>
					existing.SecurityCode.Equals(pair,
						StringComparison.OrdinalIgnoreCase)))
				{
					_markets.Remove(existing.SecurityCode);
					existing.SecurityCode = BuildUniquePoolCode(pair,
						existing.PoolAddress);
					_markets.Add(existing.SecurityCode, existing);
				}
				market.SecurityCode = BuildUniquePoolCode(pair,
					market.PoolAddress);
			}
			else
				market.SecurityCode = pair;
			if (_markets.ContainsKey(market.SecurityCode))
				throw new InvalidDataException(
					$"Duplicate Orca security code '{market.SecurityCode}'.");
			_markets.Add(market.SecurityCode, market);
			_marketsByPool.Add(market.PoolAddress, market);
			_tokens[market.TokenA.Mint] = market.TokenA;
			_tokens[market.TokenB.Mint] = market.TokenB;
		}
	}

	private OrcaMarketDefinition[] ParseMarketDefinitions()
	{
		if (Pools.IsEmpty())
			return [];
		var result = new List<OrcaMarketDefinition>();
		foreach (var item in Pools.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not (1 or 3))
				throw new FormatException(
					"Each Orca market must use pool or " +
					"pool|base-symbol|quote-symbol format.");
			result.Add(new()
			{
				PoolAddress = fields[0].NormalizePublicKey(),
				BaseSymbol = fields.Length == 3
					? NormalizeSymbolOverride(fields[1])
					: null,
				QuoteSymbol = fields.Length == 3
					? NormalizeSymbolOverride(fields[2])
					: null,
			});
		}
		return [.. result.GroupBy(static definition =>
			definition.PoolAddress, StringComparer.Ordinal)
			.Select(static group => group.First())];
	}

	private async ValueTask TryConnectSocketAsync(
		CancellationToken cancellationToken)
	{
		OrcaSocketClient previous;
		string[] pools;
		using (_sync.EnterScope())
		{
			if (_socketClient?.IsConnected == true)
				return;
			previous = _socketClient;
			_socketClient = null;
			pools = [.. _marketsByPool.Keys];
		}
		previous?.Dispose();
		var client = new OrcaSocketClient(StreamingEndpoint,
			OnSocketLogsAsync, OnSocketErrorAsync)
		{
			Parent = this,
		};
		try
		{
			await client.ConnectAsync(pools, cancellationToken);
			using (_sync.EnterScope())
				_socketClient = client;
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			client.Dispose();
			using (_sync.EnterScope())
				_nextSocketReconnect = CurrentTime + TimeSpan.FromSeconds(15);
			this.AddWarningLog(
				"Orca WebSocket unavailable; polling remains active: {0}",
				error.Message);
		}
	}

	private ValueTask OnSocketLogsAsync(string signature, string[] logs)
		=> ProcessRealtimeEventsAsync(signature, logs);

	private async ValueTask OnSocketErrorAsync(Exception error)
	{
		OrcaSocketClient client;
		using (_sync.EnterScope())
		{
			client = _socketClient;
			_socketClient = null;
			_nextSocketReconnect = CurrentTime + TimeSpan.FromSeconds(15);
		}
		client?.Dispose();
		await SendOutErrorAsync(error, CancellationToken.None);
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
		OrcaSocketClient socketClient;
		using (_sync.EnterScope())
		{
			socketClient = _socketClient;
			_socketClient = null;
		}
		socketClient?.Dispose();
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
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
			_nextSocketReconnect = default;
		}
	}

	private string BuildUniquePoolCode(string pair, string poolAddress)
	{
		for (var length = 6; length <= 20; length += 2)
		{
			var code = $"{pair}-{poolAddress[..length].ToUpperInvariant()}";
			if (!_markets.ContainsKey(code))
				return code;
		}
		return $"{pair}-{poolAddress.ToUpperInvariant()}";
	}

	private static string NormalizeSymbolOverride(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length > 20 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			throw new FormatException(
				$"Invalid Orca token symbol override '{value}'.");
		return value.ToUpperInvariant();
	}
}
