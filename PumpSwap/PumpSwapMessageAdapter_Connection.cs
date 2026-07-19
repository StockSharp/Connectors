namespace StockSharp.PumpSwap;

public partial class PumpSwapMessageAdapter
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
			_rpcClient = new(RpcEndpoint, Cluster, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await RpcClient.VerifyAsync(cancellationToken);
			WalletAddress = RpcClient.WalletAddress;
			await LoadConfigAsync(cancellationToken);
			using (_sync.EnterScope())
				_nextConfigPoll = CurrentTime + TimeSpan.FromMinutes(1);

			var definitions = ParseMarketDefinitions();
			if (definitions.Length == 0)
				throw new InvalidOperationException(
					"At least one PumpSwap pool address must be configured.");
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
					this.AddWarningLog(
						"PumpSwap pool {0} loading failed: {1}",
						definition.PoolAddress, error.Message);
				}
			}
			using (_sync.EnterScope())
				if (_markets.Count == 0)
					throw errors.Count == 1
						? errors[0]
						: new AggregateException(
							"No configured PumpSwap pools could be loaded.",
							errors);

			await TryConnectSocketAsync(cancellationToken);
			connectMsg.SessionId = $"PumpSwap {Cluster} " +
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
		var refreshConfig = false;
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
			if (_rpcClient is not null && CurrentTime >= _nextConfigPoll)
			{
				_nextConfigPoll = CurrentTime + TimeSpan.FromMinutes(1);
				refreshConfig = true;
			}
			reconnectSocket = _rpcClient is not null &&
				(_socketClient is null || !_socketClient.IsConnected) &&
				CurrentTime >= _nextSocketReconnect;
			if (reconnectSocket)
				_nextSocketReconnect = CurrentTime + TimeSpan.FromSeconds(15);
		}
		if (refreshConfig)
			await RunSafelyAsync(LoadConfigAsync, cancellationToken);
		if (reconnectSocket)
			await RunSafelyAsync(TryConnectSocketAsync, cancellationToken);
		if (pollMarket)
			await RunSafelyAsync(PollMarketAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask LoadConfigAsync(
		CancellationToken cancellationToken)
	{
		var accounts = await RpcClient.GetAccountsAsync(
		[
			PumpSwapExtensions.GlobalConfigAddress,
			PumpSwapExtensions.FeeConfigAddress(),
		], cancellationToken);
		if (accounts.Length != 2 || accounts[0] is null)
			throw new InvalidDataException(
				"PumpSwap global configuration account was not found.");
		var globalConfig = PumpSwapExtensions.DecodeGlobalConfig(accounts[0]);
		var feeConfig = accounts[1] is null
			? null
			: PumpSwapExtensions.DecodeFeeConfig(accounts[1]);
		using (_sync.EnterScope())
		{
			_globalConfig = globalConfig;
			_feeConfig = feeConfig;
		}
	}

	private async ValueTask<PumpSwapMarket> LoadMarketAsync(
		PumpSwapMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var poolAccount = await RpcClient.GetAccountAsync(
			definition.PoolAddress, cancellationToken);
		if (poolAccount is null || !poolAccount.Owner.Equals(
			PumpSwapExtensions.ProgramAddress, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Account '{definition.PoolAddress}' is not a PumpSwap pool.");
		var market = PumpSwapExtensions.DecodePool(definition.PoolAddress,
			poolAccount);
		var accounts = await RpcClient.GetAccountsAsync(
		[
			market.BaseToken.Mint,
			market.QuoteToken.Mint,
			market.PoolBaseTokenAccount,
			market.PoolQuoteTokenAccount,
			PumpSwapExtensions.MetadataAddress(market.BaseToken.Mint),
			PumpSwapExtensions.MetadataAddress(market.QuoteToken.Mint),
		], cancellationToken);
		if (accounts.Length != 6 || accounts[0] is null ||
			accounts[1] is null || accounts[2] is null || accounts[3] is null)
			throw new InvalidDataException(
				$"PumpSwap pool '{definition.PoolAddress}' has missing mint or " +
				"reserve accounts.");
		var baseMetadata = PumpSwapExtensions.DecodeMetadata(accounts[4],
			market.BaseToken.Mint);
		var quoteMetadata = PumpSwapExtensions.DecodeMetadata(accounts[5],
			market.QuoteToken.Mint);
		market.BaseToken = PumpSwapExtensions.DecodeMint(market.BaseToken.Mint,
			accounts[0], definition.BaseSymbol ?? baseMetadata.Symbol,
			baseMetadata.Name);
		market.QuoteToken = PumpSwapExtensions.DecodeMint(
			market.QuoteToken.Mint, accounts[1],
			definition.QuoteSymbol ?? quoteMetadata.Symbol,
			quoteMetadata.Name);
		market.BaseReserves = PumpSwapExtensions.DecodeTokenAmount(accounts[2],
			market.BaseToken.Mint);
		market.QuoteReserves = PumpSwapExtensions.DecodeTokenAmount(accounts[3],
			market.QuoteToken.Mint);
		if (market.BaseReserves == 0 || market.QuoteReserves == 0)
			throw new InvalidDataException(
				$"PumpSwap pool '{definition.PoolAddress}' has no liquidity.");
		return market;
	}

	private void RegisterMarket(PumpSwapMarket market)
	{
		var pair = $"{market.BaseToken.Symbol}-{market.QuoteToken.Symbol}"
			.ToUpperInvariant();
		using (_sync.EnterScope())
		{
			if (_marketsByPool.ContainsKey(market.PoolAddress))
				return;
			var matches = _marketsByPool.Values.Where(existing =>
				existing.BaseToken.Symbol.Equals(market.BaseToken.Symbol,
					StringComparison.OrdinalIgnoreCase) &&
				existing.QuoteToken.Symbol.Equals(market.QuoteToken.Symbol,
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
					$"Duplicate PumpSwap security code '{market.SecurityCode}'.");
			_markets.Add(market.SecurityCode, market);
			_marketsByPool.Add(market.PoolAddress, market);
			_tokens[market.BaseToken.Mint] = market.BaseToken;
			_tokens[market.QuoteToken.Mint] = market.QuoteToken;
		}
	}

	private PumpSwapMarketDefinition[] ParseMarketDefinitions()
	{
		if (Pools.IsEmpty() || Cluster != PumpSwapClusters.Mainnet &&
			Pools.EqualsIgnoreCase(_defaultPools))
			return [];
		var result = new List<PumpSwapMarketDefinition>();
		foreach (var item in Pools.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not (1 or 3))
				throw new FormatException(
					"Each PumpSwap market must use pool or " +
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
		PumpSwapSocketClient previous;
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
		var client = new PumpSwapSocketClient(StreamingEndpoint,
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
				"PumpSwap WebSocket unavailable; polling remains active: {0}",
				error.Message);
		}
	}

	private ValueTask OnSocketLogsAsync(string signature, string[] logs)
		=> ProcessRealtimeEventsAsync(signature, logs);

	private async ValueTask OnSocketErrorAsync(Exception error)
	{
		PumpSwapSocketClient client;
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
		PumpSwapSocketClient socketClient;
		using (_sync.EnterScope())
		{
			socketClient = _socketClient;
			_socketClient = null;
		}
		socketClient?.Dispose();
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
			_globalConfig = null;
			_feeConfig = null;
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
			_nextConfigPoll = default;
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
				$"Invalid PumpSwap token symbol override '{value}'.");
		return value.ToUpperInvariant();
	}
}
