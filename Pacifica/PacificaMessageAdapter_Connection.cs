namespace StockSharp.Pacifica;

using Native;

public partial class PacificaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _socket is not null || _signer is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_signer = new(WalletAddress, PrivateKey, AgentWallet,
				SignatureExpiryWindow);
			_restClient = new(RestEndpoint) { Parent = this };
			await RefreshMarketsAsync(cancellationToken);
			if (Signer.IsAccountAvailable)
				_portfolioName = CreatePortfolioName(Signer.Account);

			_socket = CreateSocket();
			await Socket.ConnectAsync(cancellationToken);
			if (Signer.IsSigningAvailable)
			{
				await Socket.SubscribeAsync(AccountStream(
					PacificaSources.AccountOrderUpdates), cancellationToken);
				await Socket.SubscribeAsync(AccountStream(
					PacificaSources.AccountTrades), cancellationToken);
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
		EnsureConnected();
		await Socket.PingAsync(cancellationToken);
	}

	private PacificaWebSocketClient CreateSocket()
	{
		var socket = new PacificaWebSocketClient(WebSocketEndpoint,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		socket.PricesReceived += OnPricesAsync;
		socket.BestBidOfferReceived += OnBestBidOfferAsync;
		socket.BookReceived += OnBookAsync;
		socket.TradesReceived += OnPublicTradesAsync;
		socket.CandleReceived += OnCandleAsync;
		socket.AccountInfoReceived += OnAccountInfoAsync;
		socket.PositionsReceived += OnPositionsAsync;
		socket.OrdersReceived += OnOrdersAsync;
		socket.AccountTradesReceived += OnAccountTradesAsync;
		socket.Error += OnWebSocketErrorAsync;
		socket.StateChanged += OnWebSocketStateAsync;
		return socket;
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var markets = (await RestClient.GetMarketsAsync(cancellationToken) ?? [])
			.Where(IsValidMarket)
			.GroupBy(static market => market.Symbol.Trim(), StringComparer.Ordinal)
			.Select(static group => group.Last())
			.OrderBy(static market => market.Symbol, StringComparer.Ordinal)
			.ToArray();
		if (markets.Length == 0)
			throw new InvalidDataException(
				"Pacifica returned no usable perpetual markets.");
		var prices = await RestClient.GetPricesAsync(cancellationToken) ?? [];
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets)
				_markets.Add(market.Symbol.Trim(), market);
			_prices.Clear();
			foreach (var price in prices)
				if (price?.Symbol.IsEmpty() == false &&
					_markets.ContainsKey(price.Symbol))
					_prices[price.Symbol] = price;
		}
		var serverTimestamp = prices.Select(static price => price?.Timestamp ?? 0)
			.DefaultIfEmpty().Max();
		if (serverTimestamp > 0)
			UpdateServerTime(serverTimestamp.ToPacificaTime());
	}

	private static bool IsValidMarket(PacificaMarket market)
		=> market?.Symbol.IsEmpty() == false &&
			market.InstrumentType == PacificaInstrumentTypes.Perpetual &&
			market.TickSize.TryParseDecimal() is > 0 &&
			market.LotSize.TryParseDecimal() is > 0 &&
			market.MinimumOrderNotional.TryParseDecimal() is >= 0 &&
			market.MaximumOrderNotional.TryParseDecimal() is > 0;

	private static string CreatePortfolioName(string account)
	{
		account = account.ThrowIfEmpty(nameof(account)).Trim();
		return "Pacifica_" + account[..account.Length.Min(12)];
	}

	private PacificaSubscriptionKey AccountStream(PacificaSources source)
		=> new(source, null, null, null, Signer.Account);

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socket;
		var restClient = _restClient;
		var signer = _signer;
		_socket = null;
		_restClient = null;
		_signer = null;
		if (socket is not null)
		{
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			socket.Dispose();
		}
		restClient?.Dispose();
		signer?.Dispose();
		ClearState();
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWebSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
		if (state != ConnectionStates.Restored)
			return;
		await RefreshMarketsAsync(cancellationToken);
		if (_portfolioSubscriptionId != 0 && Signer.IsAccountAvailable)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0 && Signer.IsAccountAvailable)
			await SendOrderSnapshotAsync(new OrderStatusMessage
			{
				TransactionId = _orderStatusSubscriptionId,
				IsSubscribe = true,
				PortfolioName = _portfolioName,
				Count = HistoryLimit,
			}, cancellationToken);
	}
}
