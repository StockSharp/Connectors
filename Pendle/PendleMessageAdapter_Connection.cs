namespace StockSharp.Pendle;

public partial class PendleMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rpcClient is not null || _httpClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (!System.Enum.IsDefined(Chain))
			throw new InvalidOperationException(
				$"Unsupported Pendle chain '{Chain}'.");
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_rpcClient = new(RpcEndpoint, Chain, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			_httpClient = new(ApiEndpoint, Chain)
			{
				Parent = this,
			};
			await RpcClient.VerifyAsync(cancellationToken);
			await HttpClient.VerifyAsync(cancellationToken);
			WalletAddress = RpcClient.IsWalletConfigured
				? RpcClient.WalletAddress
				: null;

			var markets = await HttpClient.GetMarketsAsync(
				ParseMarketAddresses(), MaxMarkets, cancellationToken);
			if (markets.Length == 0)
				throw new InvalidOperationException(
					"Pendle API returned no active markets for the configured " +
						"network or address filter.");
			var addresses = markets.SelectMany(market => new[]
				{
					market.PrincipalToken.StripChainPrefix(Chain),
					market.YieldToken.StripChainPrefix(Chain),
					market.UnderlyingAsset.StripChainPrefix(Chain),
				})
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			var apiAssets = await HttpClient.GetAssetsAsync(addresses,
				cancellationToken);
			var assets = apiAssets.GroupBy(static asset =>
					asset.Address.NormalizeAddress(),
					StringComparer.OrdinalIgnoreCase)
				.ToDictionary(static group => group.Key,
					static group => group.First(),
					StringComparer.OrdinalIgnoreCase);
			var errors = new List<Exception>();
			foreach (var market in markets)
			{
				try
				{
					await RegisterMarketAsync(market, assets,
						cancellationToken);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					errors.Add(error);
					this.AddWarningLog(
						"Pendle market {0} loading failed: {1}",
						market.Address, error.Message);
				}
			}
			using (_sync.EnterScope())
				if (_securities.Count == 0)
					throw errors.Count == 1
						? errors[0]
						: new AggregateException(
							"No Pendle securities could be loaded.", errors);

			connectMsg.SessionId = RpcClient.IsWalletConfigured
				? $"Pendle {Chain} {RpcClient.WalletAddress}"
				: $"Pendle {Chain} public";
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
		var expired = new List<long>();
		using (_sync.EnterScope())
		{
			foreach (var item in _candleSubscriptions.Where(item =>
				item.Value.To is DateTime end && now >= end).ToArray())
			{
				expired.Add(item.Key);
				RemoveMarketSubscriptionNoLock(item.Key);
			}
			if (_rpcClient is not null && _httpClient is not null &&
				(_level1Subscriptions.Count > 0 ||
					_candleSubscriptions.Count > 0) &&
				now >= _nextMarketPoll)
			{
				_nextMarketPoll = now + PollingInterval;
				pollMarket = true;
			}
			if (_rpcClient is not null && _httpClient is not null &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_trackedSwaps.Values.Any(static swap =>
						swap.State == OrderStates.Active)) &&
				now >= _nextPrivatePoll)
			{
				_nextPrivatePoll = now + PollingInterval;
				pollPrivate = true;
			}
		}
		if (pollMarket)
			await RunSafelyAsync(PollMarketAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		foreach (var target in expired)
			await SendSubscriptionFinishedAsync(target, cancellationToken);
		_ = timeMsg;
	}

	private async ValueTask RegisterMarketAsync(PendleApiMarket source,
		IReadOnlyDictionary<string, PendleApiAsset> assets,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		var address = source.Address.NormalizeAddress();
		if (source.ChainId != (int)Chain)
			throw new InvalidDataException(
				$"Pendle market '{address}' belongs to chain " +
					$"{source.ChainId}.");
		if (!DateTime.TryParse(source.Expiry, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var expiry))
			throw new InvalidDataException(
				$"Pendle market '{address}' has an invalid expiry.");
		var principal = await GetTokenAsync(
			source.PrincipalToken.StripChainPrefix(Chain), assets,
			cancellationToken);
		var yield = await GetTokenAsync(
			source.YieldToken.StripChainPrefix(Chain), assets,
			cancellationToken);
		var underlying = await GetTokenAsync(
			source.UnderlyingAsset.StripChainPrefix(Chain), assets,
			cancellationToken);
		if (principal.Address.EqualsIgnoreCase(yield.Address) ||
			principal.Address.EqualsIgnoreCase(underlying.Address) ||
			yield.Address.EqualsIgnoreCase(underlying.Address))
			throw new InvalidDataException(
				$"Pendle market '{address}' has duplicate asset addresses.");
		var market = new PendleMarket
		{
			Address = address,
			Name = source.Name?.Trim().Truncate(128, string.Empty) ??
				address,
			Protocol = source.Protocol?.Trim().Truncate(64, string.Empty),
			Expiry = expiry,
			PrincipalToken = principal,
			YieldToken = yield,
			UnderlyingToken = underlying,
			Liquidity = source.Details?.Liquidity ?? 0m,
			TradingVolume = source.Details?.TradingVolume ?? 0m,
			ImpliedApy = source.Details?.ImpliedApy ?? 0m,
		};
		RegisterSecurity(market, principal, PendleAssetKinds.Principal);
		RegisterSecurity(market, yield, PendleAssetKinds.Yield);
		using (_sync.EnterScope())
		{
			if (!_markets.TryAdd(address, market))
				throw new InvalidOperationException(
					$"Pendle market '{address}' is listed twice.");
		}
	}

	private async ValueTask<PendleToken> GetTokenAsync(string address,
		IReadOnlyDictionary<string, PendleApiAsset> assets,
		CancellationToken cancellationToken)
	{
		address = address.NormalizeAddress();
		using (_sync.EnterScope())
			if (_tokens.TryGetValue(address, out var cached))
				return cached;
		PendleToken token;
		if (assets.TryGetValue(address, out var asset))
		{
			if (asset.Decimals is < 0 or > 255)
				throw new InvalidDataException(
					$"Pendle asset '{address}' has invalid decimals.");
			var symbol = asset.Symbol.NormalizeTokenSymbol(address);
			token = new()
			{
				Address = address,
				Symbol = symbol,
				Name = asset.Name.NormalizeTokenName(symbol),
				Decimals = asset.Decimals,
			};
		}
		else
			token = await RpcClient.GetTokenAsync(address,
				cancellationToken);
		using (_sync.EnterScope())
		{
			_tokens[token.Address] = token;
			return token;
		}
	}

	private void RegisterSecurity(PendleMarket market, PendleToken token,
		PendleAssetKinds kind)
	{
		var fallback = (kind == PendleAssetKinds.Principal ? "PT-" : "YT-") +
			market.Name + "-" +
			market.Expiry.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		var code = token.Symbol.NormalizeSecurityCode(fallback);
		using (_sync.EnterScope())
		{
			if (_securities.ContainsKey(code))
				code = (code + "-" + token.Address[2..8])
					.NormalizeSecurityCode(fallback);
			if (_securities.ContainsKey(code))
				throw new InvalidOperationException(
					$"Pendle security code '{code}' is duplicated.");
			_securities.Add(code, new()
			{
				Market = market,
				Token = token,
				Kind = kind,
				SecurityCode = code,
			});
		}
	}

	private string[] ParseMarketAddresses()
	{
		if (MarketAddresses.IsEmpty())
			return [];
		var result = MarketAddresses.Split(';',
				StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries)
			.Select(static address => address.NormalizeAddress())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (result.Length == 0)
			throw new InvalidOperationException(
				"At least one Pendle market address must be configured.");
		if (result.Length > MaxMarkets)
			throw new InvalidOperationException(
				"Configured Pendle market addresses exceed MaxMarkets.");
		return result;
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
		_httpClient?.Dispose();
		_httpClient = null;
		_rpcClient?.Dispose();
		_rpcClient = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_securities.Clear();
			_tokens.Clear();
			_level1Subscriptions.Clear();
			_candleSubscriptions.Clear();
			_seenMarketData.Clear();
			_marketDataDeliveryOrder.Clear();
			_level1Fingerprints.Clear();
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
