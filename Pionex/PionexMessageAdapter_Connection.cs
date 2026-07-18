namespace StockSharp.Pionex;

public partial class PionexMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		_restClient = new(RestEndpoint, Key, Secret) { Parent = this };
		_portfolioName = GetPortfolioName(Key);
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			await RefreshSymbolMappingsAsync(cancellationToken);
			_marketClient = CreateMarketClient();
			await _marketClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				if (IsSectionEnabled(PionexSections.Spot))
				{
					_spotUserClient = CreateUserClient(SpotPrivateWsEndpoint, PionexSections.Spot);
					await _spotUserClient.ConnectAsync(cancellationToken);
				}
				if (IsSectionEnabled(PionexSections.Futures))
				{
					_futuresUserClient = CreateUserClient(FuturesPrivateWsEndpoint, PionexSections.Futures);
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
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		_ = timeMsg;
		_ = cancellationToken;
		return default;
	}

	private PionexWsClient CreateMarketClient()
	{
		var client = new PionexWsClient(PublicWsEndpoint, null, false, null, null,
			ReConnectionSettings.WorkingTime) { Parent = this };
		client.TradesReceived += OnTradesAsync;
		client.DepthReceived += OnDepthAsync;
		client.IndexReceived += OnIndexAsync;
		client.Error += OnWebSocketErrorAsync;
		return client;
	}

	private PionexWsClient CreateUserClient(string endpoint, PionexSections section)
	{
		var client = new PionexWsClient(endpoint, section, true, Key, Secret,
			ReConnectionSettings.WorkingTime) { Parent = this };
		client.OrderReceived += OnOrderAsync;
		client.FillReceived += OnFillAsync;
		client.BalanceReceived += OnBalanceAsync;
		client.PositionReceived += OnPositionAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnUserStateChangedAsync;
		return client;
	}

	private PionexWsClient[] GetClients()
		=> new[] { _marketClient, _spotUserClient, _futuresUserClient }
			.Where(static client => client is not null).ToArray();

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var clients = GetClients();
		_marketClient = null;
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
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_symbolSections.Clear();
			_spotPrivateSymbols.Clear();
			_privateSymbols.Clear();
		}
	}

	private ValueTask OnWebSocketErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnUserStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is not (ConnectionStates.Connected or ConnectionStates.Restored))
			return;
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null, null, 100,
				cancellationToken);
	}
}
