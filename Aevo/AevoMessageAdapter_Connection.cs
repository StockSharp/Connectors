namespace StockSharp.Aevo;

public partial class AevoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _socketClient is not null ||
			_signer is not null || _authenticator is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (!System.Enum.IsDefined(Environment))
			throw new ArgumentOutOfRangeException(nameof(Environment), Environment,
				null);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_authenticator = new(Key?.UnSecure(), Secret);
			_signer = new(SigningKey, Environment);
			_restClient = new(RestEndpoint.IsEmpty()
				? Environment.RestEndpoint()
				: RestEndpoint, _authenticator) { Parent = this };
			var time = await RestClient.GetTimeAsync(cancellationToken);
			if (time?.Timestamp > 0)
				UpdateServerTime(DateTime.UnixEpoch.AddSeconds(time.Timestamp));
			await RefreshMarketsAsync(cancellationToken);
			await ConfigureAccountAsync(cancellationToken);
			_socketClient = CreateSocket();
			await SocketClient.ConnectAsync(cancellationToken);
			_nextPing = DateTime.UtcNow + TimeSpan.FromMinutes(5);
			connectMsg.SessionId = $"Aevo {Environment} " +
				(_account.IsEmpty() ? "public" : _account[..10]);
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
		var isPing = false;
		using (_sync.EnterScope())
		{
			if (_restClient is not null && !_account.IsEmpty() &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				isPoll = true;
			}
			if (_socketClient is not null && CurrentTime >= _nextPing)
			{
				_nextPing = CurrentTime + TimeSpan.FromMinutes(5);
				isPing = true;
			}
		}
		if (isPoll)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		if (isPing)
			await RunSafelyAsync(SocketClient.PingAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var markets = (await RestClient.GetMarketsAsync(cancellationToken) ?? [])
			.Where(static market => market?.InstrumentName.IsEmpty() == false &&
				market.InstrumentId.IsEmpty() == false)
			.OrderBy(static market => market.InstrumentName,
				StringComparer.Ordinal)
			.ToArray();
		if (markets.Length == 0)
			throw new InvalidDataException("Aevo returned no usable instruments.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets)
			{
				var symbol = market.InstrumentName.Trim().ToUpperInvariant();
				if (!_markets.TryAdd(symbol, market))
					throw new InvalidDataException(
						$"Aevo returned duplicate instrument '{symbol}'.");
			}
		}
	}

	private async ValueTask ConfigureAccountAsync(
		CancellationToken cancellationToken)
	{
		_account = WalletAddress.IsEmpty()
			? null
			: WalletAddress.NormalizeAddress(nameof(WalletAddress));
		if (_authenticator.IsAvailable)
		{
			var auth = await RestClient.VerifyAuthenticationAsync(cancellationToken);
			if (auth?.IsSuccess != true)
				throw new InvalidOperationException(
					"Aevo rejected the configured API credentials.");
			var account = await RestClient.GetAccountAsync(cancellationToken);
			var returned = account?.Account.NormalizeAddress("Aevo account") ??
				throw new InvalidDataException(
					"Aevo returned no account address.");
			if (!_account.IsEmpty() && !_account.Equals(returned,
				StringComparison.Ordinal))
				throw new InvalidOperationException(
					"The configured Aevo wallet does not match the API account.");
			_account = returned;
			ValidateSigningKey(account);
		}
		WalletAddress = _account;
		_portfolioName = _account.IsEmpty()
			? null
			: "Aevo_" + _account[2..10];
	}

	private void ValidateSigningKey(AevoAccount account)
	{
		if (!Signer.IsAvailable)
			return;
		var signingAddress = Signer.SigningAddress;
		var registered = (account.SigningKeys ?? []).FirstOrDefault(key =>
			key?.SigningKey.IsEmpty() == false &&
			key.SigningKey.NormalizeAddress("Aevo signing key").Equals(
				signingAddress, StringComparison.Ordinal));
		if (registered is null)
			throw new InvalidOperationException(
				"The configured Aevo signing key is not registered for the " +
				"authenticated account.");
		if (!registered.Expiry.IsEmpty())
		{
			if (!BigInteger.TryParse(registered.Expiry, NumberStyles.None,
				CultureInfo.InvariantCulture, out var expiry) || expiry < 0)
				throw new InvalidDataException(
					"Aevo returned an invalid signing-key expiry.");
			if (expiry > 0 && expiry <= long.MaxValue &&
				DateTime.UnixEpoch.AddTicks((long)expiry / 100) <= ServerTime)
				throw new InvalidOperationException(
					"The configured Aevo signing key has expired.");
		}
	}

	private AevoSocketClient CreateSocket()
	{
		var socket = new AevoSocketClient(SocketEndpoint.IsEmpty()
			? Environment.SocketEndpoint()
			: SocketEndpoint, _authenticator,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		socket.TickerReceived += OnTickerAsync;
		socket.OrderBookReceived += OnOrderBookAsync;
		socket.TradeReceived += OnTradeAsync;
		socket.PositionsReceived += OnPositionsAsync;
		socket.OrdersReceived += OnOrdersAsync;
		socket.FillReceived += OnFillAsync;
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

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socketClient;
		var rest = _restClient;
		_socketClient = null;
		_restClient = null;
		_signer = null;
		_authenticator = null;
		if (socket is not null)
		{
			socket.TickerReceived -= OnTickerAsync;
			socket.OrderBookReceived -= OnOrderBookAsync;
			socket.TradeReceived -= OnTradeAsync;
			socket.PositionsReceived -= OnPositionsAsync;
			socket.OrdersReceived -= OnOrdersAsync;
			socket.FillReceived -= OnFillAsync;
			socket.Error -= OnSocketErrorAsync;
			socket.StateChanged -= OnSocketStateAsync;
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			finally
			{
				socket.Dispose();
			}
		}
		rest?.Dispose();
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_books.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_channelReferences.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_accountFingerprints.Clear();
			_collateralFingerprints.Clear();
			_positionFingerprints.Clear();
			_orderFingerprints.Clear();
			_seenAccountTrades.Clear();
			_account = null;
			_portfolioName = null;
			_serverTime = default;
			_nextPrivatePoll = default;
			_nextPing = default;
		}
	}
}
