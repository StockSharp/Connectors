namespace StockSharp.Bluefin;

public partial class BluefinMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _socketClient is not null ||
			_signer is not null)
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
			_signer = new(WalletAddress, PrivateKey);
			_accountAddress = Signer.WalletAddress;
			_portfolioName = _accountAddress.IsEmpty()
				? null
				: "Bluefin_" + _accountAddress[2..10];
			WalletAddress = _accountAddress;
			_restClient = new(
				ExchangeEndpoint.IsEmpty()
					? Environment.ExchangeEndpoint()
					: ExchangeEndpoint,
				TradeEndpoint.IsEmpty()
					? Environment.TradeEndpoint()
					: TradeEndpoint,
				AuthEndpoint.IsEmpty()
					? Environment.AuthEndpoint()
					: AuthEndpoint) { Parent = this };
			await RefreshMarketsAsync(cancellationToken);
			if (Signer.IsSigningAvailable)
				await AuthenticateAsync(cancellationToken);
			_socketClient = CreateSocket();
			await SocketClient.ConnectAsync(cancellationToken);
			connectMsg.SessionId = $"Bluefin {Environment} " +
				(_accountAddress.IsEmpty() ? "public" : _accountAddress[..10]);
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
		var isRefresh = false;
		var isPoll = false;
		using (_sync.EnterScope())
		{
			isRefresh = _restClient is not null &&
				!_restClient.AccessToken.IsEmpty() &&
				CurrentTime >= _tokenRefreshTime;
			if (_restClient is not null && !_accountAddress.IsEmpty() &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				isPoll = true;
			}
		}
		if (isRefresh)
			await RunSafelyAsync(RefreshAuthenticationAsync, cancellationToken);
		if (isPoll)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var info = await RestClient.GetExchangeInfoAsync(cancellationToken) ??
			throw new InvalidDataException(
				"Bluefin returned no exchange information.");
		var markets = (info.Markets ?? [])
			.Where(static market => market?.Symbol.IsEmpty() == false)
			.OrderBy(static market => market.Symbol, StringComparer.Ordinal)
			.ToArray();
		if (markets.Length == 0)
			throw new InvalidDataException("Bluefin returned no usable markets.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets)
			{
				market.Symbol = market.Symbol.Trim().ToUpperInvariant();
				if (!_markets.TryAdd(market.Symbol, market))
					throw new InvalidDataException(
						$"Bluefin returned duplicate market '{market.Symbol}'.");
			}
			_contractsConfig = info.ContractsConfig;
		}
		if (info.ServerTimeAtMillis > 0)
			UpdateServerTime(info.ServerTimeAtMillis.FromBluefinMilliseconds());
	}

	private async ValueTask AuthenticateAsync(
		CancellationToken cancellationToken)
	{
		if (!Signer.IsSigningAvailable || _accountAddress.IsEmpty())
			throw new InvalidOperationException(
				"A Sui Ed25519 private key is required for authentication.");
		var request = new BluefinLoginRequest
		{
			AccountAddress = _accountAddress,
			SignedAtMillis = DateTime.UtcNow.ToBluefinMilliseconds(),
			Audience = "api",
		};
		var response = await RestClient.AuthenticateAsync(request,
			Signer.SignLogin(request), cancellationToken);
		if (response?.AccessToken.IsEmpty() != false ||
			response.AccessTokenValidForSeconds <= 0)
			throw new InvalidDataException(
				"Bluefin returned an invalid authentication token.");
		RestClient.SetAccessToken(response.AccessToken);
		var refreshAfter = Math.Max(30,
			response.AccessTokenValidForSeconds - 60);
		using (_sync.EnterScope())
			_tokenRefreshTime = DateTime.UtcNow.AddSeconds(refreshAfter);
	}

	private async ValueTask RefreshAuthenticationAsync(
		CancellationToken cancellationToken)
	{
		await AuthenticateAsync(cancellationToken);
		if (_socketClient is not null)
			await SocketClient.ReplaceAccessTokenAsync(RestClient.AccessToken,
				cancellationToken);
	}

	private BluefinSocketClient CreateSocket()
	{
		var socket = new BluefinSocketClient(
			MarketSocketEndpoint.IsEmpty()
				? Environment.MarketSocketEndpoint()
				: MarketSocketEndpoint,
			AccountSocketEndpoint.IsEmpty()
				? Environment.AccountSocketEndpoint()
				: AccountSocketEndpoint,
			RestClient.AccessToken, ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		socket.MarketMessageReceived += OnMarketMessageAsync;
		socket.AccountMessageReceived += OnAccountMessageAsync;
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
		{
			await RefreshMarketsAsync(cancellationToken);
			using (_sync.EnterScope())
				foreach (var book in _books.Values)
					book.Invalidate();
		}
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
		var signer = _signer;
		_socketClient = null;
		_restClient = null;
		_signer = null;
		if (socket is not null)
		{
			socket.MarketMessageReceived -= OnMarketMessageAsync;
			socket.AccountMessageReceived -= OnAccountMessageAsync;
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
		signer?.Dispose();
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
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_seenTrades.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_accountFingerprints.Clear();
			_assetFingerprints.Clear();
			_positionFingerprints.Clear();
			_orderFingerprints.Clear();
			_seenAccountTrades.Clear();
			_contractsConfig = null;
			_accountAddress = null;
			_portfolioName = null;
			_serverTime = default;
			_nextPrivatePoll = default;
			_tokenRefreshTime = default;
			_lastSalt = 0;
		}
	}
}
