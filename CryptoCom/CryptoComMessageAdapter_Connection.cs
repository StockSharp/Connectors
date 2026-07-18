namespace StockSharp.CryptoCom;

public partial class CryptoComMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;

		if (this.IsTransactional())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);
			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

		if (_restClient is not null || _marketWsClient is not null || _userWsClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var restEndpoint = IsDemo ? _demoRestEndpoint : RestEndpoint;
		var marketWsEndpoint = IsDemo ? _demoMarketWsEndpoint : MarketWsEndpoint;
		var userWsEndpoint = IsDemo ? _demoUserWsEndpoint : UserWsEndpoint;

		ClearSubscriptions();
		_restClient = new(restEndpoint, Key, Secret) { Parent = this };
		_marketWsClient = new(marketWsEndpoint, false, null, null, ReConnectionSettings.WorkingTime)
		{
			Parent = this,
		};
		_marketWsClient.TickerReceived += OnTickerAsync;
		_marketWsClient.BookReceived += OnBookAsync;
		_marketWsClient.TradeReceived += OnTradeAsync;
		_marketWsClient.CandleReceived += OnCandleAsync;
		_marketWsClient.Error += OnWebSocketErrorAsync;

		_portfolioName = $"CryptoCom_{(Key.IsEmpty() ? "Public" : Key.ToId())}";

		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			await _marketWsClient.ConnectAsync(cancellationToken);

			if (!Key.IsEmpty() && !Secret.IsEmpty())
			{
				_userWsClient = new(userWsEndpoint, true, Key, Secret, ReConnectionSettings.WorkingTime)
				{
					Parent = this,
				};
				_userWsClient.UserOrderReceived += OnUserOrderAsync;
				_userWsClient.UserTradeReceived += OnUserTradeAsync;
				_userWsClient.BalanceReceived += OnBalanceAsync;
				_userWsClient.PositionReceived += OnPositionAsync;
				_userWsClient.Error += OnWebSocketErrorAsync;
				await _userWsClient.ConnectAsync(cancellationToken);
				await _userWsClient.SubscribeAsync("user.order", cancellationToken);
				await _userWsClient.SubscribeAsync("user.advance.order", cancellationToken);
				await _userWsClient.SubscribeAsync("user.trade", cancellationToken);
				await _userWsClient.SubscribeAsync("user.balance", cancellationToken);
				await _userWsClient.SubscribeAsync("user.positions", cancellationToken);
			}

			await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();

		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		_ = timeMsg;
		_ = cancellationToken;
		return default;
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		ClearSubscriptions();

		if (_userWsClient is not null)
		{
			_userWsClient.UserOrderReceived -= OnUserOrderAsync;
			_userWsClient.UserTradeReceived -= OnUserTradeAsync;
			_userWsClient.BalanceReceived -= OnBalanceAsync;
			_userWsClient.PositionReceived -= OnPositionAsync;
			_userWsClient.Error -= OnWebSocketErrorAsync;
			try
			{
				await _userWsClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			_userWsClient.Dispose();
			_userWsClient = null;
		}

		if (_marketWsClient is not null)
		{
			_marketWsClient.TickerReceived -= OnTickerAsync;
			_marketWsClient.BookReceived -= OnBookAsync;
			_marketWsClient.TradeReceived -= OnTradeAsync;
			_marketWsClient.CandleReceived -= OnCandleAsync;
			_marketWsClient.Error -= OnWebSocketErrorAsync;
			try
			{
				await _marketWsClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			_marketWsClient.Dispose();
			_marketWsClient = null;
		}

		_restClient?.Dispose();
		_restClient = null;
		_portfolioName = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
	}

	private void ClearSubscriptions()
	{
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_channelReferences.Clear();
		}
	}

	private ValueTask OnWebSocketErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);
}
