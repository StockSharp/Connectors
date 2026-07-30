namespace StockSharp.Dexalot;

public partial class DexalotMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _socketClient is not null ||
			_evmClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			var reference = await RestClient.LoadReferenceDataAsync(
				cancellationToken);
			var pairs = FilterPairs(reference.Pairs);
			_evmClient = new(RpcEndpoint, WalletAddress, PrivateKey)
			{
				Parent = this,
			};
			await EvmClient.VerifyAsync(cancellationToken);
			if (!RestClient.WalletAddress.IsEmpty() &&
				!RestClient.WalletAddress.EqualsIgnoreCase(
					EvmClient.WalletAddress))
				throw new InvalidDataException(
					"Dexalot REST and RPC clients resolved different wallet " +
						"addresses.");
			if (EvmClient.IsWalletConfigured)
				WalletAddress = EvmClient.WalletAddress;
			_tradePairsAddress = (TradePairsAddress.IsEmpty()
				? reference.TradePairs.Address
				: TradePairsAddress).NormalizeAddress();
			_portfolioAddress = (PortfolioAddress.IsEmpty()
				? reference.Portfolio.Address
				: PortfolioAddress).NormalizeAddress();
			_socketClient = new(WebSocketEndpoint)
			{
				Parent = this,
			};
			_socketClient.MessageReceived += OnSocketMessage;
			await SocketClient.ConnectAsync(cancellationToken);
			using (_sync.EnterScope())
				foreach (var pair in pairs)
					_pairs.Add(pair.Pair, pair);
			connectMsg.SessionId =
				$"Dexalot L1 {_tradePairsAddress[2..10]} " +
				(EvmClient.IsWalletConfigured
					? EvmClient.WalletAddress[2..10]
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
		await DrainSocketMessagesAsync(cancellationToken);
		var pollPrivate = false;
		using (_sync.EnterScope())
		{
			if (_restClient is not null && EvmClient.IsWalletConfigured &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0 ||
					_trackedOrders.Values.Any(static order =>
						order.State == OrderStates.Active)) &&
				DateTime.UtcNow >= _nextPrivatePoll)
			{
				_nextPrivatePoll =
					DateTime.UtcNow + PrivatePollingInterval;
				pollPrivate = true;
			}
		}
		if (pollPrivate)
		{
			try
			{
				await PollPrivateAsync(cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
		_ = timeMsg;
	}

	private void OnSocketMessage(JObject message)
	{
		if (message is null)
			return;
		using (_sync.EnterScope())
		{
			_socketMessages.Enqueue(message);

			while (_socketMessages.Count > 10_000)
				_socketMessages.Dequeue();
		}
	}

	private DexalotPair[] FilterPairs(DexalotPair[] pairs)
	{
		if (pairs is not { Length: > 0 })
			throw new InvalidDataException(
				"Dexalot returned no deployed trading pairs.");
		if (Pairs.IsEmpty())
			return pairs;
		var filters = Pairs.Split([';', ','],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (filters.Length == 0)
			return pairs;
		var result = new List<DexalotPair>();

		foreach (var filter in filters)
		{
			var pair = pairs.SingleOrDefault(item =>
				item.Pair.EqualsIgnoreCase(filter));
			if (pair is null)
				throw new InvalidOperationException(
					$"Dexalot pair '{filter}' is not deployed.");
			if (!result.Contains(pair))
				result.Add(pair);
		}

		return [.. result];
	}

	private void DisposeClients()
	{
		if (_socketClient is not null)
			_socketClient.MessageReceived -= OnSocketMessage;
		_socketClient?.Dispose();
		_socketClient = null;
		_restClient?.Dispose();
		_restClient = null;
		_evmClient?.Dispose();
		_evmClient = null;
		_tradePairsAddress = null;
		_portfolioAddress = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_pairs.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_pairReferenceCounts.Clear();
			_chartReferenceCounts.Clear();
			_seenMarketData.Clear();
			_deliveryOrder.Clear();
			_socketMessages.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_trackedOrders.Clear();
			_orderFingerprints.Clear();
			_nextPrivatePoll = default;
		}
	}
}
