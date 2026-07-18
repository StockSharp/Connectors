namespace StockSharp.Bitrue;

public partial class BitrueMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_spotRestClient is not null || _futuresRestClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			if (IsSectionEnabled(BitrueSections.Spot))
			{
				_spotRestClient = new(SpotRestEndpoint, SpotStreamRestEndpoint, Key, Secret)
				{
					Parent = this,
				};
				await _spotRestClient.SynchronizeTimeAsync(cancellationToken);
				_spotPublicWsClient = CreatePublicClient(BitrueSections.Spot,
					SpotPublicWsEndpoint);
				await _spotPublicWsClient.ConnectAsync(cancellationToken);

				if (_spotRestClient.IsCredentialsAvailable)
				{
					var response = await _spotRestClient.AcquireListenKeyAsync(cancellationToken);
					_spotListenKey = ValidateListenKey(response, BitrueSections.Spot);
					_nextSpotListenKeyExtension = DateTime.UtcNow.AddMinutes(30);
					_spotPrivateWsClient = CreatePrivateClient(BitrueSections.Spot,
						SpotPrivateWsEndpoint, _spotListenKey);
					await _spotPrivateWsClient.ConnectAsync(cancellationToken);
				}
			}

			if (IsSectionEnabled(BitrueSections.Futures))
			{
				_futuresRestClient = new(FuturesRestEndpoint, FuturesStreamRestEndpoint,
					Key, Secret) { Parent = this };
				await _futuresRestClient.SynchronizeTimeAsync(cancellationToken);
				_futuresPublicWsClient = CreatePublicClient(BitrueSections.Futures,
					FuturesPublicWsEndpoint);
				await _futuresPublicWsClient.ConnectAsync(cancellationToken);

				if (_futuresRestClient.IsCredentialsAvailable)
				{
					var response = await _futuresRestClient.AcquireListenKeyAsync(cancellationToken);
					_futuresListenKey = ValidateListenKey(response, BitrueSections.Futures);
					_nextFuturesListenKeyExtension = DateTime.UtcNow.AddMinutes(20);
					_futuresPrivateWsClient = CreatePrivateClient(BitrueSections.Futures,
						FuturesPrivateWsEndpoint, _futuresListenKey);
					await _futuresPrivateWsClient.ConnectAsync(cancellationToken);
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
		var now = DateTime.UtcNow;
		if (_spotRestClient is not null && now - _lastPollingTime >= PollingInterval)
		{
			_lastPollingTime = now;
			await PollSpotMarketDataAsync(cancellationToken);
		}

		if (_spotPrivateWsClient is not null && now >= _nextSpotListenKeyExtension)
		{
			var response = await SpotRestClient.ExtendListenKeyAsync(_spotListenKey,
				cancellationToken);
			ValidateListenKeyExtension(response, BitrueSections.Spot);
			_nextSpotListenKeyExtension = now.AddMinutes(30);
		}
		if (_futuresPrivateWsClient is not null && now >= _nextFuturesListenKeyExtension)
		{
			var response = await FuturesRestClient.ExtendListenKeyAsync(_futuresListenKey,
				cancellationToken);
			ValidateListenKeyExtension(response, BitrueSections.Futures);
			_nextFuturesListenKeyExtension = now.AddMinutes(20);
		}
	}

	private BitruePublicWsClient CreatePublicClient(BitrueSections section, string endpoint)
	{
		var client = new BitruePublicWsClient(endpoint, section,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.BookReceived += OnBookAsync;
		client.TickerReceived += OnFuturesTickerAsync;
		client.TradeReceived += OnFuturesTradeAsync;
		client.CandleReceived += OnFuturesCandleAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPublicWebSocketStateAsync;
		return client;
	}

	private BitruePrivateWsClient CreatePrivateClient(BitrueSections section, string endpoint,
		string listenKey)
	{
		var client = new BitruePrivateWsClient(endpoint, listenKey, section,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.SpotOrderReceived += OnSpotPrivateOrderAsync;
		client.SpotBalanceReceived += OnSpotPrivateBalanceAsync;
		client.FuturesOrderReceived += OnFuturesPrivateOrderAsync;
		client.FuturesAccountReceived += OnFuturesPrivateAccountAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPrivateWebSocketStateAsync;
		return client;
	}

	private BitruePublicWsClient GetPublicClient(BitrueSections section)
		=> section == BitrueSections.Spot
			? _spotPublicWsClient ?? throw new InvalidOperationException(
				"The Bitrue spot WebSocket is not connected.")
			: _futuresPublicWsClient ?? throw new InvalidOperationException(
				"The Bitrue futures WebSocket is not connected.");

	private BitruePrivateWsClient GetPrivateClient(BitrueSections section)
		=> section == BitrueSections.Spot
			? _spotPrivateWsClient ?? throw new InvalidOperationException(
				"The Bitrue spot private WebSocket is not connected.")
			: _futuresPrivateWsClient ?? throw new InvalidOperationException(
				"The Bitrue futures private WebSocket is not connected.");

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var clients = new BaseLogReceiver[]
		{
			_spotPrivateWsClient,
			_futuresPrivateWsClient,
			_spotPublicWsClient,
			_futuresPublicWsClient,
		};
		_spotPrivateWsClient = null;
		_futuresPrivateWsClient = null;
		_spotPublicWsClient = null;
		_futuresPublicWsClient = null;

		foreach (var client in clients.Where(static item => item is not null))
		{
			try
			{
				switch (client)
				{
					case BitruePrivateWsClient privateWs:
						await privateWs.DisconnectAsync(cancellationToken);
						break;
					case BitruePublicWsClient publicWs:
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

		await CloseListenKeysAsync(cancellationToken);
		_spotRestClient?.Dispose();
		_futuresRestClient?.Dispose();
		_spotRestClient = null;
		_futuresRestClient = null;
		_spotListenKey = null;
		_futuresListenKey = null;
		_nextSpotListenKeyExtension = default;
		_nextFuturesListenKeyExtension = default;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		ClearState();
	}

	private async ValueTask CloseListenKeysAsync(CancellationToken cancellationToken)
	{
		if (_spotRestClient is not null && !_spotListenKey.IsEmpty())
		{
			try
			{
				await _spotRestClient.CloseListenKeyAsync(_spotListenKey, cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
		if (_futuresRestClient is not null && !_futuresListenKey.IsEmpty())
		{
			try
			{
				await _futuresRestClient.CloseListenKeyAsync(_futuresListenKey,
					cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
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
			_spotSymbols.Clear();
			_futuresContracts.Clear();
			_futuresPrivateSymbols.Clear();
			_spotOrderSymbols.Clear();
			_futuresOrderSymbols.Clear();
			_spotLastTradeIds.Clear();
			_seenFillIds.Clear();
			_instrumentsLoaded = false;
		}
		_lastPollingTime = default;
	}

	private static string ValidateListenKey(BitrueListenKeyResponse response,
		BitrueSections section)
	{
		if (response is null || response.Code != 200)
			throw new InvalidOperationException(
				$"Bitrue {section} did not create a listen key ({response?.Code}): {response?.Message}".Trim());
		return response.Data?.Value.ThrowIfEmpty($"Bitrue {section} listen key");
	}

	private static void ValidateListenKeyExtension(BitrueListenKeyResponse response,
		BitrueSections section)
	{
		if (response is null || response.Code != 200)
			throw new InvalidOperationException(
				$"Bitrue {section} did not extend its listen key ({response?.Code}): {response?.Message}".Trim());
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnPublicWebSocketStateAsync(BitrueSections section,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		_ = section;
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask OnPrivateWebSocketStateAsync(BitrueSections section,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		_ = section;
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
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null, null,
				100, cancellationToken);
	}
}
