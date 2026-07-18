namespace StockSharp.Bullish;

public partial class BullishMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		_restClient = new(RestEndpoint, Key, Secret, RateLimitToken) { Parent = this };
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			await RefreshMarketsAsync(cancellationToken);
			if (RestClient.IsCredentialsAvailable)
				await RefreshAccountsAsync(cancellationToken);

			_bookClient = CreatePublicClient(BullishWsKinds.OrderBook);
			_tradeClient = CreatePublicClient(BullishWsKinds.Trades);
			_tickClient = CreatePublicClient(BullishWsKinds.Tick);
			await _bookClient.ConnectAsync(cancellationToken);
			await _tradeClient.ConnectAsync(cancellationToken);
			await _tickClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				_privateClient = CreatePrivateClient();
				await _privateClient.ConnectAsync(cancellationToken);
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

	private BullishWsClient CreatePublicClient(BullishWsKinds kind)
	{
		var client = new BullishWsClient(WsEndpoint, kind, RestClient,
			ReConnectionSettings.WorkingTime) { Parent = this };
		switch (kind)
		{
			case BullishWsKinds.OrderBook:
				client.DepthReceived += OnDepthAsync;
				break;
			case BullishWsKinds.Trades:
				client.TradesReceived += OnTradesAsync;
				break;
			case BullishWsKinds.Tick:
				client.TickReceived += OnTickAsync;
				break;
		}
		client.Error += OnWebSocketErrorAsync;
		return client;
	}

	private BullishWsClient CreatePrivateClient()
	{
		string[] accountIds;
		using (_sync.EnterScope())
			accountIds = [.. _accounts.Keys];
		var client = new BullishWsClient(WsEndpoint, BullishWsKinds.Private, RestClient,
			ReConnectionSettings.WorkingTime, accountIds) { Parent = this };
		client.OrderReceived += OnOrderAsync;
		client.FillReceived += OnFillAsync;
		client.AssetAccountReceived += OnAssetAccountAsync;
		client.TradingAccountReceived += OnTradingAccountAsync;
		client.PositionReceived += OnPositionAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPrivateStateChangedAsync;
		return client;
	}

	private BullishWsClient[] GetClients()
		=> new[] { _bookClient, _tradeClient, _tickClient, _privateClient }
			.Where(static client => client is not null).ToArray();

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var clients = GetClients();
		_bookClient = null;
		_tradeClient = null;
		_tickClient = null;
		_privateClient = null;

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
		_defaultTradingAccountId = null;
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
			_markets.Clear();
			_books.Clear();
			_accounts.Clear();
		}
	}

	private async ValueTask RefreshAccountsAsync(CancellationToken cancellationToken)
	{
		var accounts = await RestClient.GetTradingAccountsAsync(cancellationToken) ?? [];
		if (accounts.Length == 0)
			throw new InvalidDataException("Bullish returned no trading accounts for this API key.");

		var requested = TradingAccountId?.Trim();
		var selected = requested.IsEmpty()
			? accounts.FirstOrDefault(static account => account.IsPrimaryAccount) ?? accounts[0]
			: accounts.FirstOrDefault(account => account.TradingAccountId.EqualsIgnoreCase(requested));
		if (selected is null)
			throw new InvalidOperationException($"Bullish trading account '{requested}' is not available.");

		using (_sync.EnterScope())
		{
			_accounts.Clear();
			foreach (var account in accounts.Where(static account =>
				account?.TradingAccountId.IsEmpty() == false))
				_accounts[account.TradingAccountId] = account;
		}
		_defaultTradingAccountId = selected.TradingAccountId;
		RestClient.SetRateLimitToken(selected.RateLimitToken);
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnPrivateStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is not (ConnectionStates.Connected or ConnectionStates.Restored))
			return;
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null,
				DateTime.UtcNow, cancellationToken);
	}
}
