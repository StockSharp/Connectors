namespace StockSharp.Xrpl;

public partial class XrplMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rpcClient is not null || _signer is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(Account, Seed);
			if (Signer.IsWalletAvailable)
				Account = Signer.WalletAddress;
			var markets = XrplExtensions.ParseMarkets(Markets, DomainId);
			_rpcClient = new(RpcEndpoint)
			{
				Parent = this,
			};
			await RpcClient.VerifyAsync(cancellationToken);
			var ledger = await RpcClient.GetLedgerAsync(null,
				cancellationToken);
			foreach (var market in markets)
			{
				_ = await RpcClient.GetBookAsync(market, 1,
					cancellationToken);
				using (_sync.EnterScope())
					_markets.Add(market.SecurityCode, market);
			}
			using (_sync.EnterScope())
			{
				_latestLedger = ledger.Index;
				_nextMarketPoll = DateTime.UtcNow;
				_nextPrivatePoll = DateTime.UtcNow;
				_nextSocketReconnect = DateTime.UtcNow;
			}
			await TryConnectSocketAsync(cancellationToken);
			connectMsg.SessionId = $"XRPL mainnet " +
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
		var pollMarket = false;
		var pollPrivate = false;
		var reconnectSocket = false;
		long[] expiredTicks;
		long[] expiredCandles;
		using (_sync.EnterScope())
		{
			expiredTicks =
			[
				.. _tickSubscriptions.Where(pair =>
						pair.Value.To is DateTime end &&
						CurrentTime >= end)
					.Select(static pair => pair.Key)
			];
			expiredCandles =
			[
				.. _candleSubscriptions.Where(pair =>
						pair.Value.To is DateTime end &&
						CurrentTime >= end)
					.Select(static pair => pair.Key)
			];
			foreach (var target in expiredTicks)
				RemoveTickSubscriptionNoLock(target);
			foreach (var target in expiredCandles)
				RemoveCandleSubscriptionNoLock(target);
			if (_rpcClient is not null &&
				(_bookSubscriptions.Count > 0 ||
					_level1Subscriptions.Count > 0) &&
				CurrentTime >= _nextMarketPoll)
			{
				_nextMarketPoll = CurrentTime + PollingInterval;
				pollMarket = true;
			}
			if (_rpcClient is not null &&
				Signer.IsWalletAvailable &&
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
				_nextSocketReconnect =
					CurrentTime + TimeSpan.FromSeconds(15);
		}
		if (reconnectSocket)
			await RunSafelyAsync(TryConnectSocketAsync,
				cancellationToken);
		if (pollMarket)
			await RunSafelyAsync(PollMarketAsync, cancellationToken);
		if (pollPrivate)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		foreach (var target in expiredTicks.Concat(expiredCandles))
			await SendSubscriptionFinishedAsync(target,
				cancellationToken);
		_ = timeMsg;
	}

	private async ValueTask TryConnectSocketAsync(
		CancellationToken cancellationToken)
	{
		XrplSocketClient previous;
		using (_sync.EnterScope())
		{
			if (_socketClient?.IsConnected == true)
				return;
			previous = _socketClient;
			_socketClient = null;
		}
		previous?.Dispose();
		var client = new XrplSocketClient(StreamingEndpoint,
			Signer.IsWalletAvailable ? Signer.WalletAddress : null,
			ProcessSocketMessageAsync, OnSocketErrorAsync)
		{
			Parent = this,
		};
		try
		{
			await client.ConnectAsync(cancellationToken);
			using (_sync.EnterScope())
				_socketClient = client;
		}
		catch (Exception error) when (
			!cancellationToken.IsCancellationRequested)
		{
			client.Dispose();
			using (_sync.EnterScope())
				_nextSocketReconnect =
					DateTime.UtcNow + TimeSpan.FromSeconds(15);
			this.AddWarningLog(
				"XRPL WebSocket unavailable; snapshots remain active: {0}",
				error.Message);
		}
	}

	private async ValueTask ProcessSocketMessageAsync(JObject message)
	{
		try
		{
			var type = message.Value<string>("type")?.Trim();
			if (type.EqualsIgnoreCase("ledgerClosed"))
			{
				var index = message.Value<uint?>("ledger_index");
				if (index is uint ledgerIndex)
					using (_sync.EnterScope())
						_latestLedger = Math.Max(_latestLedger,
							ledgerIndex);
				return;
			}
			if (type.EqualsIgnoreCase("bookChanges") ||
				type.EqualsIgnoreCase("book_changes"))
			{
				var index = message.Value<uint?>("ledger_index") ?? 0;
				var time = message.Value<long?>("ledger_time") ?? 0;
				await ProcessBookChangesAsync(
					message["changes"] as JArray, index, time,
					CancellationToken.None);
				using (_sync.EnterScope())
					_latestLedger = Math.Max(_latestLedger, index);
				return;
			}
			if (type.EqualsIgnoreCase("transaction"))
			{
				using (_sync.EnterScope())
					_nextPrivatePoll = DateTime.MinValue;
			}
		}
		catch (Exception error)
		{
			await SendOutErrorAsync(error, CancellationToken.None);
		}
	}

	private async ValueTask OnSocketErrorAsync(Exception error)
	{
		XrplSocketClient client;
		using (_sync.EnterScope())
		{
			client = _socketClient;
			_socketClient = null;
			_nextSocketReconnect =
				DateTime.UtcNow + TimeSpan.FromSeconds(15);
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
		catch (Exception error) when (
			!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private void DisposeClients()
	{
		XrplSocketClient socket;
		using (_sync.EnterScope())
		{
			socket = _socketClient;
			_socketClient = null;
		}
		socket?.Dispose();
		_rpcClient?.Dispose();
		_rpcClient = null;
		_signer?.Dispose();
		_signer = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_bookSubscriptions.Clear();
			_level1Subscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_bookFingerprints.Clear();
			_level1Fingerprints.Clear();
			_seenMarketData.Clear();
			_deliveryOrder.Clear();
			_portfolioSubscriptions.Clear();
			_balanceFingerprints.Clear();
			_orderSubscriptions.Clear();
			_trackedOrders.Clear();
			_latestLedger = 0;
			_nextMarketPoll = default;
			_nextPrivatePoll = default;
			_nextSocketReconnect = default;
		}
	}
}
