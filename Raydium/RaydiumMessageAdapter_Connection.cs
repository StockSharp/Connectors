namespace StockSharp.Raydium;

public partial class RaydiumMessageAdapter
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
				? Cluster.GetApiEndpoint()
				: ApiEndpoint.Trim();
			TradeEndpoint = TradeEndpoint.IsEmpty()
				? Cluster.GetTradeEndpoint()
				: TradeEndpoint.Trim();
			_rpcClient = new(RpcEndpoint, Cluster, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await RpcClient.VerifyAsync(cancellationToken);
			WalletAddress = RpcClient.WalletAddress;
			_apiClient = new(ApiEndpoint, TradeEndpoint) { Parent = this };

			var definitions = await LoadMarketDefinitionsAsync(
				cancellationToken);
			if (definitions.Length == 0)
				throw new InvalidOperationException(
					"At least one Raydium pool must be configured or discovered.");
			var pools = new List<RaydiumPool>(definitions.Length);
			var errors = new List<Exception>();
			foreach (var definition in definitions)
			{
				try
				{
					pools.Add(await LoadPoolAsync(definition,
						cancellationToken));
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog("Raydium pool {0} loading failed: {1}",
						definition.PoolAddress, error.Message);
				}
			}
			if (pools.Count == 0)
				throw errors.Count == 1
					? errors[0]
					: new AggregateException(
						"No Raydium pools could be loaded.", errors);
			await RpcClient.VerifyProgramsAsync(pools.Select(static pool =>
				pool.ProgramAddress), cancellationToken);
			RegisterMarkets(pools);

			await TryConnectSocketAsync(cancellationToken);
			connectMsg.SessionId = $"Raydium {Cluster} " +
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
					_depthSubscriptions.Count > 0 ||
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

	private async ValueTask<RaydiumMarketDefinition[]>
		LoadMarketDefinitionsAsync(CancellationToken cancellationToken)
	{
		var explicitDefinitions = ParseMarketDefinitions();
		var explicitByPool = explicitDefinitions.ToDictionary(
			static definition => definition.PoolAddress,
			StringComparer.Ordinal);
		var pools = new Dictionary<string, RaydiumApiPool>(
			StringComparer.Ordinal);
		if (MaximumDiscoveredPools > 0)
			foreach (var pool in await _apiClient.GetPoolsAsync(
				MaximumDiscoveredPools, cancellationToken))
				pools[pool.Id.NormalizePublicKey()] = pool;

		var missing = explicitDefinitions.Where(definition =>
			!pools.ContainsKey(definition.PoolAddress)).Select(
				static definition => definition.PoolAddress).ToArray();
		for (var offset = 0; offset < missing.Length; offset += 100)
			foreach (var pool in await _apiClient.GetPoolsByIdsAsync(
				missing.Skip(offset).Take(100), cancellationToken))
				pools[pool.Id.NormalizePublicKey()] = pool;
		var unresolvedPools = explicitDefinitions.Where(definition =>
			!pools.ContainsKey(definition.PoolAddress)).Select(static definition =>
				definition.PoolAddress).ToArray();
		if (unresolvedPools.Length > 0)
			throw new InvalidDataException(
				"Raydium API returned no metadata for configured pools: " +
				string.Join(", ", unresolvedPools));

		var keys = new Dictionary<string, RaydiumApiPoolKeys>(
			StringComparer.Ordinal);
		var addresses = pools.Keys.ToArray();
		for (var offset = 0; offset < addresses.Length; offset += 100)
			foreach (var item in await _apiClient.GetPoolKeysAsync(
				addresses.Skip(offset).Take(100), cancellationToken))
				keys[item.Id.NormalizePublicKey()] = item;
		var unresolvedKeys = explicitDefinitions.Where(definition =>
			!keys.ContainsKey(definition.PoolAddress)).Select(static definition =>
				definition.PoolAddress).ToArray();
		if (unresolvedKeys.Length > 0)
			throw new InvalidDataException(
				"Raydium API returned no key metadata for configured pools: " +
				string.Join(", ", unresolvedKeys));

		return [.. pools.Values.Select(pool =>
		{
			var address = pool.Id.NormalizePublicKey();
			explicitByPool.TryGetValue(address, out var configured);
			return keys.TryGetValue(address, out var poolKeys)
				? new RaydiumMarketDefinition
				{
					PoolAddress = address,
					BaseSymbol = configured?.BaseSymbol,
					QuoteSymbol = configured?.QuoteSymbol,
					ApiPool = pool,
					ApiKeys = poolKeys,
				}
				: null;
		}).Where(static definition => definition is not null)];
	}

	private async ValueTask<RaydiumPool> LoadPoolAsync(
		RaydiumMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var info = definition.ApiPool ?? throw new InvalidDataException(
			$"Raydium pool '{definition.PoolAddress}' has no API metadata.");
		var keys = definition.ApiKeys ?? throw new InvalidDataException(
			$"Raydium pool '{definition.PoolAddress}' has no key metadata.");
		var address = definition.PoolAddress.NormalizePublicKey();
		var program = keys.ProgramId.NormalizePublicKey();
		if (!info.Id.NormalizePublicKey().Equals(address,
				StringComparison.Ordinal) ||
			!keys.Id.NormalizePublicKey().Equals(address,
				StringComparison.Ordinal) ||
			!info.ProgramId.NormalizePublicKey().Equals(program,
				StringComparison.Ordinal) ||
			!info.MintA.Address.NormalizePublicKey().Equals(
				keys.MintA.Address.NormalizePublicKey(), StringComparison.Ordinal) ||
			!info.MintB.Address.NormalizePublicKey().Equals(
				keys.MintB.Address.NormalizePublicKey(), StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Raydium API metadata for pool '{address}' is inconsistent.");
		var tokenA = CreateToken(keys.MintA, definition.BaseSymbol);
		var tokenB = CreateToken(keys.MintB, definition.QuoteSymbol);
		var vaultA = keys.Vault.A.NormalizePublicKey();
		var vaultB = keys.Vault.B.NormalizePublicKey();
		var accounts = await RpcClient.GetAccountsAsync(
			[address, tokenA.Mint, tokenB.Mint, vaultA, vaultB],
			cancellationToken);
		if (accounts.Length != 5 || accounts.Any(static account =>
			account is null))
			throw new InvalidDataException(
				$"Raydium pool '{address}' has missing on-chain accounts.");
		if (!accounts[0].Owner.Equals(program, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Pool '{address}' is not owned by Raydium program '{program}'.");
		RaydiumExtensions.ValidateMintAccount(accounts[1], tokenA);
		RaydiumExtensions.ValidateMintAccount(accounts[2], tokenB);
		RaydiumExtensions.ValidateVaultAccount(accounts[3], tokenA, vaultA);
		RaydiumExtensions.ValidateVaultAccount(accounts[4], tokenB, vaultB);
		return new()
		{
			PoolAddress = address,
			ProgramAddress = program,
			VaultA = vaultA,
			VaultB = vaultB,
			TokenA = tokenA,
			TokenB = tokenB,
			ReferencePrice = info.Price ?? 0,
			TotalValueLocked = info.TotalValueLocked ?? 0,
		};
	}

	private void RegisterMarkets(IEnumerable<RaydiumPool> pools)
	{
		var markets = pools.GroupBy(pool => RaydiumExtensions.GetMintPairKey(
			pool.TokenA.Mint, pool.TokenB.Mint), StringComparer.Ordinal)
			.Select(group =>
			{
				var ordered = group.OrderByDescending(static pool =>
					pool.TotalValueLocked).ThenBy(static pool => pool.PoolAddress,
						StringComparer.Ordinal).ToArray();
				var reference = ordered[0];
				return new RaydiumMarket
				{
					TokenA = reference.TokenA,
					TokenB = reference.TokenB,
					Pools = ordered,
					ReferencePrice = reference.ReferencePrice,
					SecurityCode = $"{reference.TokenA.Symbol}-" +
						$"{reference.TokenB.Symbol}".ToUpperInvariant(),
				};
			}).ToArray();
		foreach (var collision in markets.GroupBy(static market =>
			market.SecurityCode, StringComparer.OrdinalIgnoreCase).Where(
			static group => group.Count() > 1))
			foreach (var market in collision)
				market.SecurityCode += "-" +
					market.TokenA.Mint[..6].ToUpperInvariant();

		using (_sync.EnterScope())
		{
			foreach (var market in markets)
			{
				if (!_markets.TryAdd(market.SecurityCode, market))
					throw new InvalidDataException(
						$"Duplicate Raydium security code " +
						$"'{market.SecurityCode}'.");
				foreach (var pool in market.Pools)
				{
					if (!_marketsByPool.TryAdd(pool.PoolAddress, market))
						throw new InvalidDataException(
							$"Duplicate Raydium pool '{pool.PoolAddress}'.");
					_tokens[pool.TokenA.Mint] = pool.TokenA;
					_tokens[pool.TokenB.Mint] = pool.TokenB;
				}
			}
		}
	}

	private RaydiumMarketDefinition[] ParseMarketDefinitions()
	{
		if (Pools.IsEmpty())
			return [];
		var result = new List<RaydiumMarketDefinition>();
		foreach (var item in Pools.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not (1 or 3))
				throw new FormatException(
					"Each Raydium market must use pool or " +
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
		RaydiumSocketClient previous;
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
		var client = new RaydiumSocketClient(StreamingEndpoint,
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
				"Raydium WebSocket unavailable; polling remains active: {0}",
				error.Message);
		}
	}

	private ValueTask OnSocketLogsAsync(string signature, string[] logs)
	{
		_ = logs;
		return ProcessRealtimeTransactionAsync(signature);
	}

	private async ValueTask OnSocketErrorAsync(Exception error)
	{
		RaydiumSocketClient client;
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
		RaydiumSocketClient socketClient;
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
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_seenTrades.Clear();
			_tradeDeliveryOrder.Clear();
			_seenRealtimeSignatures.Clear();
			_realtimeSignatureOrder.Clear();
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

	private static RaydiumToken CreateToken(RaydiumApiMint mint,
		string symbolOverride)
	{
		ArgumentNullException.ThrowIfNull(mint);
		var address = mint.Address.NormalizePublicKey();
		var program = mint.ProgramId.NormalizePublicKey();
		if (program is not (RaydiumExtensions.TokenProgramAddress or
			RaydiumExtensions.Token2022ProgramAddress))
			throw new InvalidDataException(
				$"Mint '{address}' uses unsupported token program '{program}'.");
		if (mint.Decimals is < 0 or > 28)
			throw new InvalidDataException(
				$"Mint '{address}' has unsupported decimals '{mint.Decimals}'.");
		var symbol = symbolOverride ?? NormalizeApiSymbol(mint.Symbol, address);
		return new()
		{
			Mint = address,
			TokenProgram = program,
			Decimals = mint.Decimals,
			Symbol = symbol,
			Name = mint.Name.IsEmpty() ? symbol : mint.Name.Trim(),
		};
	}

	private static string NormalizeApiSymbol(string value, string mint)
	{
		value = value?.Trim();
		if (value.IsEmpty() || value.Length > 20 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			return mint[..8].ToUpperInvariant();
		return value.ToUpperInvariant();
	}

	private static string NormalizeSymbolOverride(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length > 20 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			throw new FormatException(
				$"Invalid Raydium token symbol override '{value}'.");
		return value.ToUpperInvariant();
	}
}
