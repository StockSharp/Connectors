namespace StockSharp.CoinDCX;

public partial class CoinDCXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _socketClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() != Secret.IsEmpty())
			throw new InvalidOperationException(
				"CoinDCX API key and secret must be configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, PublicRestEndpoint, Key, Secret)
			{
				Parent = this,
			};
			var markets = await RestClient.GetMarketsAsync(cancellationToken);
			if (markets is not { Length: > 0 })
				throw new InvalidDataException(
					"CoinDCX returned no market definitions.");
			RegisterMarkets(markets);

			_socketClient = new(WebSocketEndpoint, RestClient,
				ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_socketClient.TradeReceived += OnSocketTradeAsync;
			_socketClient.DepthReceived += OnSocketDepthAsync;
			_socketClient.CandleReceived += OnSocketCandleAsync;
			_socketClient.BalancesReceived += OnPrivateBalancesAsync;
			_socketClient.OrdersReceived += OnPrivateOrdersAsync;
			_socketClient.AccountTradesReceived += OnPrivateTradesAsync;
			_socketClient.Error += OnSocketErrorAsync;
			_socketClient.StateChanged += OnSocketStateAsync;
			await _socketClient.ConnectAsync(cancellationToken);

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

	private async ValueTask OnSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;

		long[] portfolioSubscriptions;
		using (_sync.EnterScope())
			portfolioSubscriptions = [.. _portfolioSubscriptions];
		if (RestClient.IsCredentialsAvailable)
			foreach (var subscriptionId in portfolioSubscriptions)
				await SendPortfolioSnapshotAsync(subscriptionId, cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Restored,
			cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var socketClient = _socketClient;
		_socketClient = null;
		if (socketClient is not null)
		{
			try
			{
				await socketClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				socketClient.Dispose();
			}
		}
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void DisposeClients()
	{
		_socketClient?.Dispose();
		_socketClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
