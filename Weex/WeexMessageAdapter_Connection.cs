namespace StockSharp.Weex;

public partial class WeexMessageAdapter
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
			if (Passphrase.IsEmpty())
				throw new InvalidOperationException("WEEX API passphrase is not specified.");
		}
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearSubscriptions();
		_restClient = new(SpotRestEndpoint, FuturesRestEndpoint, Key, Secret, Passphrase) { Parent = this };
		_portfolioName = GetPortfolioName(Key);

		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			if (IsSectionEnabled(WeexSections.Spot))
			{
				await RestClient.SyncTimeAsync(WeexSections.Spot, cancellationToken);
				_spotMarketClient = CreateMarketClient(SpotPublicWsEndpoint, WeexSections.Spot);
				await _spotMarketClient.ConnectAsync(cancellationToken);
			}

			if (IsSectionEnabled(WeexSections.Futures))
			{
				await RestClient.SyncTimeAsync(WeexSections.Futures, cancellationToken);
				_futuresMarketClient = CreateMarketClient(FuturesPublicWsEndpoint, WeexSections.Futures);
				await _futuresMarketClient.ConnectAsync(cancellationToken);
			}

			if (RestClient.HasCredentials)
			{
				if (IsSectionEnabled(WeexSections.Spot))
				{
					_spotUserClient = CreateUserClient(SpotPrivateWsEndpoint, WeexSections.Spot);
					await _spotUserClient.ConnectAsync(cancellationToken);
				}
				if (IsSectionEnabled(WeexSections.Futures))
				{
					_futuresUserClient = CreateUserClient(FuturesPrivateWsEndpoint, WeexSections.Futures);
					await _futuresUserClient.ConnectAsync(cancellationToken);
				}
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
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		foreach (var client in GetClients())
			await client.PingAsync(cancellationToken);
	}

	private WeexWsClient CreateMarketClient(string endpoint, WeexSections section)
	{
		var client = new WeexWsClient(endpoint, section, false, null,
			ReConnectionSettings.WorkingTime) { Parent = this };
		client.SpotTickerReceived += OnSpotTickerAsync;
		client.FuturesTickerReceived += OnFuturesTickerAsync;
		client.DepthReceived += OnDepthAsync;
		client.TradesReceived += OnTradesAsync;
		client.CandleReceived += OnCandleAsync;
		client.Error += OnWebSocketErrorAsync;
		return client;
	}

	private WeexWsClient CreateUserClient(string endpoint, WeexSections section)
	{
		var client = new WeexWsClient(endpoint, section, true,
			() => RestClient.CreateWebSocketAuthentication(section),
			ReConnectionSettings.WorkingTime) { Parent = this };
		client.AccountReceived += OnAccountAsync;
		client.OrderReceived += OnOrderAsync;
		client.FillReceived += OnFillAsync;
		client.PositionReceived += OnPositionAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnUserStateChangedAsync;
		return client;
	}

	private WeexWsClient[] GetClients()
		=> new[] { _spotMarketClient, _futuresMarketClient, _spotUserClient, _futuresUserClient }
			.Where(static client => client is not null).ToArray();

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var clients = GetClients();
		_spotMarketClient = null;
		_futuresMarketClient = null;
		_spotUserClient = null;
		_futuresUserClient = null;

		foreach (var client in clients)
		{
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
		_portfolioName = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		ClearSubscriptions();
	}

	private void ClearSubscriptions()
	{
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_tickerReferences.Clear();
			_depthReferences.Clear();
			_tradeReferences.Clear();
			_candleReferences.Clear();
			_orderSections.Clear();
		}
	}

	private ValueTask OnWebSocketErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnUserStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state != ConnectionStates.Connected)
			return;

		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null, 100,
				cancellationToken);
	}
}
