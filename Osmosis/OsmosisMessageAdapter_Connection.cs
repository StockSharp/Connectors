namespace StockSharp.Osmosis;

public partial class OsmosisMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_apiClient is not null || _signer is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(WalletAddress, PrivateKey);
			WalletAddress = Signer.WalletAddress;
			_apiClient = new(SqsEndpoint, LcdEndpoint, RpcEndpoint,
				AssetListEndpoint)
			{
				Parent = this,
			};
			var status = await ApiClient.GetStatusAsync(cancellationToken);
			ValidateStatus(status);
			_chainId = status.Result.NodeInfo.Network.Trim();
			var health = await ApiClient.GetHealthAsync(cancellationToken);
			ValidateHealth(health);
			var assetList = await ApiClient.GetAssetListAsync(cancellationToken);
			RegisterMarkets(assetList, ParseMarketDefinitions());
			await ValidateMarketsAsync(cancellationToken);
			await TryConnectSocketAsync(cancellationToken);
			connectMsg.SessionId = $"Osmosis {_chainId} " +
				(Signer.IsWalletAvailable
					? Signer.WalletAddress[..8]
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
		long[] expiredTicks;
		using (_sync.EnterScope())
		{
			expiredTicks = [.. _tickSubscriptions.Where(pair =>
				pair.Value.To is DateTime end && CurrentTime >= end)
				.Select(static pair => pair.Key)];
			foreach (var target in expiredTicks)
				UnsubscribeTicksNoLock(target);
			if (_apiClient is not null && _level1Subscriptions.Count > 0 &&
				CurrentTime >= _nextMarketPoll)
			{
				_nextMarketPoll = CurrentTime + PollingInterval;
				pollMarket = true;
			}
			if (_apiClient is not null && _signer?.IsWalletAvailable == true &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_trackedSwaps.Values.Any(static swap =>
						swap.State == OrderStates.Active)) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				pollPrivate = true;
			}
			reconnectSocket = _apiClient is not null &&
				(_socketClient is null || !_socketClient.IsConnected) &&
				CurrentTime >= _nextSocketReconnect;
			if (reconnectSocket)
				_nextSocketReconnect = CurrentTime + TimeSpan.FromSeconds(15);
		}
		if (reconnectSocket)
			await RunSafelyAsync(TryConnectSocketAsync, cancellationToken);
		if (pollMarket)
			await RunSafelyAsync(PollLevel1Async, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		foreach (var target in expiredTicks)
			await SendSubscriptionFinishedAsync(target, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private static void ValidateStatus(
		OsmosisRpcResponse<OsmosisStatusResult> response)
	{
		ArgumentNullException.ThrowIfNull(response);
		if (response.Error is not null)
			throw new InvalidDataException(
				$"Osmosis RPC status failed ({response.Error.Code}): " +
				response.Error.Message);
		var status = response.Result ?? throw new InvalidDataException(
			"Osmosis RPC returned no status.");
		if (status.NodeInfo?.Network != OsmosisExtensions.ChainId)
			throw new InvalidDataException(
				$"Expected Osmosis chain '{OsmosisExtensions.ChainId}', got " +
				$"'{status.NodeInfo?.Network}'.");
		if (status.SyncInfo is null || status.SyncInfo.IsCatchingUp)
			throw new InvalidOperationException(
				"The Osmosis RPC node is still catching up.");
		_ = status.SyncInfo.LatestBlockHeight.ParseUnsigned(
			"latest block height");
		_ = status.SyncInfo.LatestBlockTime.ParseUtcTime(
			"latest block time");
	}

	private static void ValidateHealth(OsmosisHealthResponse health)
	{
		ArgumentNullException.ThrowIfNull(health);
		if (!string.Equals(health.GrpcGatewayStatus, "running",
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				$"Osmosis SQS gRPC gateway status is " +
				$"'{health.GrpcGatewayStatus}'.");
		var chainHeight = health.ChainLatestHeight.ParseUnsigned(
			"SQS chain height");
		var storeHeight = health.StoreLatestHeight.ParseUnsigned(
			"SQS store height");
		if (storeHeight > chainHeight || chainHeight - storeHeight > 10)
			throw new InvalidOperationException(
				"Osmosis SQS data store is not synchronized with the chain.");
	}

	private OsmosisMarketDefinition[] ParseMarketDefinitions()
	{
		if (Markets.IsEmpty())
			throw new InvalidOperationException(
				"At least one Osmosis market must be configured.");
		var result = new List<OsmosisMarketDefinition>();
		var pairs = new HashSet<string>(StringComparer.Ordinal);
		var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in Markets.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not (2 or 3))
				throw new FormatException(
					"Each Osmosis market must use base-denom|quote-denom or " +
					"base-denom|quote-denom|security-code format.");
			var baseDenomination = fields[0].NormalizeDenomination();
			var quoteDenomination = fields[1].NormalizeDenomination();
			if (baseDenomination.Equals(quoteDenomination,
				StringComparison.Ordinal))
				throw new FormatException(
					"An Osmosis market cannot contain the same token twice.");
			if (!pairs.Add(PairKey(baseDenomination, quoteDenomination)))
				throw new FormatException(
					"Duplicate Osmosis market denomination pair.");
			var code = fields.Length == 3
				? fields[2].NormalizeSecurityCode()
				: null;
			if (!code.IsEmpty() && !codes.Add(code))
				throw new FormatException(
					$"Duplicate Osmosis security code '{code}'.");
			result.Add(new()
			{
				BaseDenomination = baseDenomination,
				QuoteDenomination = quoteDenomination,
				SecurityCode = code,
			});
		}
		if (result.Count == 0)
			throw new InvalidOperationException(
				"At least one Osmosis market must be configured.");
		return [.. result];
	}

	private void RegisterMarkets(OsmosisAssetList assetList,
		OsmosisMarketDefinition[] definitions)
	{
		ArgumentNullException.ThrowIfNull(assetList);
		if (!assetList.ChainName.Equals("osmosis",
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				$"The asset list belongs to '{assetList.ChainName}', not Osmosis.");
		var assets = new Dictionary<string, OsmosisToken>(
			StringComparer.Ordinal);
		foreach (var asset in assetList.Assets ?? [])
		{
			if (asset is null || asset.IsDisabled || asset.IsPreview ||
				asset.Denomination.IsEmpty() || asset.Symbol.IsEmpty())
				continue;
			if (asset.Decimals is < 0 or > 28)
				throw new InvalidDataException(
					$"Osmosis asset '{asset.Symbol}' has unsupported decimals.");
			var token = new OsmosisToken
			{
				Denomination = asset.Denomination.NormalizeDenomination(),
				Symbol = asset.Symbol.NormalizeSymbol(),
				Name = asset.Name?.Trim(),
				Decimals = asset.Decimals,
			};
			if (!assets.TryAdd(token.Denomination, token))
				throw new InvalidDataException(
					$"The Osmosis asset list duplicates '{token.Denomination}'.");
		}

		foreach (var definition in definitions)
		{
			if (!assets.TryGetValue(definition.BaseDenomination,
				out var baseToken))
				throw new InvalidDataException(
					$"Osmosis asset list does not contain base denomination " +
					$"'{definition.BaseDenomination}'.");
			if (!assets.TryGetValue(definition.QuoteDenomination,
				out var quoteToken))
				throw new InvalidDataException(
					$"Osmosis asset list does not contain quote denomination " +
					$"'{definition.QuoteDenomination}'.");
			var code = definition.SecurityCode ??
				$"{baseToken.Symbol}-{quoteToken.Symbol}";
			code = code.NormalizeSecurityCode();
			using (_sync.EnterScope())
			{
				if (_markets.ContainsKey(code))
					code = BuildUniqueCode(code, definition.BaseDenomination,
						definition.QuoteDenomination);
				var market = new OsmosisMarket
				{
					BaseToken = baseToken,
					QuoteToken = quoteToken,
					SecurityCode = code,
				};
				_markets.Add(code, market);
				_marketsByPair.Add(PairKey(baseToken.Denomination,
					quoteToken.Denomination), market);
				_tokens[baseToken.Denomination] = baseToken;
				_tokens[quoteToken.Denomination] = quoteToken;
			}
		}
	}

	private async ValueTask ValidateMarketsAsync(
		CancellationToken cancellationToken)
	{
		OsmosisMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		foreach (var market in markets)
		{
			var amount = ProbeVolume.ToBaseUnits(market.BaseToken.Decimals);
			_ = await ApiClient.GetExactInputQuoteAsync(
				market.BaseToken.Denomination,
				market.QuoteToken.Denomination, amount, cancellationToken);
		}
	}

	private string BuildUniqueCode(string code, string baseDenomination,
		string quoteDenomination)
	{
		var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
			PairKey(baseDenomination, quoteDenomination))));
		for (var length = 6; length <= 20; length += 2)
		{
			var candidate = $"{code}-{hash[..length]}";
			if (!_markets.ContainsKey(candidate))
				return candidate;
		}
		throw new InvalidDataException(
			$"Cannot create a unique Osmosis code for '{code}'.");
	}

	private async ValueTask TryConnectSocketAsync(
		CancellationToken cancellationToken)
	{
		OsmosisSocketClient previous;
		using (_sync.EnterScope())
		{
			if (_socketClient?.IsConnected == true)
				return;
			previous = _socketClient;
			_socketClient = null;
		}
		previous?.Dispose();
		var client = new OsmosisSocketClient(StreamingEndpoint,
			OnSocketSwapAsync, OnSocketErrorAsync)
		{
			Parent = this,
		};
		try
		{
			await client.ConnectAsync(cancellationToken);
			using (_sync.EnterScope())
				_socketClient = client;
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			client.Dispose();
			using (_sync.EnterScope())
				_nextSocketReconnect = CurrentTime + TimeSpan.FromSeconds(15);
			this.AddWarningLog(
				"Osmosis WebSocket unavailable; Level1 remains active: {0}",
				error.Message);
		}
	}

	private async ValueTask OnSocketSwapAsync(OsmosisSwapEvent swap)
	{
		OsmosisMarket market;
		Sides side;
		(long Id, TickSubscription Subscription)[] targets;
		using (_sync.EnterScope())
		{
			if (_marketsByPair.TryGetValue(PairKey(swap.Input.Denomination,
				swap.Output.Denomination), out market))
				side = Sides.Sell;
			else if (_marketsByPair.TryGetValue(PairKey(
				swap.Output.Denomination, swap.Input.Denomination), out market))
				side = Sides.Buy;
			else
				return;
			targets = [.. _tickSubscriptions.Where(pair =>
				ReferenceEquals(pair.Value.Market, market)).Select(static pair =>
					(pair.Key, pair.Value))];
		}
		if (targets.Length == 0)
			return;
		var baseAmount = (side == Sides.Sell
			? swap.Input.Amount
			: swap.Output.Amount).FromBaseUnits(market.BaseToken.Decimals);
		var quoteAmount = (side == Sides.Sell
			? swap.Output.Amount
			: swap.Input.Amount).FromBaseUnits(market.QuoteToken.Decimals);
		if (baseAmount <= 0 || quoteAmount <= 0)
			return;
		var time = await GetBlockTimeAsync(swap.Height, CancellationToken.None);
		var identity = $"{swap.TransactionHash}:{swap.MessageIndex}:" +
			$"{swap.PoolId}:{swap.Input.Amount}:{swap.Output.Amount}";
		foreach (var target in targets)
		{
			if (target.Subscription.To is DateTime end && time > end)
				continue;
			if (!await SendTradeAsync(market, target.Id, identity, time,
				quoteAmount / baseAmount, baseAmount, side,
				CancellationToken.None))
				continue;
			var isFinished = false;
			using (_sync.EnterScope())
			{
				if (_tickSubscriptions.TryGetValue(target.Id,
					out var active))
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

	private async ValueTask<DateTime> GetBlockTimeAsync(long height,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			if (_blockTimes.TryGetValue(height, out var cached))
				return cached;
		var time = await ApiClient.GetBlockTimeAsync(height, cancellationToken);
		using (_sync.EnterScope())
		{
			if (_blockTimes.TryAdd(height, time))
				_blockTimeOrder.Enqueue(height);
			while (_blockTimeOrder.Count > 4096)
				_blockTimes.Remove(_blockTimeOrder.Dequeue());
			return _blockTimes[height];
		}
	}

	private async ValueTask OnSocketErrorAsync(Exception error)
	{
		OsmosisSocketClient client;
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
		OsmosisSocketClient socketClient;
		using (_sync.EnterScope())
		{
			socketClient = _socketClient;
			_socketClient = null;
		}
		socketClient?.Dispose();
		_apiClient?.Dispose();
		_apiClient = null;
		_signer?.Dispose();
		_signer = null;
		_chainId = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByPair.Clear();
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
			_blockTimes.Clear();
			_blockTimeOrder.Clear();
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
			_nextSocketReconnect = default;
		}
	}
}
