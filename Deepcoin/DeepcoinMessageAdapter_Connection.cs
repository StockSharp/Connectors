namespace StockSharp.Deepcoin;

public partial class DeepcoinMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		_restClient = new(RestEndpoint, Key, Secret, Passphrase) { Parent = this };
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			_spotWsClient = CreatePublicClient(DeepcoinProductTypes.Spot, SpotWsEndpoint);
			_swapWsClient = CreatePublicClient(DeepcoinProductTypes.Swap, SwapWsEndpoint);
			await _spotWsClient.ConnectAsync(cancellationToken);
			await _swapWsClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				var listenKey = await RestClient.AcquireListenKeyAsync(cancellationToken);
				_listenKey = listenKey?.Value.ThrowIfEmpty("Deepcoin listen key");
				_nextListenKeyExtension = DateTime.UtcNow.AddMinutes(30);
				_privateWsClient = CreatePrivateClient(_listenKey);
				await _privateWsClient.ConnectAsync(cancellationToken);
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
		if (_spotWsClient is not null)
			await _spotWsClient.PingAsync(cancellationToken);
		if (_swapWsClient is not null)
			await _swapWsClient.PingAsync(cancellationToken);
		if (_privateWsClient is not null)
		{
			await _privateWsClient.PingAsync(cancellationToken);
			if (DateTime.UtcNow >= _nextListenKeyExtension)
			{
				var result = await RestClient.ExtendListenKeyAsync(_listenKey, cancellationToken);
				if (result?.Value.IsEmpty() != false)
					throw new InvalidDataException("Deepcoin did not confirm the listen-key extension.");
				if (!result.Value.EqualsIgnoreCase(_listenKey))
					throw new InvalidOperationException(
						"Deepcoin replaced the active listen key; reconnect is required.");
				_nextListenKeyExtension = DateTime.UtcNow.AddMinutes(30);
			}
		}
	}

	private DeepcoinPublicWsClient CreatePublicClient(DeepcoinProductTypes productType,
		string endpoint)
	{
		var client = new DeepcoinPublicWsClient(endpoint, productType,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.TickerReceived += OnTickerAsync;
		client.BookReceived += OnBookAsync;
		client.TradeReceived += OnTradeAsync;
		client.CandleReceived += OnCandleAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPublicWebSocketStateAsync;
		return client;
	}

	private DeepcoinPrivateWsClient CreatePrivateClient(string listenKey)
	{
		var client = new DeepcoinPrivateWsClient(PrivateWsEndpoint, listenKey,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.AssetReceived += OnPrivateAssetAsync;
		client.OrderReceived += OnPrivateOrderAsync;
		client.PositionReceived += OnPrivatePositionAsync;
		client.TradeReceived += OnPrivateTradeAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPrivateWebSocketStateAsync;
		return client;
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var privateClient = _privateWsClient;
		var swapClient = _swapWsClient;
		var spotClient = _spotWsClient;
		_privateWsClient = null;
		_swapWsClient = null;
		_spotWsClient = null;

		foreach (var client in new BaseLogReceiver[] { privateClient, swapClient, spotClient }
			.Where(static item => item is not null))
		{
			try
			{
				switch (client)
				{
					case DeepcoinPrivateWsClient privateWs:
						await privateWs.DisconnectAsync(cancellationToken);
						break;
					case DeepcoinPublicWsClient publicWs:
						await publicWs.DisconnectAsync(cancellationToken);
						break;
				}
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			client.Dispose();
		}

		_restClient?.Dispose();
		_restClient = null;
		_listenKey = null;
		_nextListenKeyExtension = default;
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
			_spotWireSymbols.Clear();
			_swapWireSymbols.Clear();
			_orderInstruments.Clear();
			_seenFillIds.Clear();
			_instrumentMapsLoaded = false;
		}
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnPublicWebSocketStateAsync(DeepcoinProductTypes productType,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		_ = productType;
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask OnPrivateWebSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(state, cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null, 100,
				cancellationToken);
	}
}
