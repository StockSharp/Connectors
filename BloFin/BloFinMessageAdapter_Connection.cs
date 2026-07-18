namespace StockSharp.BloFin;

public partial class BloFinMessageAdapter
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
		_portfolioName = GetPortfolioName(Key);
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			_publicWsClient = CreatePublicClient();
			await _publicWsClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				_privateWsClient = CreatePrivateClient();
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
		if (_publicWsClient is not null)
			await _publicWsClient.PingAsync(cancellationToken);
		if (_privateWsClient is not null)
			await _privateWsClient.PingAsync(cancellationToken);
	}

	private BloFinWsClient CreatePublicClient()
	{
		var client = new BloFinWsClient(PublicWsEndpoint, false, null, null, null,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.TickerReceived += OnTickerAsync;
		client.BookReceived += OnBookAsync;
		client.TradeReceived += OnTradeAsync;
		client.CandleReceived += OnCandleAsync;
		client.FundingRateReceived += OnFundingRateAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPublicWebSocketStateAsync;
		return client;
	}

	private BloFinWsClient CreatePrivateClient()
	{
		var client = new BloFinWsClient(PrivateWsEndpoint, true, Key, Secret, Passphrase,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.OrderReceived += OnOrderAsync;
		client.PositionReceived += OnPositionAsync;
		client.AccountReceived += OnAccountAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPrivateWebSocketStateAsync;
		return client;
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var publicClient = _publicWsClient;
		var privateClient = _privateWsClient;
		_publicWsClient = null;
		_privateWsClient = null;

		foreach (var client in new[] { privateClient, publicClient }.Where(static item => item is not null))
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
			_seenFillIds.Clear();
		}
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnPublicWebSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
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
