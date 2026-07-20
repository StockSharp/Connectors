namespace StockSharp.Drift;

public partial class DriftMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _dataSocket is not null ||
			_dlobSocket is not null || _signer is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(WalletAddress, PrivateKey);
			WalletAddress = Signer.WalletAddress;
			_restClient = new(DataApiEndpoint, DlobEndpoint) { Parent = this };
			await RefreshMarketsAsync(cancellationToken);
			await ConfigureAccountAsync(cancellationToken);
			_dataSocket = CreateDataSocket();
			_dlobSocket = CreateDlobSocket();
			await DataSocket.ConnectAsync(cancellationToken);
			await DlobSocket.ConnectAsync(cancellationToken);
			connectMsg.SessionId = "Drift " +
				(_accountAddress.IsEmpty() ? "public" : _accountAddress[..8]);
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
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
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var isPoll = false;
		using (_sync.EnterScope())
			if (_restClient is not null && !_accountAddress.IsEmpty() &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				isPoll = true;
			}
		if (isPoll)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetMarketsAsync(cancellationToken);
		var markets = response?.Markets?
			.Where(static market => market?.Symbol.IsEmpty() == false)
			.OrderBy(static market => market.Symbol, StringComparer.Ordinal)
			.ToArray() ?? [];
		if (markets.Length == 0)
			throw new InvalidDataException("Drift returned no usable markets.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets)
			{
				market.Symbol = market.Symbol.Trim().ToUpperInvariant();
				if (!_markets.TryAdd(market.Symbol, market))
					throw new InvalidDataException(
						$"Drift returned duplicate market '{market.Symbol}'.");
			}
		}
	}

	private async ValueTask ConfigureAccountAsync(
		CancellationToken cancellationToken)
	{
		_accountAddress = AccountAddress.IsEmpty()
			? null
			: AccountAddress.NormalizePublicKey();
		if (Signer.IsWalletAvailable)
		{
			var accounts = await RestClient.GetAccountsAsync(Signer.WalletAddress,
				cancellationToken);
			var available = (accounts?.Accounts ?? [])
				.Where(static account => account?.AccountId.IsEmpty() == false)
				.OrderBy(static account => account.SubAccountId)
				.ToArray();
			if (_accountAddress.IsEmpty())
				_accountAddress = available.FirstOrDefault()?.AccountId?
					.NormalizePublicKey();
			else if (available.Length > 0 && !available.Any(account =>
				account.AccountId.Equals(_accountAddress,
					StringComparison.Ordinal)))
				throw new InvalidOperationException(
					"The configured Drift account does not belong to the wallet.");
		}
		AccountAddress = _accountAddress;
		_portfolioName = _accountAddress.IsEmpty()
			? null
			: "Drift_" + _accountAddress[..8];
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

	private DriftDataSocketClient CreateDataSocket()
	{
		var socket = new DriftDataSocketClient(DataSocketEndpoint,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		socket.MarketsReceived += OnMarketsAsync;
		socket.CandleReceived += OnCandleAsync;
		socket.Error += OnSocketErrorAsync;
		socket.StateChanged += OnSocketStateAsync;
		return socket;
	}

	private DriftDlobSocketClient CreateDlobSocket()
	{
		var socket = new DriftDlobSocketClient(DlobSocketEndpoint,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		socket.BookReceived += OnBookAsync;
		socket.TradeReceived += OnTradeAsync;
		socket.Error += OnSocketErrorAsync;
		socket.StateChanged += OnSocketStateAsync;
		return socket;
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
		if (state == ConnectionStates.Restored)
			await RefreshMarketsAsync(cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var dataSocket = _dataSocket;
		var dlobSocket = _dlobSocket;
		var rest = _restClient;
		var signer = _signer;
		_dataSocket = null;
		_dlobSocket = null;
		_restClient = null;
		_signer = null;
		foreach (var socket in new BaseLogReceiver[] { dataSocket, dlobSocket })
		{
			if (socket is null)
				continue;
			try
			{
				if (socket is DriftDataSocketClient data)
					await data.DisconnectAsync(cancellationToken);
				else if (socket is DriftDlobSocketClient dlob)
					await dlob.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			socket.Dispose();
		}
		rest?.Dispose();
		signer?.Dispose();
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_depthReferences.Clear();
			_tradeReferences.Clear();
			_candleReferences.Clear();
			_seenTrades.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_accountFingerprints.Clear();
			_balanceFingerprints.Clear();
			_positionFingerprints.Clear();
			_orderFingerprints.Clear();
			_knownOrders.Clear();
			_seenAccountTrades.Clear();
			_accountAddress = null;
			_portfolioName = null;
			_serverTime = default;
			_nextPrivatePoll = default;
		}
	}
}
