namespace StockSharp.QFEX;

using Native;

public partial class QFEXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _marketSocket is not null ||
			_tradeSocket is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() != Secret.IsEmpty())
			throw new InvalidOperationException(
				"QFEX API key and secret must be configured together.");
		if (!AccountId.IsEmpty() && Key.IsEmpty())
			throw new InvalidOperationException(
				"QFEX account ID requires API credentials.");
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret, AccountId)
			{
				Parent = this,
			};
			await RefreshMarketsAsync(cancellationToken);

			_marketSocket = new(MarketSocketEndpoint,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_marketSocket.DataReceived += OnMarketDataAsync;
			_marketSocket.Error += OnWebSocketErrorAsync;
			_marketSocket.StateChanged += OnMarketSocketStateAsync;
			await _marketSocket.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				_portfolioName = CreatePortfolioName(Key,
					RestClient.AccountId);
				_tradeSocket = new(TradeSocketEndpoint, Key, Secret, AccountId,
					ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				_tradeSocket.OrderReceived += OnOrderAsync;
				_tradeSocket.FillReceived += OnFillAsync;
				_tradeSocket.BalanceReceived += OnBalanceAsync;
				_tradeSocket.PositionReceived += OnPositionAsync;
				_tradeSocket.Error += OnWebSocketErrorAsync;
				_tradeSocket.StateChanged += OnTradeSocketStateAsync;
				await _tradeSocket.ConnectAsync(cancellationToken);
				await TradeSocket.SubscribeAsync(
					QFEXTradeChannels.OrderResponses, cancellationToken);
				await TradeSocket.SubscribeAsync(QFEXTradeChannels.Fills,
					cancellationToken);
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

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var markets = (await RestClient.GetReferenceDataAsync(null,
			cancellationToken) ?? [])
			.Where(IsValidMarket)
			.GroupBy(static market => market.Symbol.Trim(),
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.Last())
			.OrderBy(static market => market.Symbol,
				StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (markets.Length == 0)
			throw new InvalidDataException(
				"QFEX returned no usable perpetual markets.");
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets)
				_markets.Add(market.Symbol.Trim(), market);
		}
	}

	private static bool IsValidMarket(QFEXReferenceDataSymbol market)
		=> market?.Symbol.IsEmpty() == false &&
			market.Status != QFEXSymbolStatuses.Delisted &&
			market.TickSize.TryParseDecimal() is > 0 &&
			market.LotSize.TryParseDecimal() is > 0 &&
			market.MinimumQuantity.TryParseDecimal() is >= 0 &&
			market.MaximumQuantity.TryParseDecimal() is > 0;

	private static string CreatePortfolioName(string publicKey,
		string accountId)
	{
		var value = accountId.IsEmpty()
			? publicKey.ThrowIfEmpty(nameof(publicKey)).Trim()
			: accountId.Trim();
		var suffix = new string(value.Where(char.IsLetterOrDigit).Take(16)
			.ToArray());
		if (suffix.IsEmpty())
			throw new InvalidOperationException(
				"QFEX credentials do not contain a usable portfolio identifier.");
		return "QFEX_" + suffix.ToUpperInvariant();
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var tradeSocket = _tradeSocket;
		var marketSocket = _marketSocket;
		var restClient = _restClient;
		_tradeSocket = null;
		_marketSocket = null;
		_restClient = null;
		foreach (var socket in new Disposable[] { tradeSocket, marketSocket }
			.Where(static socket => socket is not null))
		{
			try
			{
				switch (socket)
				{
					case QFEXTradeWebSocketClient trade:
						await trade.DisconnectAsync(cancellationToken);
						break;
					case QFEXMarketDataWebSocketClient market:
						await market.DisconnectAsync(cancellationToken);
						break;
				}
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			socket.Dispose();
		}
		restClient?.Dispose();
		ClearState();
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnMarketSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
		if (state == ConnectionStates.Restored)
			await RefreshMarketsAsync(cancellationToken);
	}

	private async ValueTask OnTradeSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
		if (state != ConnectionStates.Restored)
			return;
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(new OrderStatusMessage
			{
				TransactionId = _orderStatusSubscriptionId,
				IsSubscribe = true,
				PortfolioName = _portfolioName,
				Count = HistoryLimit,
			}, cancellationToken);
	}
}
