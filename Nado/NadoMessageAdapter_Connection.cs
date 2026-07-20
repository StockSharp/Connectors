namespace StockSharp.Nado;

using Native;

public partial class NadoMessageAdapter
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
			_restClient = new(GatewayEndpoint, GatewayV2Endpoint, ArchiveEndpoint,
				ArchiveV2Endpoint) { Parent = this };
			var status = await RestClient.GetStatusAsync(cancellationToken);
			if (!status.EqualsIgnoreCase("active"))
				throw new InvalidOperationException(
					"Nado gateway is not active (status: " + status + ").");
			_contracts = await RestClient.GetContractsAsync(cancellationToken) ??
				throw new InvalidDataException(
					"Nado returned no signing domain information.");
			if (!long.TryParse(_contracts.ChainId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var chainId) || chainId <= 0 ||
				_contracts.EndpointAddress.IsEmpty())
				throw new InvalidDataException(
					"Nado returned invalid signing domain information.");

			await RefreshMarketsAsync(cancellationToken);
			await ConfigureAccountAsync(cancellationToken);

			_socket = CreateSocket();
			await Socket.ConnectAsync(cancellationToken);
			if (!_subaccount.IsEmpty())
				foreach (var stream in AccountStreams())
					await Socket.SubscribeAsync(stream, cancellationToken);

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
		var configuredAddress = WalletAddress?.Trim();
		if (!PrivateKey.IsEmpty())
		{
			_signer = new(PrivateKey.UnSecure(), SubaccountName);
			if (!configuredAddress.IsEmpty() &&
				!configuredAddress.Equals(_signer.Address,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"The Nado wallet address does not match the private key.");
			_walletAddress = _signer.Address;
			_subaccount = _signer.Subaccount;
		}
		else if (!configuredAddress.IsEmpty())
		{
			_subaccount = NadoSigner.CreateSubaccountHex(configuredAddress,
				SubaccountName);
			_walletAddress = "0x" + _subaccount.Substring(2, 40);
		}

		if (_subaccount.IsEmpty())
			return;
		_portfolioName = "Nado_" + _walletAddress + "_" + SubaccountName;
		_ = await RestClient.GetSubaccountAsync(_subaccount, cancellationToken);
		_ = await RestClient.GetFeeRatesAsync(_subaccount, cancellationToken);
	}

	private NadoWebSocketClient CreateSocket()
	{
		var socket = new NadoWebSocketClient(WebSocketEndpoint,
			ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		socket.TradeReceived += OnTradeAsync;
		socket.BestBidOfferReceived += OnBestBidOfferAsync;
		socket.BookDepthReceived += OnBookDepthAsync;
		socket.CandleReceived += OnCandleAsync;
		socket.FundingRateReceived += OnFundingRateAsync;
		socket.FillReceived += OnFillAsync;
		socket.PositionChangeReceived += OnPositionChangeAsync;
		socket.OrderUpdateReceived += OnOrderUpdateAsync;
		socket.DepthGap += OnDepthGapAsync;
		socket.ServerTimeReceived += OnServerTimeAsync;
		socket.Error += OnWebSocketErrorAsync;
		socket.StateChanged += OnWebSocketStateAsync;
		return socket;
	}

	private async ValueTask RefreshMarketsAsync(
		CancellationToken cancellationToken)
	{
		var pairsTask = RestClient.GetPairsAsync(cancellationToken).AsTask();
		var productsTask = RestClient.GetProductsAsync(cancellationToken).AsTask();
		await Task.WhenAll(pairsTask, productsTask);
		var pairs = await pairsTask ?? [];
		var products = await productsTask ?? throw new InvalidDataException(
			"Nado returned no products.");
		var spotById = (products.SpotProducts ?? [])
			.Where(static item => item is not null)
			.ToDictionary(static item => item.ProductId);
		var perpetualById = (products.PerpetualProducts ?? [])
			.Where(static item => item is not null)
			.ToDictionary(static item => item.ProductId);
		var markets = new List<NadoMarket>();
		foreach (var pair in pairs)
		{
			if (pair?.ProductId is not > 0 || pair.TickerId.IsEmpty() ||
				pair.BaseAsset.IsEmpty() || pair.QuoteAsset.IsEmpty())
				continue;
			NadoMarket market;
			if (spotById.TryGetValue(pair.ProductId, out var spot) &&
				IsValidBook(spot.BookInfo))
				market = new()
				{
					ProductId = pair.ProductId,
					Symbol = pair.TickerId,
					TickerId = pair.TickerId,
					BaseAsset = pair.BaseAsset,
					QuoteAsset = pair.QuoteAsset,
					Type = NadoProductTypes.Spot,
					BookInfo = spot.BookInfo,
					OraclePrice = spot.OraclePrice,
				};
			else if (perpetualById.TryGetValue(pair.ProductId, out var perpetual) &&
				IsValidBook(perpetual.BookInfo))
				market = new()
				{
					ProductId = pair.ProductId,
					Symbol = pair.TickerId,
					TickerId = pair.TickerId,
					BaseAsset = pair.BaseAsset,
					QuoteAsset = pair.QuoteAsset,
					Type = NadoProductTypes.Perpetual,
					BookInfo = perpetual.BookInfo,
					OraclePrice = perpetual.OraclePrice,
					IndexPrice = perpetual.IndexPrice,
					OpenInterest = perpetual.State?.OpenInterest,
				};
			else
				continue;
			markets.Add(market);
		}
		if (markets.Count == 0)
			throw new InvalidDataException(
				"Nado returned no usable spot or perpetual markets.");

		var prices = await RestClient.GetMarketPricesAsync(
			[.. markets.Select(static market => market.ProductId)],
			cancellationToken);
		var pricesById = (prices?.Prices ?? []).Where(static item => item is not null)
			.ToDictionary(static item => item.ProductId);
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByProduct.Clear();
			_prices.Clear();
			foreach (var market in markets.OrderBy(static item => item.Symbol,
				StringComparer.Ordinal))
			{
				_markets.Add(market.Symbol, market);
				_marketsByProduct.Add(market.ProductId, market);
				pricesById.TryGetValue(market.ProductId, out var price);
				_prices[market.ProductId] = new()
				{
					Bid = price?.BidPrice.TryParseX18(),
					Ask = price?.AskPrice.TryParseX18(),
					Oracle = market.OraclePrice.TryParseX18(),
					Index = market.IndexPrice.TryParseX18(),
				};
			}
		}
	}

	private static bool IsValidBook(NadoBookInfo book)
		=> book is not null && book.SizeIncrement.TryParseAmount() is > 0 &&
			book.PriceIncrement.TryParseX18() is > 0 &&
			book.MinimumSize.TryParseAmount() is > 0;

	private NadoSubscriptionKey[] AccountStreams()
		=>
		[
			new(NadoStreamTypes.Fill, 0, 0, _subaccount),
			new(NadoStreamTypes.PositionChange, 0, 0, _subaccount),
			new(NadoStreamTypes.OrderUpdate, 0, 0, _subaccount),
		];

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

	private ValueTask OnServerTimeAsync(DateTime time,
		CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		UpdateServerTime(time);
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

		await RefreshMarketsAsync(cancellationToken);
		DepthSubscription[] depthSubscriptions;
		using (_sync.EnterScope())
			depthSubscriptions = [.. _depthSubscriptions.Values];
		foreach (var subscription in depthSubscriptions)
			await SendDepthSnapshotAsync(subscription.ProductId,
				subscription.TransactionId, subscription.Depth, cancellationToken);
		if (!_subaccount.IsEmpty())
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

	private async ValueTask OnDepthGapAsync(int productId,
		CancellationToken cancellationToken)
	{
		this.AddWarningLog(
			"Nado product {0} order-book sequence gap; refreshing snapshot.",
			productId);
		DepthSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Values.Where(
				subscription => subscription.ProductId == productId)];
		foreach (var subscription in subscriptions)
			await SendDepthSnapshotAsync(productId, subscription.TransactionId,
				subscription.Depth, cancellationToken);
	}
}
