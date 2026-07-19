namespace StockSharp.VALR;

public partial class VALRMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _tradeSocketClient is not null ||
			_accountSocketClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() != Secret.IsEmpty())
			throw new InvalidOperationException(
				"VALR API key and secret must be configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret, SubAccountId)
			{
				Parent = this,
			};
			var pairs = await RestClient.GetPairsAsync(cancellationToken);
			RegisterMarkets(pairs);
			using (_sync.EnterScope())
				if (_markets.Count == 0)
					throw new InvalidDataException(
						"VALR returned no active markets.");

			if (RestClient.IsCredentialsAvailable)
			{
				_tradeSocketClient = new(TradeWebSocketEndpoint, Key, Secret,
					SubAccountId, true, ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				_tradeSocketClient.MarketSummaryReceived +=
					OnSocketMarketSummaryAsync;
				_tradeSocketClient.OrderBookReceived += OnSocketOrderBookAsync;
				_tradeSocketClient.TradeReceived += OnSocketTradeAsync;
				_tradeSocketClient.CandleReceived += OnSocketCandleAsync;
				_tradeSocketClient.Error += OnSocketErrorAsync;
				_tradeSocketClient.StateChanged += OnTradeSocketStateAsync;
				await _tradeSocketClient.ConnectAsync(cancellationToken);

				_accountSocketClient = new(AccountWebSocketEndpoint, Key, Secret,
					SubAccountId, false, ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				_accountSocketClient.BalanceReceived += OnBalanceEventAsync;
				_accountSocketClient.OpenOrdersReceived += OnOpenOrdersEventAsync;
				_accountSocketClient.OrderStatusReceived += OnOrderStatusEventAsync;
				_accountSocketClient.AccountTradeReceived += OnAccountTradeEventAsync;
				_accountSocketClient.PositionReceived += OnPositionEventAsync;
				_accountSocketClient.PositionClosedReceived +=
					OnPositionClosedEventAsync;
				_accountSocketClient.Error += OnSocketErrorAsync;
				_accountSocketClient.StateChanged += OnAccountSocketStateAsync;
				await _accountSocketClient.ConnectAsync(cancellationToken);
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

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnTradeSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state == ConnectionStates.Restored)
			await SendOutConnectionStateAsync(ConnectionStates.Restored,
				cancellationToken);
	}

	private async ValueTask OnAccountSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state == ConnectionStates.Restored)
			await RefreshPrivateSnapshotsAsync(cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var accountSocket = _accountSocketClient;
		_accountSocketClient = null;
		var tradeSocket = _tradeSocketClient;
		_tradeSocketClient = null;
		var rest = _restClient;
		_restClient = null;
		await DisposeSocketAsync(accountSocket, cancellationToken);
		await DisposeSocketAsync(tradeSocket, cancellationToken);
		rest?.Dispose();
		ClearState();
	}

	private async ValueTask DisposeSocketAsync(VALRSocketClient socket,
		CancellationToken cancellationToken)
	{
		if (socket is null)
			return;
		try
		{
			await socket.DisconnectAsync(cancellationToken);
		}
		catch (Exception error)
		{
			if (!cancellationToken.IsCancellationRequested)
				await SendOutErrorAsync(error, cancellationToken);
		}
		finally
		{
			socket.Dispose();
		}
	}

	private void DisposeClients()
	{
		_accountSocketClient?.Dispose();
		_accountSocketClient = null;
		_tradeSocketClient?.Dispose();
		_tradeSocketClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
