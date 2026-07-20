namespace StockSharp.Grvt;

public partial class GrvtMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		SnapshotInterval = ValidateSnapshotInterval(SnapshotInterval);
		MarketDepth = ValidateDepth(MarketDepth);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(EdgeEndpoint, MarketDataEndpoint, TradingEndpoint,
				Key, SubAccountId) { Parent = this };
			await RefreshInstrumentsAsync(cancellationToken);
			await SynchronizeTimeAsync(cancellationToken);

			if (!Secret.IsEmpty())
				_signer = new(Secret.UnSecure(), ChainId);
			if (RestClient.IsCredentialsAvailable)
				await RestClient.LoginAsync(cancellationToken);

			_marketSocket = CreateMarketSocket();
			await _marketSocket.ConnectAsync(cancellationToken);

			if (RestClient.IsAuthenticated && !RestClient.SubAccountId.IsEmpty())
			{
				_tradingSocket = CreateTradingSocket();
				await _tradingSocket.ConnectAsync(cancellationToken);
				await SubscribePrivateStreamsAsync(cancellationToken);
			}

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
		_ = timeMsg;
		if (_restClient is not null && DateTime.UtcNow >= _nextServerTimeSync)
			await SynchronizeTimeAsync(cancellationToken);
	}

	private async ValueTask RefreshInstrumentsAsync(
		CancellationToken cancellationToken)
	{
		var instruments = await RestClient.GetAllInstrumentsAsync(
			cancellationToken) ?? [];
		if (instruments.Length == 0)
			throw new InvalidDataException("GRVT returned no active instruments.");
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			foreach (var instrument in instruments)
			{
				if (instrument?.Instrument.IsEmpty() != false ||
					instrument.InstrumentHash.IsEmpty() ||
					instrument.Base.IsEmpty() || instrument.Quote.IsEmpty())
					continue;
				_instruments[instrument.Instrument.ToUpperInvariant()] = instrument;
			}
		}
	}

	private async ValueTask SynchronizeTimeAsync(
		CancellationToken cancellationToken)
	{
		var before = DateTime.UtcNow;
		var server = await RestClient.GetServerTimeAsync(cancellationToken);
		var after = DateTime.UtcNow;
		_serverTimeOffset = server - before.AddTicks((after - before).Ticks / 2);
		_nextServerTimeSync = after.AddMinutes(5);
	}

	private GrvtWebSocketClient CreateMarketSocket()
	{
		var client = new GrvtWebSocketClient(MarketWebSocketEndpoint, null, null,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.TickerReceived += OnTickerAsync;
		client.BookReceived += OnBookAsync;
		client.TradeReceived += OnPublicTradeAsync;
		client.CandlestickReceived += OnCandlestickAsync;
		client.Error += OnWebSocketErrorAsync;
		return client;
	}

	private GrvtWebSocketClient CreateTradingSocket()
	{
		var client = new GrvtWebSocketClient(TradingWebSocketEndpoint,
			RestClient.Cookie, RestClient.AccountId,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.OrderReceived += OnOrderAsync;
		client.FillReceived += OnFillAsync;
		client.PositionReceived += OnPositionAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnTradingSocketStateChangedAsync;
		return client;
	}

	private async ValueTask SubscribePrivateStreamsAsync(
		CancellationToken cancellationToken)
	{
		var selector = RestClient.SubAccountId;
		await _tradingSocket.SubscribeAsync("v1.order", selector,
			cancellationToken);
		await _tradingSocket.SubscribeAsync("v1.fill", selector,
			cancellationToken);
		await _tradingSocket.SubscribeAsync("v1.position", selector,
			cancellationToken);
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnTradingSocketStateChangedAsync(
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state != ConnectionStates.Restored)
			return;
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null,
				cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var marketSocket = _marketSocket;
		var tradingSocket = _tradingSocket;
		_marketSocket = null;
		_tradingSocket = null;

		foreach (var client in new[] { marketSocket, tradingSocket })
		{
			if (client is null)
				continue;
			try
			{
				await client.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			client.Dispose();
		}

		_restClient?.Dispose();
		_restClient = null;
		_signer = null;
		ClearState();
	}

	private void DisposeClients()
	{
		_marketSocket?.Dispose();
		_tradingSocket?.Dispose();
		_restClient?.Dispose();
		_marketSocket = null;
		_tradingSocket = null;
		_restClient = null;
		_signer = null;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_serverTimeOffset = default;
		_nextServerTimeSync = default;
	}
}
