namespace StockSharp.OrderlyNetwork;

public partial class OrderlyNetworkMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _signer is not null ||
			_publicSocket is not null || _privateSocket is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(AccountId, Secret);
			AccountId = _signer.AccountId;
			_restClient = new(RestEndpoint, _signer) { Parent = this };

			var marketsTask = RestClient.GetMarketsAsync(cancellationToken).AsTask();
			var futuresTask = RestClient.GetFuturesAsync(cancellationToken).AsTask();
			await Task.WhenAll(marketsTask, futuresTask);
			RegisterMarkets(marketsTask.Result, futuresTask.Result);
			UpdateServerTime(RestClient.ServerTime);

			if (_signer.IsAccountAvailable)
			{
				_publicSocket = CreatePublicSocket();
				await _publicSocket.ConnectAsync(cancellationToken);
				if (_signer.IsSigningAvailable)
				{
					_privateSocket = CreatePrivateSocket();
					await _privateSocket.ConnectAsync(cancellationToken);
				}
			}
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

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		if (_publicSocket is not null)
			await _publicSocket.PingAsync(cancellationToken);
		if (_privateSocket is not null)
			await _privateSocket.PingAsync(cancellationToken);
	}

	private OrderlyNetworkSocketClient CreatePublicSocket()
	{
		var socket = new OrderlyNetworkSocketClient(PublicWebSocketEndpoint,
			_signer.AccountId, _signer, () => ServerTime, false,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		socket.BboReceived += OnBboAsync;
		socket.TickerReceived += OnTickerAsync;
		socket.TradeReceived += OnPublicTradeAsync;
		socket.CandleReceived += OnCandleAsync;
		socket.DepthReceived += OnDepthAsync;
		socket.ServerTimeReceived += OnSocketServerTimeAsync;
		socket.Error += OnSocketErrorAsync;
		socket.StateChanged += OnPublicSocketStateAsync;
		return socket;
	}

	private OrderlyNetworkSocketClient CreatePrivateSocket()
	{
		var socket = new OrderlyNetworkSocketClient(PrivateWebSocketEndpoint,
			_signer.AccountId, _signer, () => ServerTime, true,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		socket.BalancesReceived += OnBalancesAsync;
		socket.PositionsReceived += OnPositionsAsync;
		socket.ExecutionReceived += OnExecutionAsync;
		socket.ServerTimeReceived += OnSocketServerTimeAsync;
		socket.Error += OnSocketErrorAsync;
		socket.StateChanged += OnPrivateSocketStateAsync;
		return socket;
	}

	private ValueTask OnSocketServerTimeAsync(long timestamp,
		CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		UpdateServerTime(timestamp);
		return default;
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnPublicSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(state, cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;
		await ResynchronizeAllDepthsAsync(cancellationToken);
		await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask OnPrivateSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(state, cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;
		long[] portfolios;
		long[] orders;
		using (_sync.EnterScope())
		{
			portfolios = [.. _portfolioSubscriptions];
			orders = [.. _orderSubscriptions.Keys];
		}
		foreach (var subscriptionId in portfolios)
			await SendPortfolioSnapshotAsync(subscriptionId, cancellationToken);
		foreach (var subscriptionId in orders)
			await SendOrderSnapshotAsync(subscriptionId, null, null, null,
				HistoryLimit, cancellationToken);
		await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private void RegisterMarkets(OrderlyNetworkSymbolInfo[] markets,
		OrderlyNetworkFuture[] futures)
	{
		if (markets is not { Length: > 0 })
			throw new InvalidDataException(
				"Orderly Network returned no available markets.");
		using (_sync.EnterScope())
		{
			foreach (var market in markets)
				if (market?.Symbol.IsEmpty() == false)
					_markets[market.Symbol] = market;
			foreach (var future in futures ?? [])
				if (future?.Symbol.IsEmpty() == false)
					_futures[future.Symbol] = future;
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var privateSocket = _privateSocket;
		_privateSocket = null;
		if (privateSocket is not null)
		{
			try
			{
				await privateSocket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			privateSocket.Dispose();
		}

		var publicSocket = _publicSocket;
		_publicSocket = null;
		if (publicSocket is not null)
		{
			try
			{
				await publicSocket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			publicSocket.Dispose();
		}

		_restClient?.Dispose();
		_restClient = null;
		_signer?.Dispose();
		_signer = null;
		ClearState();
	}
}
