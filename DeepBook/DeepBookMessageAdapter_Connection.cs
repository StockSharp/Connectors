namespace StockSharp.DeepBook;

public partial class DeepBookMessageAdapter
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
			_apiClient = new(IndexerEndpoint)
			{
				Parent = this,
			};
			_suiClient = new(GrpcEndpoint, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			WalletAddress = SuiClient.WalletAddress;

			var status = await ApiClient.GetStatusAsync(cancellationToken);
			var service = await SuiClient.GetServiceInfoAsync(cancellationToken);
			ValidateService(service);
			_chainId = service.ChainId.Trim();

			PackageId = PackageId.NormalizeSuiAddress();
			var package = await SuiClient.GetObjectAsync(PackageId,
				cancellationToken);
			if (!package.ObjectType.Equals("package",
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"The configured Sui node does not expose the configured " +
					"DeepBook package.");
			ClockObjectId = ClockObjectId.NormalizeSuiAddress();
			_clock = await SuiClient.GetSharedObjectAsync(ClockObjectId, false,
				cancellationToken);

			var markets = FilterMarkets(
				await ApiClient.GetMarketsAsync(cancellationToken));
			using (_sync.EnterScope())
			{
				foreach (var market in markets)
				{
					_markets.Add(market.SecurityCode, market);
					_marketsByPool.Add(market.PoolId, market);
					_tokens.TryAdd(market.BaseToken.CoinType,
						market.BaseToken);
					_tokens.TryAdd(market.QuoteToken.CoinType,
						market.QuoteToken);
				}
			}
			connectMsg.SessionId = $"DeepBook Sui {_chainId} " +
				$"checkpoint {status.LatestCheckpoint} " +
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
		List<long> expired = [];
		using (_sync.EnterScope())
		{
			foreach (var pair in _tickSubscriptions.Where(pair =>
				pair.Value.To is DateTime end && now >= end).ToArray())
			{
				expired.Add(pair.Key);
				RemoveMarketSubscriptionNoLock(pair.Key);
			}
			foreach (var pair in _candleSubscriptions.Where(pair =>
				pair.Value.To is DateTime end && now >= end).ToArray())
			{
				expired.Add(pair.Key);
				RemoveMarketSubscriptionNoLock(pair.Key);
			}
			if (_apiClient is not null &&
				(_level1Subscriptions.Count > 0 ||
					_depthSubscriptions.Count > 0 ||
					_tickSubscriptions.Count > 0 ||
					_candleSubscriptions.Count > 0) &&
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
		}
		if (pollMarket)
			await RunSafelyAsync(PollMarketDataAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		foreach (var target in expired.Distinct())
			await SendSubscriptionFinishedAsync(target, cancellationToken);
		_ = timeMsg;
	}

	private static void ValidateService(GetServiceInfoResponse service)
	{
		ArgumentNullException.ThrowIfNull(service);
		if (!service.Chain.Equals("mainnet",
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				$"DeepBook connector requires Sui mainnet, but the node reports " +
				$"'{service.Chain}'.");
		if (service.ChainId.IsEmpty() || service.CheckpointHeight == 0 ||
			service.Server.IsEmpty())
			throw new InvalidDataException(
				"Sui gRPC returned incomplete service information.");
	}

	private DeepBookMarket[] FilterMarkets(DeepBookMarket[] markets)
	{
		if (markets is not { Length: > 0 })
			throw new InvalidDataException(
				"DeepBook indexer returned no pools.");
		if (Pools.IsEmpty())
			return markets;
		var filters = Pools.Split([';', ','],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (filters.Length == 0)
			return markets;
		var selected = new List<DeepBookMarket>();
		foreach (var filter in filters)
		{
			var match = markets.FirstOrDefault(market =>
				market.PoolName.Equals(filter,
					StringComparison.OrdinalIgnoreCase) ||
				market.SecurityCode.Equals(filter,
					StringComparison.OrdinalIgnoreCase) ||
				market.PoolId.Equals(filter,
					StringComparison.OrdinalIgnoreCase));
			if (match is null)
				throw new InvalidOperationException(
					$"DeepBook pool '{filter}' was not returned by the indexer.");
			if (!selected.Contains(match))
				selected.Add(match);
		}
		return [.. selected];
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
		_apiClient?.Dispose();
		_apiClient = null;
		_suiClient?.Dispose();
		_suiClient = null;
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
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_seenMarketData.Clear();
			_marketDataDeliveryOrder.Clear();
			_level1Fingerprints.Clear();
			_depthFingerprints.Clear();
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
