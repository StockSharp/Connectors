namespace StockSharp.Cetus;

public partial class CetusMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_apiClient is not null || _suiClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_apiClient = new(RouterEndpoint)
			{
				Parent = this,
			};
			_suiClient = new(GrpcEndpoint, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			WalletAddress = SuiClient.WalletAddress;
			var service = await SuiClient.GetServiceInfoAsync(cancellationToken);
			ValidateService(service);
			_chainId = service.ChainId.Trim();

			var integration = await SuiClient.GetObjectAsync(
				CetusExtensions.IntegrationPackage, cancellationToken);
			if (!integration.ObjectType.Equals("package",
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"The configured Sui node does not expose the current Cetus " +
					"integration package.");
			_globalConfig = await SuiClient.GetSharedObjectAsync(
				CetusExtensions.GlobalConfig, false, cancellationToken);
			_clock = await SuiClient.GetSharedObjectAsync(CetusExtensions.Clock,
				false, cancellationToken);

			var definitions = ParseMarketDefinitions();
			var errors = new List<Exception>();
			foreach (var definition in definitions)
			{
				try
				{
					await RegisterMarketAsync(definition, cancellationToken);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog("Cetus pool {0} loading failed: {1}",
						definition.PoolId, error.Message);
				}
			}
			using (_sync.EnterScope())
				if (_markets.Count == 0)
					throw errors.Count == 1
						? errors[0]
						: new AggregateException(
							"No Cetus pools could be loaded.", errors);

			await TryConnectCheckpointStreamAsync(cancellationToken);
			connectMsg.SessionId = $"Cetus Sui {_chainId} " +
				(SuiClient.IsWalletAvailable
					? SuiClient.WalletAddress[2..10]
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
		var now = DateTime.UtcNow;
		var pollMarket = false;
		var pollPrivate = false;
		var reconnectStream = false;
		long[] expiredTicks;
		using (_sync.EnterScope())
		{
			expiredTicks = [.. _tickSubscriptions.Where(pair =>
				pair.Value.To is DateTime end && now >= end)
				.Select(static pair => pair.Key)];
			foreach (var target in expiredTicks)
				UnsubscribeTicksNoLock(target);
			if (_apiClient is not null && _level1Subscriptions.Count > 0 &&
				now >= _nextMarketPoll)
			{
				_nextMarketPoll = now + PollingInterval;
				pollMarket = true;
			}
			if (_suiClient is not null && SuiClient.IsWalletAvailable &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0) &&
				now >= _nextPrivatePoll)
			{
				_nextPrivatePoll = now + PollingInterval;
				pollPrivate = true;
			}
			reconnectStream = _suiClient is not null &&
				(_checkpointClient is null || !_checkpointClient.IsConnected) &&
				now >= _nextStreamReconnect;
			if (reconnectStream)
				_nextStreamReconnect = now + TimeSpan.FromSeconds(15);
		}
		if (reconnectStream)
			await RunSafelyAsync(TryConnectCheckpointStreamAsync,
				cancellationToken);
		if (pollMarket)
			await RunSafelyAsync(PollLevel1Async, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		foreach (var target in expiredTicks)
			await SendSubscriptionFinishedAsync(target, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private static void ValidateService(GetServiceInfoResponse service)
	{
		ArgumentNullException.ThrowIfNull(service);
		if (!service.Chain.Equals("mainnet",
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				$"Cetus connector requires Sui mainnet, but the node reports " +
				$"'{service.Chain}'.");
		if (service.ChainId.IsEmpty() || service.CheckpointHeight == 0 ||
			service.Server.IsEmpty())
			throw new InvalidDataException(
				"Sui gRPC returned incomplete service information.");
	}

	private CetusMarketDefinition[] ParseMarketDefinitions()
	{
		if (Pools.IsEmpty())
			throw new InvalidOperationException(
				"At least one Cetus pool must be configured.");
		var result = new List<CetusMarketDefinition>();
		var pools = new HashSet<string>(StringComparer.Ordinal);
		foreach (var entry in Pools.Split(';',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var fields = entry.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not (3 or 4))
				throw new FormatException(
					"Cetus pool entries must use " +
					"pool|base-coin-type|quote-coin-type|security-code.");
			var poolId = fields[0].NormalizeSuiAddress();
			if (!pools.Add(poolId))
				throw new FormatException(
					$"Duplicate Cetus pool '{poolId}'.");
			var baseCoin = fields[1].NormalizeCoinType();
			var quoteCoin = fields[2].NormalizeCoinType();
			if (baseCoin == quoteCoin)
				throw new FormatException(
					$"Cetus pool '{poolId}' has identical coin types.");
			result.Add(new()
			{
				PoolId = poolId,
				BaseCoinType = baseCoin,
				QuoteCoinType = quoteCoin,
				SecurityCode = fields.Length == 4 && !fields[3].IsEmpty()
					? fields[3].NormalizeSecurityCode()
					: null,
			});
		}
		if (result.Count == 0)
			throw new InvalidOperationException(
				"At least one Cetus pool must be configured.");
		return [.. result];
	}

	private async ValueTask RegisterMarketAsync(
		CetusMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var pool = await SuiClient.GetObjectAsync(definition.PoolId,
			cancellationToken);
		if (pool.Owner?.Kind != Owner.Types.OwnerKind.Shared ||
			!pool.Owner.HasVersion || pool.Owner.Version == 0)
			throw new InvalidDataException(
				$"Cetus pool '{definition.PoolId}' is not a shared Sui object.");
		var types = pool.ObjectType.ParsePoolCoinTypes();
		var isDirect = types.CoinA == definition.BaseCoinType &&
			types.CoinB == definition.QuoteCoinType;
		var isReverse = types.CoinB == definition.BaseCoinType &&
			types.CoinA == definition.QuoteCoinType;
		if (!isDirect && !isReverse)
			throw new InvalidDataException(
				$"Cetus pool '{definition.PoolId}' coin types do not match " +
				"the configured market.");
		var coinA = await GetOrLoadTokenAsync(types.CoinA, cancellationToken);
		var coinB = await GetOrLoadTokenAsync(types.CoinB, cancellationToken);
		var market = new CetusMarket
		{
			PoolId = definition.PoolId,
			PoolInitialVersion = pool.Owner.Version,
			CoinA = coinA,
			CoinB = coinB,
			BaseToken = isDirect ? coinA : coinB,
			QuoteToken = isDirect ? coinB : coinA,
			SecurityCode = definition.SecurityCode ??
				$"{(isDirect ? coinA : coinB).Symbol}-" +
				$"{(isDirect ? coinB : coinA).Symbol}",
		};
		market.SecurityCode = market.SecurityCode.NormalizeSecurityCode();
		using (_sync.EnterScope())
		{
			if (_markets.ContainsKey(market.SecurityCode))
			{
				if (!definition.SecurityCode.IsEmpty())
					throw new InvalidDataException(
						$"Duplicate Cetus security code " +
						$"'{market.SecurityCode}'.");
				market.SecurityCode = BuildUniqueCode(market.SecurityCode,
					market.PoolId);
			}
			_markets.Add(market.SecurityCode, market);
			_marketsByPool.Add(market.PoolId, market);
		}

		try
		{
			var probeAmount = 1m.ToBaseUnits(market.BaseToken.Decimals);
			if (probeAmount == 0)
				throw new InvalidOperationException(
					"Cetus validation probe rounded to zero.");
			_ = await ApiClient.GetExactInputQuoteAsync(market,
				market.BaseToken.CoinType, market.QuoteToken.CoinType,
				probeAmount, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_markets.Remove(market.SecurityCode);
				_marketsByPool.Remove(market.PoolId);
			}
			throw;
		}
	}

	private async ValueTask<CetusToken> GetOrLoadTokenAsync(string coinType,
		CancellationToken cancellationToken)
	{
		coinType = coinType.NormalizeCoinType();
		using (_sync.EnterScope())
			if (_tokens.TryGetValue(coinType, out var cached))
				return cached;
		var token = await SuiClient.GetTokenAsync(coinType, cancellationToken);
		using (_sync.EnterScope())
		{
			if (_tokens.TryGetValue(coinType, out var cached))
				return cached;
			_tokens.Add(coinType, token);
			return token;
		}
	}

	private string BuildUniqueCode(string code, string poolId)
	{
		var suffix = poolId[2..].ToUpperInvariant();
		for (var length = 6; length <= 20; length += 2)
		{
			var candidate = $"{code}-{suffix[..length]}";
			if (!_markets.ContainsKey(candidate))
				return candidate;
		}
		throw new InvalidDataException(
			$"Cannot create a unique Cetus code for pool '{poolId}'.");
	}

	private async ValueTask TryConnectCheckpointStreamAsync(
		CancellationToken cancellationToken)
	{
		CetusCheckpointClient previous;
		using (_sync.EnterScope())
		{
			if (_checkpointClient?.IsConnected == true)
				return;
			previous = _checkpointClient;
			_checkpointClient = null;
		}
		previous?.Dispose();
		var client = SuiClient.CreateCheckpointClient(OnCheckpointSwapAsync,
			OnCheckpointErrorAsync);
		try
		{
			await client.ConnectAsync(cancellationToken);
			using (_sync.EnterScope())
				_checkpointClient = client;
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			client.Dispose();
			using (_sync.EnterScope())
				_nextStreamReconnect = DateTime.UtcNow +
					TimeSpan.FromSeconds(15);
			this.AddWarningLog(
				"Cetus checkpoint stream unavailable; Level1 remains active: {0}",
				error.Message);
		}
	}

	private async ValueTask OnCheckpointSwapAsync(CetusSwapEvent swap)
	{
		CetusMarket market;
		(long Id, TickSubscription Subscription)[] targets;
		using (_sync.EnterScope())
		{
			if (!_marketsByPool.TryGetValue(swap.PoolId, out market))
				return;
			targets = [.. _tickSubscriptions.Where(pair =>
				ReferenceEquals(pair.Value.Market, market)).Select(static pair =>
					(pair.Key, pair.Value))];
		}
		if (targets.Length == 0)
			return;
		var execution = ReadSwapExecution(market, swap);
		var identity = $"{swap.TransactionDigest}:{swap.EventIndex}";
		foreach (var target in targets)
		{
			if (target.Subscription.To is DateTime end && swap.Time > end)
				continue;
			if (!await SendTradeAsync(market, target.Id, identity, swap.Time,
				execution.Price, execution.Volume, execution.Side,
				CancellationToken.None))
				continue;
			var isFinished = false;
			using (_sync.EnterScope())
			{
				if (_tickSubscriptions.TryGetValue(target.Id, out var active))
				{
					active.Delivered++;
					isFinished = active.Delivered >= active.Maximum;
					if (isFinished)
						UnsubscribeTicksNoLock(target.Id);
				}
			}
			if (isFinished)
				await SendSubscriptionFinishedAsync(target.Id,
					CancellationToken.None);
		}
	}

	private async ValueTask OnCheckpointErrorAsync(Exception error)
	{
		CetusCheckpointClient client;
		using (_sync.EnterScope())
		{
			client = _checkpointClient;
			_checkpointClient = null;
			_nextStreamReconnect = DateTime.UtcNow + TimeSpan.FromSeconds(15);
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
		CetusCheckpointClient checkpointClient;
		using (_sync.EnterScope())
		{
			checkpointClient = _checkpointClient;
			_checkpointClient = null;
		}
		checkpointClient?.Dispose();
		_apiClient?.Dispose();
		_apiClient = null;
		_suiClient?.Dispose();
		_suiClient = null;
		_globalConfig = null;
		_clock = null;
		_chainId = null;
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
			_seenTrades.Clear();
			_tradeDeliveryOrder.Clear();
			_level1Fingerprints.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedSwaps.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
			_nextStreamReconnect = default;
		}
	}
}
