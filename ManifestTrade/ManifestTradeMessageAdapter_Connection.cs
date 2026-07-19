namespace StockSharp.ManifestTrade;

public partial class ManifestTradeMessageAdapter
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
			StatsEndpoint = StatsEndpoint.IsEmpty()
				? _defaultStatsEndpoint
				: StatsEndpoint.Trim();
			_rpcClient = new(RpcEndpoint, Cluster, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await RpcClient.VerifyAsync(cancellationToken);
			WalletAddress = RpcClient.WalletAddress;
			if (Cluster == ManifestTradeClusters.Mainnet)
				_statsClient = new(StatsEndpoint) { Parent = this };

			var definitions = await LoadMarketDefinitionsAsync(
				cancellationToken);
			if (definitions.Length == 0)
				throw new InvalidOperationException(
					"At least one Manifest Trade market must be configured or " +
					"discovered.");
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
						"Manifest market {0} loading failed: {1}",
						definition.MarketAddress, error.Message);
				}
			}
			using (_sync.EnterScope())
				if (_markets.Count == 0)
					throw errors.Count == 1
						? errors[0]
						: new AggregateException(
							"No Manifest Trade markets could be loaded.", errors);

			await TryConnectSocketAsync(cancellationToken);
			connectMsg.SessionId = $"Manifest Trade {Cluster} " +
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
					_trackedOrders.Values.Any(static order =>
						order.State == OrderStates.Active)) &&
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

	private async ValueTask<ManifestTradeMarketDefinition[]>
		LoadMarketDefinitionsAsync(CancellationToken cancellationToken)
	{
		var configured = ParseMarketDefinitions();
		var definitions = new Dictionary<string,
			ManifestTradeMarketDefinition>(StringComparer.Ordinal);
		if (_statsClient is not null && MaximumDiscoveredMarkets > 0)
		{
			try
			{
				foreach (var ticker in await _statsClient.GetTickersAsync(
					MaximumDiscoveredMarkets, cancellationToken))
				{
					var address = ticker.MarketAddress.NormalizePublicKey();
					definitions[address] = new()
					{
						MarketAddress = address,
						Ticker = ticker,
					};
				}
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested && configured.Length > 0)
			{
				this.AddWarningLog(
					"Manifest market discovery failed; configured markets " +
					"remain active: {0}", error.Message);
			}
		}
		foreach (var definition in configured)
		{
			definitions.TryGetValue(definition.MarketAddress,
				out var discovered);
			definitions[definition.MarketAddress] = new()
			{
				MarketAddress = definition.MarketAddress,
				BaseSymbol = definition.BaseSymbol,
				QuoteSymbol = definition.QuoteSymbol,
				Ticker = discovered?.Ticker,
			};
		}
		return [.. definitions.Values];
	}

	private async ValueTask<ManifestTradeMarket> LoadMarketAsync(
		ManifestTradeMarketDefinition definition,
		CancellationToken cancellationToken)
	{
		var account = await RpcClient.GetAccountAsync(definition.MarketAddress,
			cancellationToken);
		if (account is null)
			throw new InvalidDataException(
				$"Manifest market '{definition.MarketAddress}' was not found.");
		var slot = await RpcClient.GetSlotAsync(cancellationToken);
		var market = ManifestTradeExtensions.DecodeMarket(
			definition.MarketAddress, account, slot);
		if (definition.Ticker is { } ticker &&
			(!ticker.BaseMint.Equals(market.BaseToken.Mint,
				StringComparison.Ordinal) ||
			 !ticker.QuoteMint.Equals(market.QuoteToken.Mint,
				StringComparison.Ordinal)))
			throw new InvalidDataException(
				$"Stats metadata for market '{definition.MarketAddress}' does " +
				"not match the on-chain account.");
		var accounts = await RpcClient.GetAccountsAsync(
		[
			market.BaseToken.Mint,
			market.QuoteToken.Mint,
			ManifestTradeExtensions.MetadataAddress(market.BaseToken.Mint),
			ManifestTradeExtensions.MetadataAddress(market.QuoteToken.Mint),
		], cancellationToken);
		if (accounts.Length != 4 || accounts[0] is null || accounts[1] is null)
			throw new InvalidDataException(
				$"Manifest market '{definition.MarketAddress}' has missing " +
				"mint accounts.");
		var metadataBase = ManifestTradeExtensions.DecodeMetadata(accounts[2],
			market.BaseToken.Mint);
		var metadataQuote = ManifestTradeExtensions.DecodeMetadata(accounts[3],
			market.QuoteToken.Mint);
		var baseToken = ManifestTradeExtensions.DecodeMint(
			market.BaseToken.Mint, accounts[0],
			definition.BaseSymbol ?? metadataBase.Symbol, metadataBase.Name);
		var quoteToken = ManifestTradeExtensions.DecodeMint(
			market.QuoteToken.Mint, accounts[1],
			definition.QuoteSymbol ?? metadataQuote.Symbol, metadataQuote.Name);
		if (baseToken.Decimals != market.BaseToken.Decimals ||
			quoteToken.Decimals != market.QuoteToken.Decimals)
			throw new InvalidDataException(
				$"Manifest market '{definition.MarketAddress}' mint decimals " +
				"do not match the on-chain header.");
		market.BaseToken = baseToken;
		market.QuoteToken = quoteToken;
		return market;
	}

	private void RegisterMarket(ManifestTradeMarket market)
	{
		var pair = $"{market.BaseToken.Symbol}-{market.QuoteToken.Symbol}"
			.ToUpperInvariant();
		using (_sync.EnterScope())
		{
			if (_marketsByAddress.ContainsKey(market.MarketAddress))
				return;
			var matches = _marketsByAddress.Values.Where(existing =>
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
					existing.SecurityCode = BuildUniqueMarketCode(pair,
						existing.MarketAddress);
					_markets.Add(existing.SecurityCode, existing);
				}
				market.SecurityCode = BuildUniqueMarketCode(pair,
					market.MarketAddress);
			}
			else
				market.SecurityCode = pair;
			if (_markets.ContainsKey(market.SecurityCode))
				throw new InvalidDataException(
					$"Duplicate Manifest security code '{market.SecurityCode}'.");
			_markets.Add(market.SecurityCode, market);
			_marketsByAddress.Add(market.MarketAddress, market);
			_tokens[market.BaseToken.Mint] = market.BaseToken;
			_tokens[market.QuoteToken.Mint] = market.QuoteToken;
		}
	}

	private ManifestTradeMarketDefinition[] ParseMarketDefinitions()
	{
		if (Markets.IsEmpty())
			return [];
		var result = new List<ManifestTradeMarketDefinition>();
		foreach (var item in Markets.Split(';',
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries))
		{
			var fields = item.Split('|', StringSplitOptions.TrimEntries);
			if (fields.Length is not (1 or 3))
				throw new FormatException(
					"Each Manifest market must use market or " +
					"market|base-symbol|quote-symbol format.");
			result.Add(new()
			{
				MarketAddress = fields[0].NormalizePublicKey(),
				BaseSymbol = fields.Length == 3
					? NormalizeSymbolOverride(fields[1])
					: null,
				QuoteSymbol = fields.Length == 3
					? NormalizeSymbolOverride(fields[2])
					: null,
			});
		}
		return [.. result.GroupBy(static definition =>
			definition.MarketAddress, StringComparer.Ordinal)
			.Select(static group => group.First())];
	}

	private async ValueTask TryConnectSocketAsync(
		CancellationToken cancellationToken)
	{
		ManifestTradeSocketClient previous;
		string[] markets;
		using (_sync.EnterScope())
		{
			if (_socketClient?.IsConnected == true)
				return;
			previous = _socketClient;
			_socketClient = null;
			markets = [.. _marketsByAddress.Keys];
		}
		previous?.Dispose();
		var client = new ManifestTradeSocketClient(StreamingEndpoint,
			OnSocketAccountAsync, OnSocketLogsAsync, OnSocketErrorAsync)
		{
			Parent = this,
		};
		try
		{
			await client.ConnectAsync(markets, cancellationToken);
			using (_sync.EnterScope())
				_socketClient = client;
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			client.Dispose();
			using (_sync.EnterScope())
				_nextSocketReconnect = CurrentTime + TimeSpan.FromSeconds(15);
			this.AddWarningLog(
				"Manifest Trade WebSocket unavailable; polling remains active: {0}",
				error.Message);
		}
	}

	private async ValueTask OnSocketAccountAsync(string marketAddress,
		ManifestTradeRpcAccount account, long slot)
	{
		ManifestTradeMarket market;
		using (_sync.EnterScope())
			if (!_marketsByAddress.TryGetValue(marketAddress, out market))
				return;
		var state = ManifestTradeExtensions.DecodeMarket(marketAddress,
			account, slot);
		ApplyMarketState(market, state);
		await PublishMarketAsync(market, CancellationToken.None);
	}

	private ValueTask OnSocketLogsAsync(string signature, string[] logs)
		=> ProcessRealtimeEventsAsync(signature, logs);

	private async ValueTask OnSocketErrorAsync(Exception error)
	{
		ManifestTradeSocketClient client;
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
		ManifestTradeSocketClient socketClient;
		using (_sync.EnterScope())
		{
			socketClient = _socketClient;
			_socketClient = null;
		}
		socketClient?.Dispose();
		_rpcClient?.Dispose();
		_rpcClient = null;
		_statsClient?.Dispose();
		_statsClient = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByAddress.Clear();
			_tokens.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_seenTrades.Clear();
			_seenPrivateExecutions.Clear();
			_tradeDeliveryOrder.Clear();
			_candleFingerprints.Clear();
			_bookFingerprints.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedOrders.Clear();
			_pendingActions.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
			_nextSocketReconnect = default;
		}
	}

	private static void ApplyMarketState(ManifestTradeMarket target,
		ManifestTradeMarket source)
	{
		if (!source.BaseToken.Mint.Equals(target.BaseToken.Mint,
				StringComparison.Ordinal) ||
			!source.QuoteToken.Mint.Equals(target.QuoteToken.Mint,
				StringComparison.Ordinal) ||
			!source.BaseVault.Equals(target.BaseVault, StringComparison.Ordinal) ||
			!source.QuoteVault.Equals(target.QuoteVault,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Manifest market '{target.MarketAddress}' changed immutable " +
				"metadata.");
		target.Version = source.Version;
		target.NextOrderSequence = source.NextOrderSequence;
		target.QuoteVolumeAtoms = source.QuoteVolumeAtoms;
		target.Slot = source.Slot;
		target.Bids = source.Bids;
		target.Asks = source.Asks;
		target.Seats = source.Seats;
	}

	private string BuildUniqueMarketCode(string pair, string marketAddress)
	{
		for (var length = 6; length <= 20; length += 2)
		{
			var code = $"{pair}-{marketAddress[..length].ToUpperInvariant()}";
			if (!_markets.ContainsKey(code))
				return code;
		}
		return $"{pair}-{marketAddress.ToUpperInvariant()}";
	}

	private static string NormalizeSymbolOverride(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length > 20 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			throw new FormatException(
				$"Invalid Manifest token symbol override '{value}'.");
		return value.ToUpperInvariant();
	}
}
