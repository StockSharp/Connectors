namespace StockSharp.Extended;

using Native;

public partial class ExtendedMessageAdapter
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
			var apiKey = Key.IsEmpty() ? null : Key.UnSecure().Trim();
			_restClient = new(RestEndpoint, apiKey) { Parent = this };
			await RefreshMarketsAsync(cancellationToken);

			if (!apiKey.IsEmpty())
			{
				_account = await RestClient.GetAccountAsync(cancellationToken) ??
					throw new InvalidDataException(
						"Extended returned no account information.");
				if (_account.Id <= 0)
					throw new InvalidDataException(
						"Extended returned an invalid account identifier.");
				_portfolioName = "Extended_" + _account.Id.ToString(
					CultureInfo.InvariantCulture);
				await RefreshFeesAsync(cancellationToken);
			}

			if (!PrivateKey.IsEmpty())
			{
				if (_account is null)
					throw new InvalidOperationException(
						"An Extended API key is required with the Stark private key.");
				if (_account.L2Vault is not uint vault || vault == 0 ||
					_account.L2Key.IsEmpty())
					throw new InvalidDataException(
						"Extended returned incomplete L2 signing information.");
				_signer = new(PrivateKey, _account.L2Key, vault,
					RestEndpoint.Contains("sepolia",
						StringComparison.OrdinalIgnoreCase));
			}

			_socket = CreateSocket(apiKey);
			await Socket.ConnectAsync(cancellationToken);
			if (_account is not null)
				await Socket.SubscribeAsync(AccountStream(), cancellationToken);

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

	private ExtendedWebSocketClient CreateSocket(string apiKey)
	{
		var socket = new ExtendedWebSocketClient(WebSocketEndpoint, apiKey,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		socket.OrderBookReceived += OnOrderBookAsync;
		socket.TradesReceived += OnPublicTradesAsync;
		socket.FundingRateReceived += OnFundingRateAsync;
		socket.PriceReceived += OnPriceAsync;
		socket.CandlesReceived += OnCandlesAsync;
		socket.PositionsReceived += OnPositionsAsync;
		socket.OrdersReceived += OnOrdersAsync;
		socket.AccountTradesReceived += OnAccountTradesAsync;
		socket.BalanceReceived += OnBalanceAsync;
		socket.SpotBalancesReceived += OnSpotBalancesAsync;
		socket.SequenceGap += OnSequenceGapAsync;
		socket.Error += OnWebSocketErrorAsync;
		socket.StateChanged += OnWebSocketStateAsync;
		return socket;
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var markets = (await RestClient.GetMarketsAsync(cancellationToken) ?? [])
			.Where(IsValidMarket)
			.GroupBy(static market => market.Name.Trim(), StringComparer.Ordinal)
			.Select(static group => group.Last())
			.OrderBy(static market => market.Name, StringComparer.Ordinal)
			.ToArray();
		if (markets.Length == 0)
			throw new InvalidDataException(
				"Extended returned no usable spot or perpetual markets.");

		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets)
			{
				var symbol = market.Name.Trim();
				_markets.Add(symbol, market);
				_prices[symbol] = CreatePriceState(market.Statistics);
			}
			foreach (var stale in _prices.Keys.Except(_markets.Keys,
				StringComparer.Ordinal).ToArray())
				_prices.Remove(stale);
		}
	}

	private static bool IsValidMarket(ExtendedMarket market)
		=> market?.Name.IsEmpty() == false && market.IsActive &&
			Enum.IsDefined(market.Type) && market.TradingConfig is not null &&
			market.L2Config is not null &&
			market.TradingConfig.MinimumOrderSize.TryParseExtendedDecimal() is > 0 &&
			market.TradingConfig.MinimumOrderSizeChange.TryParseExtendedDecimal() is > 0 &&
			market.TradingConfig.MinimumPriceChange.TryParseExtendedDecimal() is > 0 &&
			market.L2Config.CollateralResolution > 0 &&
			market.L2Config.SyntheticResolution > 0 &&
			!market.L2Config.CollateralId.IsEmpty() &&
			!market.L2Config.SyntheticId.IsEmpty();

	private static PriceState CreatePriceState(ExtendedMarketStats statistics)
		=> new()
		{
			MarkPrice = statistics?.MarkPrice.TryParseExtendedDecimal(),
			IndexPrice = statistics?.IndexPrice.TryParseExtendedDecimal(),
			LastPrice = statistics?.LastPrice.TryParseExtendedDecimal(),
			BestBidPrice = statistics?.BidPrice.TryParseExtendedDecimal(),
			BestAskPrice = statistics?.AskPrice.TryParseExtendedDecimal(),
		};

	private async ValueTask RefreshFeesAsync(CancellationToken cancellationToken)
	{
		var fees = await RestClient.GetFeesAsync(null, cancellationToken) ?? [];
		using (_sync.EnterScope())
		{
			_takerFees.Clear();
			foreach (var fee in fees)
				if (fee?.Market.IsEmpty() == false &&
					fee.TakerFeeRate.TryParseExtendedDecimal() is decimal rate &&
					rate is >= 0 and <= 1)
					_takerFees[fee.Market] = rate;
		}
	}

	private ExtendedSubscriptionKey AccountStream()
		=> new(ExtendedStreamScopes.Account, null, null, null,
			_account.Id.ToString(CultureInfo.InvariantCulture));

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
		if (_account is not null)
		{
			await RefreshFeesAsync(cancellationToken);
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

	private async ValueTask OnSequenceGapAsync(long previous, long current,
		CancellationToken cancellationToken)
	{
		this.AddWarningLog(
			"Extended WebSocket sequence gap {0}->{1}; refreshing snapshots.",
			previous, current);
		await RefreshMarketsAsync(cancellationToken);

		DepthSubscription[] depthSubscriptions;
		using (_sync.EnterScope())
			depthSubscriptions = [.. _depthSubscriptions.Values];
		foreach (var subscription in depthSubscriptions)
		{
			var snapshot = await RestClient.GetOrderBookAsync(subscription.Symbol,
				cancellationToken);
			if (snapshot is not null)
				await SendBookAsync(subscription.Symbol, snapshot,
					subscription.TransactionId, subscription.Depth, true,
					DateTime.UtcNow, cancellationToken);
		}

		if (_account is not null)
		{
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
}
