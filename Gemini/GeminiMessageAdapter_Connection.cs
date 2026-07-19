namespace StockSharp.Gemini;

public partial class GeminiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		_restClient = new(RestEndpoint, Key, Secret, Account) { Parent = this };
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			RegisterSymbols(await RestClient.GetSymbolsAsync(cancellationToken));
			_wsClient = CreateWebSocketClient();
			await _wsClient.ConnectAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
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
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
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
	protected override ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		return _wsClient?.PingAsync(cancellationToken) ?? default;
	}

	private GeminiWsClient CreateWebSocketClient()
	{
		var client = new GeminiWsClient(WebSocketEndpoint, RestClient,
			IsCancelOnDisconnect, ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.TickerReceived += OnTickerAsync;
		client.DepthReceived += OnDepthAsync;
		client.TradeReceived += OnTradeAsync;
		client.OrderReceived += OnOrderUpdateAsync;
		client.BalanceReceived += OnBalanceUpdateAsync;
		client.PositionReceived += OnPositionUpdateAsync;
		client.Resynchronizing += OnResynchronizingAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
		return client;
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var wsClient = _wsClient;
		_wsClient = null;
		if (wsClient is not null)
		{
			try
			{
				await wsClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			wsClient.Dispose();
		}

		_restClient?.Dispose();
		_restClient = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		ClearState();
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private ValueTask OnResynchronizingAsync(CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		using (_sync.EnterScope())
			foreach (var state in _depthStates.Values)
			{
				state.IsSnapshotReady = false;
				state.LastUpdateId = 0;
				state.Bids.Clear();
				state.Asks.Clear();
			}
		return default;
	}

	private async ValueTask OnWebSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(state, cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;

		if (_portfolioSubscriptionId != 0 && RestClient.IsCredentialsAvailable)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
		if (_orderStatusSubscriptionId != 0 && RestClient.IsCredentialsAvailable)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null,
				500, cancellationToken);
		await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private void RegisterSymbols(IEnumerable<string> symbols)
	{
		using (_sync.EnterScope())
			foreach (var symbol in symbols ?? [])
				if (!symbol.IsEmpty())
					_symbols.Add(symbol.ToUpperInvariant());
	}
}
