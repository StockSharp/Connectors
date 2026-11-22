namespace StockSharp.CoinJar;

using System.Net.WebSockets;

using Nito.AsyncEx;

public partial class CoinJarMessageAdapter
{
	private readonly AsyncLock _socketSync = new();
	private volatile bool _pollPublicMarketData;
	private int _publicPollInProgress;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		_restClient = new(TradingEndpoint, DataEndpoint, Token) { Parent = this };
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			RegisterProducts(await RestClient.GetProductsAsync(cancellationToken));
			if (RestClient.IsCredentialsAvailable)
				await EnsureSocketAsync(cancellationToken);
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

	private async ValueTask EnsureSocketAsync(CancellationToken cancellationToken)
	{
		using var scope = await _socketSync.LockAsync(cancellationToken);
		EnsureConnected();
		if (_socketClient is not null || _pollPublicMarketData)
			return;

		var isPublic = !RestClient.IsCredentialsAvailable;
		using var timeout = isPublic
			? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
			: null;
		timeout?.CancelAfter(TimeSpan.FromSeconds(8));
		var socket = CreateSocketClient();
		try
		{
			await socket.ConnectAsync(timeout?.Token ?? cancellationToken);
			EnsureConnected();
			socket.Error += OnSocketErrorAsync;
			socket.StateChanged += OnSocketStateAsync;
			_socketClient = socket;
		}
		catch (Exception error) when (isPublic && !cancellationToken.IsCancellationRequested &&
			(error is WebSocketException || error is OperationCanceledException && timeout.IsCancellationRequested))
		{
			socket.Dispose();
			_pollPublicMarketData = true;
			this.AddWarningLog("CoinJar WebSocket connection failed. Polling public market data via REST. {0}", error.Message);
		}
		catch
		{
			socket.Dispose();
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

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		if (!_pollPublicMarketData || Interlocked.Exchange(ref _publicPollInProgress, 1) != 0)
			return;
		try
		{
			await PollPublicMarketDataAsync(cancellationToken);
		}
		finally
		{
			Volatile.Write(ref _publicPollInProgress, 0);
		}
	}

	private CoinJarSocketClient CreateSocketClient()
	{
		var client = new CoinJarSocketClient(WebSocketEndpoint, Token,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.TickerReceived += OnSocketTickerAsync;
		client.BookReceived += OnSocketBookAsync;
		client.TradesReceived += OnSocketTradesAsync;
		client.OrderReceived += OnSocketOrderAsync;
		client.FillReceived += OnSocketFillAsync;
		client.AccountReceived += OnSocketAccountAsync;
		return client;
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Reconnecting)
		{
			using (_sync.EnterScope())
			{
				foreach (var book in _books.Values)
				{
					book.IsInitialized = false;
					book.IsSnapshotRequested = false;
					book.Bids.Clear();
					book.Asks.Clear();
				}

				foreach (var subscription in _depthSubscriptions.Values)
				{
					subscription.Bids.Clear();
					subscription.Asks.Clear();
					subscription.IsInitialized = false;
				}
			}
			return;
		}
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(state, cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;

		long[] portfolios;
		(long Id, OrderSubscription Subscription)[] orders;
		using (_sync.EnterScope())
		{
			portfolios = [.. _portfolioSubscriptions];
			orders = [.. _orderSubscriptions.Select(static pair =>
				(pair.Key, pair.Value))];
		}

		foreach (var transactionId in portfolios)
			await SendPortfolioSnapshotAsync(transactionId, true,
				cancellationToken);

		foreach (var item in orders)
			await SendOrderSnapshotAsync(item.Id, item.Subscription, null, null,
				1000, true, cancellationToken);

		await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		using var scope = await _socketSync.LockAsync();
		var socketClient = _socketClient;
		_socketClient = null;
		if (socketClient is not null)
		{
			try
			{
				await socketClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			socketClient.Dispose();
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
