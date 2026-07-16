namespace StockSharp.TigerBrokers.Native;

sealed class TigerNativeClient : BaseLogReceiver
{
	private readonly TigerConfig _config;
	private readonly QuoteClient _quoteClient;
	private readonly TradeClient _tradeClient;
	private readonly PushClient _pushClient;
	private readonly Channel<TigerPushEvent> _events = Channel.CreateUnbounded<TigerPushEvent>(new()
	{
		SingleReader = true,
		SingleWriter = false,
	});
	private readonly CancellationTokenSource _eventCancellation = new();
	private readonly Task _eventTask;
	private readonly SynchronizedSet<string> _quotes = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _options = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _futures = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _depth = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeTicks = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _klines = new(StringComparer.OrdinalIgnoreCase);

	public TigerNativeClient(TigerConfig config)
	{
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_quoteClient = new(config);
		_tradeClient = new(config);
		_pushClient = PushClient.GetInstance();
		_eventTask = ProcessEvents(_eventCancellation.Token);
	}

	public override string Name => nameof(TigerBrokers) + "_" + nameof(TigerNativeClient);

	public event Func<QuoteBasicData, CancellationToken, ValueTask> QuoteReceived;
	public event Func<QuoteBBOData, CancellationToken, ValueTask> BboReceived;
	public event Func<QuoteDepthData, CancellationToken, ValueTask> DepthReceived;
	public event Func<TradeTick, CancellationToken, ValueTask> TradeTickReceived;
	public event Func<TickData, CancellationToken, ValueTask> FullTickReceived;
	public event Func<KlineData, CancellationToken, ValueTask> KlineReceived;
	public event Func<OrderStatusData, CancellationToken, ValueTask> OrderReceived;
	public event Func<OrderTransactionData, CancellationToken, ValueTask> OrderTransactionReceived;
	public event Func<PositionData, CancellationToken, ValueTask> PositionReceived;
	public event Func<AssetData, CancellationToken, ValueTask> AssetReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		Disconnect();
		_eventCancellation.Cancel();
		_events.Writer.TryComplete();
		try
		{
			_eventTask.GetAwaiter().GetResult();
		}
		catch (OperationCanceledException)
		{
		}
		_eventCancellation.Dispose();
		base.DisposeManaged();
	}

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_pushClient.IsConnected())
			throw new InvalidOperationException("The official Tiger OpenAPI SDK supports one active push connection per process and tigerId.");

		_pushClient.Config(_config).ApiComposeCallback(new TigerPushCallback(e => _events.Writer.TryWrite(e)));
		try
		{
			if (!await _pushClient.ConnectAsync().WaitAsync(cancellationToken))
				throw new InvalidOperationException("Tiger OpenAPI push connection failed.");
		}
		catch
		{
			_pushClient.Disconnect();
			throw;
		}
	}

	public void Disconnect()
	{
		if (_pushClient.IsConnected())
			_pushClient.Disconnect();
	}

	public ValueTask SetSubscription(TigerFeedTypes feedType, string symbol, bool subscribe)
	{
		var subscriptions = GetSubscriptions(feedType);
		if (subscribe)
		{
			if (subscriptions.Contains(symbol))
				return default;

			subscriptions.Add(symbol);
		}
		else if (!subscriptions.Remove(symbol))
			return default;

		SendSubscription(feedType, symbol, subscribe);
		return default;
	}

	public async Task<SymbolNameItem[]> GetStocks(Market market, CancellationToken cancellationToken)
	{
		var response = await ExecuteQuote<SymbolNameResponse>(QuoteApiService.ALL_SYMBOL_NAMES,
			new QuoteMarketModel { Market = market, Lang = Language.en_US }, cancellationToken);
		return [.. response.Data ?? []];
	}

	public async Task<FutureExchangeItem[]> GetFutureExchanges(CancellationToken cancellationToken)
	{
		var response = await ExecuteQuote<FutureExchangeResponse>(QuoteApiService.FUTURE_EXCHANGE,
			new FutureExchangeModel { SecType = SecType.FUT.ToString() }, cancellationToken);
		return [.. response.Data ?? []];
	}

	public async Task<FutureContractItem[]> GetFutures(string exchangeCode, CancellationToken cancellationToken)
	{
		var response = await ExecuteQuote<FutureContractsResponse>(QuoteApiService.FUTURE_CONTRACT_BY_EXCHANGE_CODE,
			new FutureContractByExchCodeModel { ExchangeCode = exchangeCode }, cancellationToken);
		return [.. response.Data ?? []];
	}

	public async Task<FutureContractItem> GetFuture(string contractCode, CancellationToken cancellationToken)
	{
		var response = await ExecuteQuote<FutureContractResponse>(QuoteApiService.FUTURE_CONTRACT_BY_CONTRACT_CODE,
			new FutureContractByConCodeModel { ContractCode = contractCode }, cancellationToken);
		return response.Data;
	}

	public async Task<OptionExpirationItem[]> GetOptionExpirations(string symbol, Market market, CancellationToken cancellationToken)
	{
		var response = await ExecuteQuote<OptionExpirationResponse>(QuoteApiService.OPTION_EXPIRATION,
			new OptionExpirationModel { Symbols = [symbol], Market = market }, cancellationToken);
		return [.. response.Data ?? []];
	}

	public async Task<OptionChainItem[]> GetOptionChain(string symbol, Market market, long expiry, CancellationToken cancellationToken)
	{
		var response = await ExecuteQuote<OptionChainResponse>(QuoteApiService.OPTION_CHAIN,
			new OptionChainV3Model
			{
				Market = market,
				OptionBasic = [new() { Symbol = symbol, Expiry = expiry }],
				ReturnGreekValue = true,
			}, cancellationToken);
		return [.. response.Data ?? []];
	}

	public Task<QuoteKlineResponse> GetStockCandles(string symbol, string period, long from, long to, int limit,
		CancellationToken cancellationToken)
		=> ExecuteQuote<QuoteKlineResponse>(QuoteApiService.KLINE, new QuoteKlineModel
		{
			Symbols = [symbol],
			Period = period,
			BeginTime = from,
			EndTime = to,
			Limit = limit,
			Rigth = RightOption.br,
		}, cancellationToken);

	public Task<FutureKlineResponse> GetFutureCandles(string symbol, string period, long from, long to, int limit,
		CancellationToken cancellationToken)
		=> ExecuteQuote<FutureKlineResponse>(QuoteApiService.FUTURE_KLINE, new FutureKlineModel
		{
			ContractCodes = [symbol],
			Period = period,
			BeginTime = from,
			EndTime = to,
			Limit = limit,
		}, cancellationToken);

	public Task<OptionKlineResponse> GetOptionCandles(TigerInstrument instrument, string period, long from, long to, int limit,
		CancellationToken cancellationToken)
		=> ExecuteQuote<OptionKlineResponse>(QuoteApiService.OPTION_KLINE, new OptionKlineV2Model
		{
			Market = instrument.Market,
			OptionQuery =
			[
				new()
				{
					Symbol = instrument.Symbol,
					Right = instrument.Right,
					Strike = instrument.Strike?.ToString(CultureInfo.InvariantCulture),
					Expiry = instrument.ExpiryDate.ToUnixMilliseconds(),
					Period = period,
					BeginTime = from,
					EndTime = to,
					Limit = limit,
					SortDir = SortDir.SortDir_Ascend,
				},
			],
		}, cancellationToken);

	public async Task<PositionDetail[]> GetPositions(string account, CancellationToken cancellationToken)
	{
		var response = await ExecuteTrade<PositionsResponse>(TradeApiService.POSITIONS,
			new PositionsModel { Account = account, Market = Market.ALL }, cancellationToken);
		return [.. response.Data?.Items ?? []];
	}

	public Task<PrimeAssetResponse> GetAssets(string account, CancellationToken cancellationToken)
		=> ExecuteTrade<PrimeAssetResponse>(TradeApiService.PRIME_ASSETS,
			new PrimeAssetsModel { Account = account, Consolidated = true }, cancellationToken);

	public async Task<TradeOrder[]> GetOrders(string account, DateTimeOffset? from, DateTimeOffset? to,
		CancellationToken cancellationToken)
	{
		var model = new QueryOrderModel
		{
			Account = account,
			StartDate = from.ToUnixMilliseconds(),
			EndDate = to.ToUnixMilliseconds(),
			Limit = 300,
			SortBy = OrderSortBy.LATEST_CREATED,
		};
		var result = new List<TradeOrder>();
		do
		{
			var response = await ExecuteTrade<OrderBatchResponse>(TradeApiService.ORDERS, model, cancellationToken);
			result.AddRange(response.Data?.Items ?? []);
			model.PageToken = response.Data?.NextPageToken;
		}
		while (!model.PageToken.IsEmpty());
		return [.. result];
	}

	public async Task<OrderTransactions[]> GetTransactions(string account, DateTimeOffset? from, DateTimeOffset? to,
		CancellationToken cancellationToken)
	{
		var response = await ExecuteTrade<OrderTransactionsResponse>(TradeApiService.ORDER_TRANSACTIONS,
			new OrderTransactionsModel
			{
				Account = account,
				StartDate = from.ToUnixMilliseconds(),
				EndDate = to.ToUnixMilliseconds(),
				Limit = 300,
			}, cancellationToken);
		return [.. response.Data?.Items ?? []];
	}

	public Task<PlaceOrderResponse> PlaceOrder(PlaceOrderModel model, CancellationToken cancellationToken)
		=> ExecuteTrade<PlaceOrderResponse>(TradeApiService.PLACE_ORDER, model, cancellationToken);

	public Task<TigerOperationResponse> ModifyOrder(ModifyOrderModel model, CancellationToken cancellationToken)
		=> ExecuteTrade<TigerOperationResponse>(TradeApiService.MODIFY_ORDER, model, cancellationToken);

	public Task<TigerOperationResponse> CancelOrder(CancelOrderModel model, CancellationToken cancellationToken)
		=> ExecuteTrade<TigerOperationResponse>(TradeApiService.CANCEL_ORDER, model, cancellationToken);

	private async Task<T> ExecuteQuote<T>(string apiMethod, ApiModel model, CancellationToken cancellationToken)
		where T : TigerResponse
		=> EnsureResponse(await _quoteClient.ExecuteAsync(new TigerRequest<T>
		{
			ApiMethodName = apiMethod,
			ModelValue = model,
		}).WaitAsync(cancellationToken), apiMethod);

	private async Task<T> ExecuteTrade<T>(string apiMethod, ApiModel model, CancellationToken cancellationToken)
		where T : TigerResponse
		=> EnsureResponse(await _tradeClient.ExecuteAsync(new TigerRequest<T>
		{
			ApiMethodName = apiMethod,
			ModelValue = model,
		}).WaitAsync(cancellationToken), apiMethod);

	private static T EnsureResponse<T>(T response, string operation)
		where T : TigerResponse
	{
		if (response == null)
			throw new InvalidOperationException($"Tiger OpenAPI {operation} returned an empty response.");
		if (!response.IsSuccess())
			throw new InvalidOperationException($"Tiger OpenAPI {operation} failed ({response.Code}): {response.Message}");
		return response;
	}

	private SynchronizedSet<string> GetSubscriptions(TigerFeedTypes feedType)
		=> feedType switch
		{
			TigerFeedTypes.Quote => _quotes,
			TigerFeedTypes.Option => _options,
			TigerFeedTypes.Future => _futures,
			TigerFeedTypes.Depth => _depth,
			TigerFeedTypes.TradeTick => _tradeTicks,
			TigerFeedTypes.Kline => _klines,
			_ => throw new ArgumentOutOfRangeException(nameof(feedType), feedType, null),
		};

	private void SendSubscription(TigerFeedTypes feedType, string symbol, bool subscribe)
	{
		var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { symbol };
		_ = (feedType, subscribe) switch
		{
			(TigerFeedTypes.Quote, true) => _pushClient.SubscribeQuote(symbols),
			(TigerFeedTypes.Quote, false) => _pushClient.CancelSubscribeQuote(symbols),
			(TigerFeedTypes.Option, true) => _pushClient.SubscribeOption(symbols),
			(TigerFeedTypes.Option, false) => _pushClient.CancelSubscribeOption(symbols),
			(TigerFeedTypes.Future, true) => _pushClient.SubscribeFuture(symbols),
			(TigerFeedTypes.Future, false) => _pushClient.CancelSubscribeFuture(symbols),
			(TigerFeedTypes.Depth, true) => _pushClient.SubscribeDepthQuote(symbols),
			(TigerFeedTypes.Depth, false) => _pushClient.CancelSubscribeDepthQuote(symbols),
			(TigerFeedTypes.TradeTick, true) => _pushClient.SubscribeTradeTick(symbols),
			(TigerFeedTypes.TradeTick, false) => _pushClient.CancelSubscribeTradeTick(symbols),
			(TigerFeedTypes.Kline, true) => _pushClient.SubscribeKline(symbols),
			(TigerFeedTypes.Kline, false) => _pushClient.CancelSubscribeKline(symbols),
			_ => throw new ArgumentOutOfRangeException(nameof(feedType), feedType, null),
		};
	}

	private void Resubscribe()
	{
		foreach (var feedType in Enum.GetValues<TigerFeedTypes>())
			foreach (var symbol in GetSubscriptions(feedType).ToArray())
				SendSubscription(feedType, symbol, true);
		foreach (var subject in new[] { Subject.Asset, Subject.Position, Subject.OrderStatus, Subject.OrderTransaction })
			_pushClient.Subscribe(subject, _config.DefaultAccount);
	}

	private async Task ProcessEvents(CancellationToken cancellationToken)
	{
		await foreach (var message in _events.Reader.ReadAllAsync(cancellationToken))
		{
			try
			{
				switch (message)
				{
					case TigerConnectedEvent:
						Resubscribe();
						await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
						break;
					case TigerDisconnectedEvent:
						await Invoke(StateChanged, ConnectionStates.Disconnected, cancellationToken);
						break;
					case TigerQuoteEvent quote:
						await Invoke(QuoteReceived, quote.Data, cancellationToken);
						break;
					case TigerBboEvent bbo:
						await Invoke(BboReceived, bbo.Data, cancellationToken);
						break;
					case TigerDepthEvent depth:
						await Invoke(DepthReceived, depth.Data, cancellationToken);
						break;
					case TigerTradeTickEvent tick:
						await Invoke(TradeTickReceived, tick.Data, cancellationToken);
						break;
					case TigerFullTickEvent fullTick:
						await Invoke(FullTickReceived, fullTick.Data, cancellationToken);
						break;
					case TigerKlineEvent kline:
						await Invoke(KlineReceived, kline.Data, cancellationToken);
						break;
					case TigerOrderEvent order:
						await Invoke(OrderReceived, order.Data, cancellationToken);
						break;
					case TigerOrderTransactionEvent transaction:
						await Invoke(OrderTransactionReceived, transaction.Data, cancellationToken);
						break;
					case TigerPositionEvent position:
						await Invoke(PositionReceived, position.Data, cancellationToken);
						break;
					case TigerAssetEvent asset:
						await Invoke(AssetReceived, asset.Data, cancellationToken);
						break;
					case TigerErrorEvent error:
						await Invoke(Error, error.Error, cancellationToken);
						break;
				}
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}
		}
	}

	private static ValueTask Invoke<T>(Func<T, CancellationToken, ValueTask> handler, T value, CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);
}
