namespace StockSharp.WooX;

public partial class WooXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		_restClient = new(RestEndpoint, HistoricalRestEndpoint, Key, Secret) { Parent = this };
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			if (RestClient.IsCredentialsAvailable && ApplicationId.IsEmpty())
				throw new InvalidOperationException(
					"WOO X application ID is required when API credentials are configured.");

			var symbols = await RestClient.GetSymbolsAsync(cancellationToken);
			RegisterSymbols(symbols.Rows);
			_symbolsLoaded = true;

			_publicApplicationId = ApplicationId.IsEmpty()
				? Guid.NewGuid().ToString("D")
				: ApplicationId.Trim();
			_publicWsClient = CreatePublicClient(_publicApplicationId);
			await _publicWsClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				_privateWsClient = CreatePrivateClient(ApplicationId.Trim());
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

	private WooXPublicWsClient CreatePublicClient(string applicationId)
	{
		var client = new WooXPublicWsClient(PublicWsEndpoint, applicationId,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.TickerReceived += OnTickerAsync;
		client.BestBidOfferReceived += OnBestBidOfferAsync;
		client.BookReceived += OnBookAsync;
		client.TradeReceived += OnTradeAsync;
		client.CandleReceived += OnCandleAsync;
		client.ReferencePriceReceived += OnReferencePriceAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPublicWebSocketStateAsync;
		return client;
	}

	private WooXPrivateWsClient CreatePrivateClient(string applicationId)
	{
		var client = new WooXPrivateWsClient(PrivateWsEndpoint, applicationId, RestClient,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.BalanceReceived += OnPrivateBalanceAsync;
		client.ExecutionReceived += OnPrivateExecutionAsync;
		client.PositionReceived += OnPrivatePositionAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnPrivateWebSocketStateAsync;
		return client;
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var privateClient = _privateWsClient;
		var publicClient = _publicWsClient;
		_privateWsClient = null;
		_publicWsClient = null;

		if (privateClient is not null)
		{
			try
			{
				await privateClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			privateClient.Dispose();
		}
		if (publicClient is not null)
		{
			try
			{
				await publicClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			publicClient.Dispose();
		}

		_restClient?.Dispose();
		_restClient = null;
		_publicApplicationId = null;
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
			_symbols.Clear();
			_seenFillIds.Clear();
			_symbolsLoaded = false;
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
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null, 500,
				cancellationToken);
	}

	private void RegisterSymbols(IEnumerable<WooXSymbol> symbols)
	{
		using (_sync.EnterScope())
			foreach (var symbol in symbols ?? [])
				if (symbol?.Symbol.IsEmpty() == false)
					_symbols[symbol.Symbol] = symbol;
	}
}
