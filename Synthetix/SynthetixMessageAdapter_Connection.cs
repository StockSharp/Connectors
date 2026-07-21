namespace StockSharp.Synthetix;

public partial class SynthetixMessageAdapter
{
	private const string _defaultInfoEndpoint =
		"https://papi.synthetix.io/v1/info";
	private const string _defaultTradeEndpoint =
		"https://papi.synthetix.io/v1/trade";
	private const string _defaultInfoSocketEndpoint =
		"wss://papi.synthetix.io/v1/ws/info";
	private const string _defaultTradeSocketEndpoint =
		"wss://papi.synthetix.io/v1/ws/trade";

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_apiClient is not null || _socketClient is not null ||
			_signer is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(PrivateKey);
			if (!SubAccountId.IsEmpty() && !Signer.IsAvailable)
				throw new InvalidOperationException(
					"A Synthetix private key is required when a subaccount is set.");
			if (Signer.IsAvailable && SubAccountId.IsEmpty())
				throw new InvalidOperationException(
					"A Synthetix subaccount ID is required with a private key.");
			_portfolioName = SubAccountId.IsEmpty()
				? null
				: "Synthetix_" + SubAccountId;
			_apiClient = new(
				InfoEndpoint.IsEmpty() ? _defaultInfoEndpoint : InfoEndpoint,
				TradeEndpoint.IsEmpty() ? _defaultTradeEndpoint : TradeEndpoint)
			{
				Parent = this,
			};
			await RefreshMarketsAsync(cancellationToken);
			await RefreshPricesAsync(cancellationToken);
			_socketClient = CreateSocket();
			await SocketClient.ConnectAsync(cancellationToken);
			using (_sync.EnterScope())
			{
				_nextPing = CurrentTime + TimeSpan.FromSeconds(25);
				_nextPrivatePoll = CurrentTime + PollingInterval;
			}
			connectMsg.SessionId = "Synthetix mainnet " +
				(SubAccountId.IsEmpty() ? "public" : SubAccountId);
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
		var isPing = false;
		var isPoll = false;
		using (_sync.EnterScope())
		{
			if (_socketClient is not null && CurrentTime >= _nextPing)
			{
				_nextPing = CurrentTime + TimeSpan.FromSeconds(25);
				isPing = true;
			}
			if (_apiClient is not null && Signer.IsAvailable &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0) &&
				CurrentTime >= _nextPrivatePoll)
			{
				_nextPrivatePoll = CurrentTime + PollingInterval;
				isPoll = true;
			}
		}
		if (isPing)
			await RunSafelyAsync(SocketClient.PingAsync, cancellationToken);
		if (isPoll)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var markets = (await ApiClient.GetMarketsAsync(cancellationToken) ?? [])
			.Where(static market => market?.Symbol.IsEmpty() == false)
			.OrderBy(static market => market.Symbol, StringComparer.Ordinal)
			.ToArray();
		if (markets.Length == 0)
			throw new InvalidDataException(
				"Synthetix returned no usable markets.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets)
			{
				market.Symbol = market.Symbol.Trim().ToUpperInvariant();
				if (!_markets.TryAdd(market.Symbol, market))
					throw new InvalidDataException(
						$"Synthetix returned duplicate market '{market.Symbol}'.");
			}
		}
		UpdateServerTime(ApiClient.ServerTime);
	}

	private async ValueTask RefreshPricesAsync(
		CancellationToken cancellationToken)
	{
		var response = await ApiClient.GetMarketPricesAsync(cancellationToken);
		var prices = response?.Items ?? [];
		using (_sync.EnterScope())
		{
			_prices.Clear();
			foreach (var price in prices.Where(static price =>
				price?.Symbol.IsEmpty() == false))
				_prices[price.Symbol.Trim().ToUpperInvariant()] = price;
		}
		UpdateServerTime(ApiClient.ServerTime);
	}

	private SynthetixSocketClient CreateSocket()
	{
		var socket = new SynthetixSocketClient(
			InfoSocketEndpoint.IsEmpty()
				? _defaultInfoSocketEndpoint
				: InfoSocketEndpoint,
			TradeSocketEndpoint.IsEmpty()
				? _defaultTradeSocketEndpoint
				: TradeSocketEndpoint,
			Signer, SubAccountId, ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		socket.PriceReceived += OnPriceUpdateAsync;
		socket.TradeReceived += OnTradeUpdateAsync;
		socket.BookReceived += OnBookUpdateAsync;
		socket.CandleReceived += OnCandleUpdateAsync;
		socket.PrivateReceived += OnPrivateUpdateAsync;
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
			await RefreshPricesAsync(cancellationToken);
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
		var api = _apiClient;
		_socketClient = null;
		_apiClient = null;
		_signer = null;
		if (socket is not null)
		{
			socket.PriceReceived -= OnPriceUpdateAsync;
			socket.TradeReceived -= OnTradeUpdateAsync;
			socket.BookReceived -= OnBookUpdateAsync;
			socket.CandleReceived -= OnCandleUpdateAsync;
			socket.PrivateReceived -= OnPrivateUpdateAsync;
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
		api?.Dispose();
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_prices.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_seenTrades.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_accountFingerprints.Clear();
			_collateralFingerprints.Clear();
			_positionFingerprints.Clear();
			_orderFingerprints.Clear();
			_seenAccountTrades.Clear();
			_transactionByClientOrder.Clear();
			_transactionByVenueOrder.Clear();
			_portfolioName = null;
			_serverTime = default;
			_nextPrivatePoll = default;
			_nextPing = default;
			_isPrivateSocketSubscribed = false;
		}
	}
}
