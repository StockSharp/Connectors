namespace StockSharp.Xt;

public partial class XtMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		_restClient = new(SpotRestEndpoint, FuturesRestEndpoint, Key, Secret) { Parent = this };
		_portfolioName = GetPortfolioName(Key);
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			await RefreshSymbolMappingsAsync(cancellationToken);
			if (IsSectionEnabled(XtSections.Spot))
			{
				_spotMarketClient = CreateMarketClient(SpotPublicWsEndpoint, XtSections.Spot);
				await _spotMarketClient.ConnectAsync(cancellationToken);
			}
			if (IsSectionEnabled(XtSections.Futures))
			{
				_futuresMarketClient = CreateMarketClient(FuturesPublicWsEndpoint, XtSections.Futures);
				await _futuresMarketClient.ConnectAsync(cancellationToken);
			}

			if (RestClient.IsCredentialsAvailable)
			{
				if (IsSectionEnabled(XtSections.Spot))
				{
					_spotUserClient = CreateUserClient(SpotPrivateWsEndpoint, XtSections.Spot);
					await _spotUserClient.ConnectAsync(cancellationToken);
				}
				if (IsSectionEnabled(XtSections.Futures))
				{
					_futuresUserClient = CreateUserClient(FuturesPrivateWsEndpoint, XtSections.Futures);
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
		return SendHeartbeatsAsync(cancellationToken);
	}

	private async ValueTask SendHeartbeatsAsync(CancellationToken cancellationToken)
	{
		foreach (var client in GetClients())
			await client.SendHeartbeatAsync(cancellationToken);
	}

	private XtWsClient CreateMarketClient(string endpoint, XtSections section)
	{
		var client = new XtWsClient(endpoint, section, false, RestClient,
			ReConnectionSettings.WorkingTime) { Parent = this };
		client.TradesReceived += OnTradesAsync;
		client.DepthReceived += OnDepthAsync;
		client.IndexReceived += OnIndexAsync;
		client.Error += OnWebSocketErrorAsync;
		return client;
	}

	private XtWsClient CreateUserClient(string endpoint, XtSections section)
	{
		var client = new XtWsClient(endpoint, section, true, RestClient,
			ReConnectionSettings.WorkingTime) { Parent = this };
		client.OrderReceived += OnOrderAsync;
		client.FillReceived += OnFillAsync;
		client.BalanceReceived += OnBalanceAsync;
		client.PositionReceived += OnPositionAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnUserStateChangedAsync;
		return client;
	}

	private XtWsClient[] GetClients()
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
			_knownSymbols.Clear();
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
