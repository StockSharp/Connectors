namespace StockSharp.StandX;

public partial class StandXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, AuthEndpoint, Chain, WalletAddress,
				PrivateKey) { Parent = this };
			await RestClient.SynchronizeTimeAsync(cancellationToken);
			await RefreshInstrumentsAsync(cancellationToken);
			if (RestClient.IsSigningAvailable)
				await RestClient.AuthenticateAsync(TokenLifetime, cancellationToken);

			_marketSocket = CreateMarketSocket();
			await _marketSocket.ConnectAsync(cancellationToken);
			if (RestClient.IsAuthenticated)
			{
				WalletAddress = RestClient.WalletAddress;
				_portfolioName = CreatePortfolioName(Chain, WalletAddress);
				_orderSocket = CreateOrderSocket();
				await _orderSocket.ConnectAsync(cancellationToken);
			}
			_nextTimeSynchronization = DateTime.UtcNow.AddMinutes(30);
			_nextCandlePoll = DateTime.UtcNow + CandlePollingInterval;
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
		var now = DateTime.UtcNow;
		if (_restClient is not null && now >= _nextTimeSynchronization)
		{
			_nextTimeSynchronization = now.AddMinutes(30);
			try
			{
				await RestClient.SynchronizeTimeAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		CandleSubscription[] candles = [];
		if (_restClient is not null && now >= _nextCandlePoll)
		{
			_nextCandlePoll = now + CandlePollingInterval;
			using (_sync.EnterScope())
				candles = [.. _candleSubscriptions.Values];
		}
		foreach (var candle in candles)
		{
			try
			{
				await PollCandleAsync(candle, cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private StandXMarketWebSocketClient CreateMarketSocket()
	{
		var client = new StandXMarketWebSocketClient(MarketSocketEndpoint,
			RestClient.AccessToken, ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.PriceReceived += OnPriceAsync;
		client.BookReceived += OnBookAsync;
		client.PublicTradeReceived += OnPublicTradeAsync;
		client.OrderReceived += OnOrderAsync;
		client.PositionReceived += OnPositionAsync;
		client.BalanceReceived += OnWalletBalanceAsync;
		client.UserTradeReceived += OnUserTradeAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnMarketSocketStateAsync;
		return client;
	}

	private StandXOrderWebSocketClient CreateOrderSocket()
	{
		var client = new StandXOrderWebSocketClient(OrderSocketEndpoint,
			RestClient, ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnOrderSocketStateAsync;
		return client;
	}

	private async ValueTask RefreshInstrumentsAsync(
		CancellationToken cancellationToken)
	{
		var instruments = (await RestClient.GetSymbolsAsync(cancellationToken) ?? [])
			.Where(static instrument =>
				instrument?.Symbol.IsEmpty() == false && instrument.IsTrading())
			.GroupBy(static instrument => instrument.Symbol.Trim(),
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.Last())
			.ToArray();
		if (instruments.Length == 0)
			throw new InvalidDataException("StandX returned no trading markets.");
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			foreach (var instrument in instruments)
				_instruments[instrument.Symbol.Trim()] = instrument;
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var orderSocket = _orderSocket;
		var marketSocket = _marketSocket;
		var restClient = _restClient;
		_orderSocket = null;
		_marketSocket = null;
		_restClient = null;
		foreach (var client in new Disposable[] { orderSocket, marketSocket }
			.Where(static client => client is not null))
		{
			try
			{
				switch (client)
				{
					case StandXOrderWebSocketClient order:
						await order.DisconnectAsync(cancellationToken);
						break;
					case StandXMarketWebSocketClient market:
						await market.DisconnectAsync(cancellationToken);
						break;
				}
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			client.Dispose();
		}
		restClient?.Dispose();
		ClearState();
	}

	private static string CreatePortfolioName(StandXChains chain,
		string walletAddress)
	{
		walletAddress = walletAddress.ThrowIfEmpty(nameof(walletAddress));
		var suffix = walletAddress.Length <= 12
			? walletAddress
			: walletAddress[..12];
		return $"STANDX_{chain.ToString().ToUpperInvariant()}_{suffix}";
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnMarketSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
		if (state != ConnectionStates.Restored)
			return;
		await RefreshInstrumentsAsync(cancellationToken);
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null,
				null, 500, cancellationToken);
	}

	private async ValueTask OnOrderSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
	}
}
