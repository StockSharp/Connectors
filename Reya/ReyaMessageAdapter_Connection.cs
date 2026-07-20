namespace StockSharp.Reya;

public partial class ReyaMessageAdapter
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
			RestEndpoint = NormalizeEndpoint(RestEndpoint, false,
				nameof(RestEndpoint));
			WebSocketEndpoint = NormalizeEndpoint(WebSocketEndpoint, true,
				nameof(WebSocketEndpoint));
			_restClient = new ReyaRestClient(RestEndpoint) { Parent = this };
			await RefreshReferenceDataAsync(cancellationToken);
			await ConfigureAccountAsync(cancellationToken);
			_socket = CreateSocket();
			await Socket.ConnectAsync(cancellationToken);
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

	private async ValueTask ConfigureAccountAsync(
		CancellationToken cancellationToken)
	{
		if (!PrivateKey.IsEmpty())
			_signer = new ReyaSigner(PrivateKey.UnSecure(), ChainId,
				OrdersGatewayAddress.ThrowIfEmpty(nameof(OrdersGatewayAddress)));

		_ownerAddress = WalletAddress?.Trim();
		if (_ownerAddress.IsEmpty() && _signer is not null)
			_ownerAddress = _signer.Address;
		if (!_ownerAddress.IsEmpty())
		{
			_ownerAddress = NormalizeAddress(_ownerAddress,
				nameof(WalletAddress));
			_portfolioName = "Reya_" + _ownerAddress;
		}

		if (!AccountId.IsEmpty())
		{
			var configured = AccountId.ParseReyaInteger("account ID");
			if (configured <= 0)
				throw new ArgumentOutOfRangeException(nameof(AccountId), AccountId,
					"Reya account ID must be positive.");
			_configuredAccountId = configured;
		}

		if (_ownerAddress.IsEmpty())
			return;

		var accounts = await RestClient.GetAccountsAsync(_ownerAddress,
			cancellationToken);
		using (_sync.EnterScope())
		{
			_accounts.Clear();
			foreach (var account in accounts.Where(static account =>
				account is not null && account.AccountId > 0))
				_accounts.TryAdd(account.Type, account.AccountId);
		}
	}

	private ReyaSocketClient CreateSocket()
	{
		var socket = new ReyaSocketClient(WebSocketEndpoint,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		socket.PerpetualSummaryReceived += OnPerpetualSummaryAsync;
		socket.SpotSummaryReceived += OnSpotSummaryAsync;
		socket.PriceReceived += OnPriceAsync;
		socket.DepthReceived += OnDepthAsync;
		socket.PerpetualExecutionsReceived += OnPerpetualExecutionsAsync;
		socket.SpotExecutionsReceived += OnSpotExecutionsAsync;
		socket.PositionsReceived += OnPositionsAsync;
		socket.OrdersReceived += OnOrdersAsync;
		socket.BalancesReceived += OnBalancesAsync;
		socket.ServerTimeReceived += OnServerTimeAsync;
		socket.Error += OnWebSocketErrorAsync;
		socket.StateChanged += OnWebSocketStateAsync;
		return socket;
	}

	private async ValueTask RefreshReferenceDataAsync(
		CancellationToken cancellationToken)
	{
		var perpetualTask = RestClient.GetPerpetualMarketsAsync(cancellationToken)
			.AsTask();
		var spotTask = RestClient.GetSpotMarketsAsync(cancellationToken).AsTask();
		var perpetualSummaryTask = RestClient.GetPerpetualSummariesAsync(
			cancellationToken).AsTask();
		var spotSummaryTask = RestClient.GetSpotSummariesAsync(cancellationToken)
			.AsTask();
		var priceTask = RestClient.GetPricesAsync(cancellationToken).AsTask();
		await Task.WhenAll(perpetualTask, spotTask, perpetualSummaryTask,
			spotSummaryTask, priceTask);

		var perpetualDefinitions = await perpetualTask;
		var spotDefinitions = await spotTask;
		var perpetualSummaries = await perpetualSummaryTask;
		var spotSummaries = await spotSummaryTask;
		var prices = await priceTask;
		var markets = perpetualDefinitions
			.Where(static value => value is not null)
			.Select(static value => value.ToMarket())
			.Concat(spotDefinitions
				.Where(static value => value is not null)
				.Select(static value => value.ToMarket()))
			.OrderBy(static market => market.Symbol, StringComparer.Ordinal)
			.ToArray();
		if (markets.Length == 0)
			throw new InvalidDataException("Reya returned no usable markets.");

		using (_sync.EnterScope())
		{
			_markets.Clear();
			_prices.Clear();
			foreach (var market in markets)
			{
				if (!_markets.TryAdd(market.Symbol, market))
					throw new InvalidDataException(
						"Reya returned duplicate symbol '" + market.Symbol + "'.");
				_prices.Add(market.Symbol, new());
			}
			foreach (var summary in perpetualSummaries)
				ApplySummaryUnsafe(summary);
			foreach (var summary in spotSummaries)
				ApplySummaryUnsafe(summary);
			foreach (var price in prices)
				ApplyPriceUnsafe(price);
		}
	}

	private void ApplySummaryUnsafe(ReyaMarketSummary summary)
	{
		if (summary?.Symbol.IsEmpty() != false ||
			!_prices.TryGetValue(summary.Symbol, out var state))
			return;
		state.OraclePrice = summary.OraclePrice.TryParseReyaDecimal();
		state.PoolPrice = summary.PoolPrice.TryParseReyaDecimal();
		state.Volume24Hours = summary.Volume24Hours.TryParseReyaDecimal();
		state.PriceChange24Hours =
			summary.PriceChange24Hours.TryParseReyaDecimal();
		state.OpenInterest = summary.OpenInterest.TryParseReyaDecimal();
		state.FundingRate = summary.FundingRate.TryParseReyaDecimal();
		state.UpdatedAt = (summary.PricesUpdatedAt ?? summary.UpdatedAt) > 0
			? (summary.PricesUpdatedAt ?? summary.UpdatedAt).FromReyaMilliseconds()
			: state.UpdatedAt;
	}

	private void ApplySummaryUnsafe(ReyaSpotMarketSummary summary)
	{
		if (summary?.Symbol.IsEmpty() != false ||
			!_prices.TryGetValue(summary.Symbol, out var state))
			return;
		state.OraclePrice = summary.OraclePrice.TryParseReyaDecimal();
		state.Volume24Hours = summary.Volume24Hours.TryParseReyaDecimal();
		state.PriceChange24Hours =
			summary.PriceChange24Hours.TryParseReyaDecimal();
		state.UpdatedAt = summary.UpdatedAt > 0
			? summary.UpdatedAt.FromReyaMilliseconds()
			: state.UpdatedAt;
	}

	private void ApplyPriceUnsafe(ReyaPrice price)
	{
		if (price?.Symbol.IsEmpty() != false ||
			!_prices.TryGetValue(price.Symbol, out var state))
			return;
		state.OraclePrice = price.OraclePrice.TryParseReyaDecimal();
		state.PoolPrice = price.PoolPrice.TryParseReyaDecimal();
		state.UpdatedAt = price.UpdatedAt > 0
			? price.UpdatedAt.FromReyaMilliseconds()
			: state.UpdatedAt;
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socket;
		var rest = _restClient;
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
		rest?.Dispose();
		ClearState();
	}

	private ValueTask OnServerTimeAsync(long timestamp,
		CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		UpdateServerTime(timestamp);
		return default;
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

		await RefreshReferenceDataAsync(cancellationToken);
		DepthSubscription[] depths;
		using (_sync.EnterScope())
			depths = [.. _depthSubscriptions.Values];
		foreach (var subscription in depths)
			await SendDepthSnapshotAsync(subscription.Symbol,
				subscription.TransactionId, subscription.Depth, cancellationToken);
		long[] portfolios;
		KeyValuePair<long, OrderStatusSubscription>[] orders;
		using (_sync.EnterScope())
		{
			portfolios = [.. _portfolioSubscriptions];
			orders = [.. _orderSubscriptions];
		}
		foreach (var transactionId in portfolios)
			await SendPortfolioSnapshotAsync(transactionId, cancellationToken);
		foreach (var (transactionId, subscription) in orders)
			await SendOrderSnapshotAsync(subscription, transactionId,
				cancellationToken);
	}

	private static string NormalizeAddress(string address, string parameterName)
	{
		address = address.ThrowIfEmpty(parameterName).Trim();
		if (address.Length != 42 ||
			!address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			address.Skip(2).Any(static value => !value.IsDigit() &&
				value is not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F')))
			throw new ArgumentException(
				"Reya wallet address must be a 20-byte hexadecimal EVM address.",
				parameterName);
		return address.ToLowerInvariant();
	}
}
