namespace StockSharp.Chainflip;

public partial class ChainflipMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_stateClient is not null || _httpClient is not null ||
			_ethereumClient is not null || _arbitrumClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_stateClient = new(StateRpcEndpoint)
			{
				Parent = this,
			};
			_httpClient = new(BackendEndpoint)
			{
				Parent = this,
			};
			var markets = FilterMarkets(
				await StateClient.VerifyAndGetMarketsAsync(
					cancellationToken));
			var probe = markets.FirstOrDefault(static market =>
				market.BaseAsset.Chain.EqualsIgnoreCase("Ethereum") &&
				market.BaseAsset.Symbol.EqualsIgnoreCase("ETH")) ??
				markets[0];
			var probeAmount = BigInteger.Max(
				ProbeVolume.ToBaseUnits(probe.BaseAsset.Decimals),
				await StateClient.GetMinimumDepositAmountAsync(
					probe.BaseAsset, cancellationToken));
			await HttpClient.GetQuoteAsync(probe.BaseAsset,
				probe.QuoteAsset, probeAmount, false, cancellationToken);

			if (HasWalletConfiguration)
			{
				_ethereumClient = new(EthereumRpcEndpoint, "Ethereum",
					WalletAddress, PrivateKey)
				{
					Parent = this,
				};
				_arbitrumClient = new(ArbitrumRpcEndpoint, "Arbitrum",
					WalletAddress, PrivateKey)
				{
					Parent = this,
				};
				await _ethereumClient.VerifyAsync(cancellationToken);
				await _arbitrumClient.VerifyAsync(cancellationToken);
				if (!_ethereumClient.WalletAddress.EqualsIgnoreCase(
					_arbitrumClient.WalletAddress))
					throw new InvalidDataException(
						"Ethereum and Arbitrum clients resolved different " +
							"wallet addresses.");
				WalletAddress = _ethereumClient.WalletAddress;
			}

			_lastFillBlock = await StateClient.GetBestBlockNumberAsync(
				cancellationToken);
			using (_sync.EnterScope())
			{
				foreach (var market in markets)
				{
					_markets.Add(market.SecurityCode, market);
					_marketsByKey.Add(market.Key, market);
				}
			}
			connectMsg.SessionId =
				$"Chainflip mainnet block {_lastFillBlock} " +
				(HasWalletConfiguration
					? WalletAddress[2..10]
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

			if (_stateClient is not null &&
				(_level1Subscriptions.Count > 0 ||
					_depthSubscriptions.Count > 0 ||
					_tickSubscriptions.Count > 0) &&
				now >= _nextMarketPoll)
			{
				_nextMarketPoll = now + PollingInterval;
				pollMarket = true;
			}
			if (_httpClient is not null &&
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
			await RunSafelyAsync(PollMarketDataAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);

		foreach (var target in expired.Distinct())
			await SendSubscriptionFinishedAsync(target, cancellationToken);

		_ = timeMsg;
	}

	private ChainflipMarket[] FilterMarkets(ChainflipMarket[] markets)
	{
		if (markets is not { Length: > 0 })
			throw new InvalidDataException(
				"Chainflip returned no available pools.");
		if (Pools.IsEmpty())
			return markets;
		var filters = Pools.Split([';', ','],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (filters.Length == 0)
			return markets;
		var selected = new List<ChainflipMarket>();

		foreach (var filter in filters)
		{
			var match = markets.FirstOrDefault(market =>
				market.SecurityCode.Equals(filter,
					StringComparison.OrdinalIgnoreCase) ||
				market.Key.Equals(filter,
					StringComparison.OrdinalIgnoreCase) ||
				market.BaseAsset.Key.Equals(filter,
					StringComparison.OrdinalIgnoreCase));
			if (match is null)
				throw new InvalidOperationException(
					$"Chainflip pool '{filter}' is not available.");
			if (!selected.Contains(match))
				selected.Add(match);
		}

		return [.. selected];
	}

	private string GetDestinationAddress(ChainflipAsset asset)
	{
		ArgumentNullException.ThrowIfNull(asset);
		if (asset.IsEvm)
		{
			var client = GetEvmClient(asset.Chain);
			if (client?.IsWalletConfigured != true)
				throw new InvalidOperationException(
					$"Configure an EVM wallet for destination chain " +
						$"'{asset.Chain}'.");
			return client.WalletAddress;
		}
		var address = asset.Chain.ToUpperInvariant() switch
		{
			"BITCOIN" => BitcoinAddress,
			"SOLANA" => SolanaAddress,
			"ASSETHUB" => AssethubAddress,
			"POLKADOT" => PolkadotAddress,
			"TRON" => TronAddress,
			_ => null,
		};
		return address.ThrowIfEmpty(
			$"{asset.Chain} destination address").Trim();
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
		_stateClient?.Dispose();
		_stateClient = null;
		_httpClient?.Dispose();
		_httpClient = null;
		_ethereumClient?.Dispose();
		_ethereumClient = null;
		_arbitrumClient?.Dispose();
		_arbitrumClient = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByKey.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_seenMarketData.Clear();
			_marketDataDeliveryOrder.Clear();
			_level1Fingerprints.Clear();
			_depthFingerprints.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedSwaps.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
			_lastFillBlock = 0;
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
		}
	}
}
