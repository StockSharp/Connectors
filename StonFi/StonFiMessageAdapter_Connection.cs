namespace StockSharp.StonFi;

public partial class StonFiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _tonClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(ApiEndpoint)
			{
				Parent = this,
			};
			_tonClient = new(TonCenterEndpoint, TonCenterApiKey, Mnemonic,
				WalletAddress, WalletSubwalletId, WalletRevision)
			{
				Parent = this,
			};
			await TonClient.VerifyAsync(cancellationToken);
			var pools = await RestClient.GetPoolsAsync(PoolLimit, Pools,
				cancellationToken);
			var assets = await RestClient.GetAssetsAsync(pools.SelectMany(
				static pool => new[]
				{
					pool.Token0Address,
					pool.Token1Address,
				}), cancellationToken);
			var markets = CreateMarkets(pools, assets);
			var latest = await RestClient.GetLatestBlockAsync(
				cancellationToken);
			if (latest?.Block is null || latest.Block.Number <= 0 ||
				latest.Block.Timestamp <= 0)
				throw new InvalidDataException(
					"STON.fi returned no latest event block.");
			using (_sync.EnterScope())
			{
				foreach (var market in markets)
				{
					_markets.Add(market.SecurityCode, market);
					_marketsByPool.Add(
						market.Pool.Address.NormalizeTonAddress(), market);
				}
				_lastEventBlock = Math.Max(0,
					latest.Block.Number -
						StonFiExtensions.MaximumEventBlockRange);
				_nextMarketPoll = DateTime.UtcNow;
				_nextPrivatePoll = DateTime.UtcNow;
			}
			if (TonClient.IsWalletConfigured)
				WalletAddress = TonClient.WalletAddress;
			connectMsg.SessionId = $"STON.fi TON " +
				(TonClient.IsWalletConfigured
					? TonClient.WalletAddress[2..10]
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
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
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
		_ = timeMsg;
		var pollMarket = false;
		var pollPrivate = false;
		using (_sync.EnterScope())
		{
			var now = DateTime.UtcNow;
			if (_restClient is not null &&
				(_level1Subscriptions.Count > 0 ||
					_tickSubscriptions.Count > 0 ||
					_candleSubscriptions.Count > 0) &&
				now >= _nextMarketPoll)
			{
				_nextMarketPoll = now + PollingInterval;
				pollMarket = true;
			}
			if (_restClient is not null && TonClient.IsWalletConfigured &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_trackedSwaps.Values.Any(static swap =>
						swap.State == OrderStates.Active)) &&
				now >= _nextPrivatePoll)
			{
				_nextPrivatePoll = now + PrivatePollingInterval;
				pollPrivate = true;
			}
		}
		if (pollMarket)
			await PollSafelyAsync(PollMarketAsync, cancellationToken);
		if (pollPrivate)
			await PollSafelyAsync(PollPrivateAsync, cancellationToken);
	}

	private static StonMarket[] CreateMarkets(StonPoolInfo[] pools,
		StonAssetInfo[] assets)
	{
		if (pools is not { Length: > 0 })
			throw new InvalidDataException(
				"STON.fi returned no liquidity pools.");
		var assetMap = assets.ToDictionary(
			static asset => asset.Address.NormalizeTonAddress(),
			StringComparer.OrdinalIgnoreCase);
		var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<StonMarket>();
		foreach (var pool in pools)
		{
			var poolAddress = pool.Address.NormalizeTonAddress();
			var token0 = pool.Token0Address.NormalizeTonAddress();
			var token1 = pool.Token1Address.NormalizeTonAddress();
			if (!assetMap.TryGetValue(token0, out var asset0) ||
				!assetMap.TryGetValue(token1, out var asset1))
				throw new InvalidDataException(
					$"STON.fi pool '{poolAddress}' references an unknown " +
						"asset.");
			_ = asset0.GetDecimals();
			_ = asset1.GetDecimals();
			var reserve0 = pool.Reserve0.ParseInteger("reserve0");
			var reserve1 = pool.Reserve1.ParseInteger("reserve1");
			if (reserve0 < 0 || reserve1 < 0)
				throw new InvalidDataException(
					$"STON.fi pool '{poolAddress}' has negative reserves.");
			var code = StonFiExtensions.CreateSecurityCode(asset0, asset1);
			if (!codes.Add(code))
			{
				var suffix = poolAddress[2..10];
				code += "@" + suffix;
				if (!codes.Add(code))
					throw new InvalidDataException(
						$"STON.fi generated duplicate security code " +
							$"'{code}'.");
			}
			result.Add(new()
			{
				SecurityCode = code,
				Pool = pool,
				Asset0 = asset0,
				Asset1 = asset1,
			});
		}
		if (result.Count == 0)
			throw new InvalidDataException(
				"STON.fi returned no usable liquidity pools.");
		return [.. result];
	}

	private async ValueTask PollSafelyAsync(
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
		_restClient?.Dispose();
		_restClient = null;
		_tonClient?.Dispose();
		_tonClient = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByPool.Clear();
			_level1Subscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_seenMarketData.Clear();
			_deliveryOrder.Clear();
			_level1Fingerprints.Clear();
			_portfolioSubscriptions.Clear();
			_balanceFingerprints.Clear();
			_orderSubscriptions.Clear();
			_orderFingerprints.Clear();
			_trackedSwaps.Clear();
			_lastTrades.Clear();
			_lastEventBlock = 0;
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
		}
	}
}
