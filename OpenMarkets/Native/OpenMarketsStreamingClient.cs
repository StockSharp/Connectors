namespace StockSharp.OpenMarkets.Native;

sealed class OpenMarketsStreamingClient : Disposable
{
	private readonly OpenMarketsClient _client;
	private readonly bool _isTest;
	private readonly Func<OpenMarketsStreamQuote[], CancellationToken, ValueTask> _quoteHandler;
	private readonly Func<OpenMarketsStreamMarketTrade[], CancellationToken, ValueTask> _marketTradeHandler;
	private readonly Func<OpenMarketsStreamOrder[], CancellationToken, ValueTask> _orderHandler;
	private readonly Func<OpenMarketsStreamTrade[], CancellationToken, ValueTask> _tradeHandler;
	private readonly Func<OpenMarketsStreamPosition[], CancellationToken, ValueTask> _positionHandler;
	private readonly Func<OpenMarketsStreamCash[], CancellationToken, ValueTask> _cashHandler;
	private readonly Func<Exception, CancellationToken, ValueTask> _errorHandler;
	private readonly SemaphoreSlim _marketSync = new(1, 1);
	private readonly SemaphoreSlim _omsSync = new(1, 1);
	private HubConnection _market;
	private HubConnection _oms;
	private bool _isQuoteSubscribed;
	private bool _isMarketTradeSubscribed;
	private bool _isOrderSubscribed;
	private bool _isTradeSubscribed;
	private bool _isPositionSubscribed;
	private bool _isCashSubscribed;
	private string _dataSource = OpenMarketsExtensions.DefaultDataSource;

	public OpenMarketsStreamingClient(OpenMarketsClient client, bool isTest,
		Func<OpenMarketsStreamQuote[], CancellationToken, ValueTask> quoteHandler,
		Func<OpenMarketsStreamMarketTrade[], CancellationToken, ValueTask> marketTradeHandler,
		Func<OpenMarketsStreamOrder[], CancellationToken, ValueTask> orderHandler,
		Func<OpenMarketsStreamTrade[], CancellationToken, ValueTask> tradeHandler,
		Func<OpenMarketsStreamPosition[], CancellationToken, ValueTask> positionHandler,
		Func<OpenMarketsStreamCash[], CancellationToken, ValueTask> cashHandler,
		Func<Exception, CancellationToken, ValueTask> errorHandler)
	{
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_isTest = isTest;
		_quoteHandler = quoteHandler ?? throw new ArgumentNullException(nameof(quoteHandler));
		_marketTradeHandler = marketTradeHandler ?? throw new ArgumentNullException(nameof(marketTradeHandler));
		_orderHandler = orderHandler ?? throw new ArgumentNullException(nameof(orderHandler));
		_tradeHandler = tradeHandler ?? throw new ArgumentNullException(nameof(tradeHandler));
		_positionHandler = positionHandler ?? throw new ArgumentNullException(nameof(positionHandler));
		_cashHandler = cashHandler ?? throw new ArgumentNullException(nameof(cashHandler));
		_errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
	}

	public async Task EnsureMarketSubscriptions(string dataSource, bool isQuotes, bool isTrades,
		CancellationToken cancellationToken)
	{
		await _marketSync.WaitAsync(cancellationToken);
		try
		{
			_dataSource = dataSource.ThrowIfEmpty(nameof(dataSource));
			_market ??= CreateMarketConnection();
			if (_market.State == HubConnectionState.Disconnected)
			{
				await _market.StartAsync(cancellationToken);
				await ResubscribeMarketCore(cancellationToken);
			}

			if (isQuotes && !_isQuoteSubscribed)
			{
				await _market.InvokeAsync("SubscribeToQuoteUpdates", _dataSource, cancellationToken);
				_isQuoteSubscribed = true;
			}
			if (isTrades && !_isMarketTradeSubscribed)
			{
				await _market.InvokeAsync("SubscribeToTradeUpdates", _dataSource, cancellationToken);
				_isMarketTradeSubscribed = true;
			}
		}
		finally
		{
			_marketSync.Release();
		}
	}

	public async Task EnsureOmsSubscriptions(bool isOrders, bool isTrades, bool isPositions,
		bool isCash, CancellationToken cancellationToken)
	{
		await _omsSync.WaitAsync(cancellationToken);
		try
		{
			_oms ??= CreateOmsConnection();
			if (_oms.State == HubConnectionState.Disconnected)
			{
				await _oms.StartAsync(cancellationToken);
				await ResubscribeOmsCore(cancellationToken);
			}

			if (isOrders && !_isOrderSubscribed)
			{
				await _oms.InvokeAsync("SubscribeToOrderUpdates", cancellationToken);
				_isOrderSubscribed = true;
			}
			if (isTrades && !_isTradeSubscribed)
			{
				await _oms.InvokeAsync("SubscribeToTradeUpdates", cancellationToken);
				_isTradeSubscribed = true;
			}
			if (isPositions && !_isPositionSubscribed)
			{
				await _oms.InvokeAsync("SubscribeToPortfolioPositionUpdates", cancellationToken);
				_isPositionSubscribed = true;
			}
			if (isCash && !_isCashSubscribed)
			{
				await _oms.InvokeAsync("SubscribeToPortfolioCashDetailUpdates", cancellationToken);
				_isCashSubscribed = true;
			}
		}
		finally
		{
			_omsSync.Release();
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		if (_market != null && _market.State != HubConnectionState.Disconnected)
			await _market.StopAsync(cancellationToken);
		if (_oms != null && _oms.State != HubConnectionState.Disconnected)
			await _oms.StopAsync(cancellationToken);
	}

	private HubConnection CreateMarketConnection()
	{
		var connection = new HubConnectionBuilder()
			.WithUrl(_client.GetMarketStreamAddress(_isTest), options =>
				options.AccessTokenProvider = () => _client.GetAccessToken(
					OpenMarketsClient.MarketDataStreamScope, CancellationToken.None))
			.WithAutomaticReconnect()
			.AddMessagePackProtocol()
			.Build();

		connection.On<OpenMarketsStreamQuote[]>("QuotesUpdated", quotes =>
			_quoteHandler(quotes ?? [], CancellationToken.None).AsTask());
		connection.On<OpenMarketsStreamMarketTrade[]>("TradesUpdated", trades =>
			_marketTradeHandler(trades ?? [], CancellationToken.None).AsTask());
		connection.Reconnected += _ => ResubscribeMarket();
		connection.Closed += error => error == null
			? Task.CompletedTask
			: _errorHandler(error, CancellationToken.None).AsTask();
		return connection;
	}

	private HubConnection CreateOmsConnection()
	{
		var connection = new HubConnectionBuilder()
			.WithUrl(_client.GetOmsStreamAddress(_isTest), options =>
				options.AccessTokenProvider = () => _client.GetAccessToken(
					OpenMarketsClient.OmsStreamScope, CancellationToken.None))
			.WithAutomaticReconnect()
			.AddMessagePackProtocol()
			.Build();

		connection.On<OpenMarketsStreamOrder[]>("OrdersUpdated", orders =>
			_orderHandler(orders ?? [], CancellationToken.None).AsTask());
		connection.On<OpenMarketsStreamTrade[]>("TradesUpdated", trades =>
			_tradeHandler(trades ?? [], CancellationToken.None).AsTask());
		connection.On<OpenMarketsStreamPosition[]>("PortfolioPositionsUpdated", positions =>
			_positionHandler(positions ?? [], CancellationToken.None).AsTask());
		connection.On<OpenMarketsStreamCash[]>("PortfolioCashDetailsUpdated", cash =>
			_cashHandler(cash ?? [], CancellationToken.None).AsTask());
		connection.Reconnected += _ => ResubscribeOms();
		connection.Closed += error => error == null
			? Task.CompletedTask
			: _errorHandler(error, CancellationToken.None).AsTask();
		return connection;
	}

	private async Task ResubscribeMarket()
	{
		try
		{
			await ResubscribeMarketCore(CancellationToken.None);
		}
		catch (Exception error)
		{
			await _errorHandler(error, CancellationToken.None);
		}
	}

	private async Task ResubscribeMarketCore(CancellationToken cancellationToken)
	{
		if (_isQuoteSubscribed)
			await _market.InvokeAsync("SubscribeToQuoteUpdates", _dataSource, cancellationToken);
		if (_isMarketTradeSubscribed)
			await _market.InvokeAsync("SubscribeToTradeUpdates", _dataSource, cancellationToken);
	}

	private async Task ResubscribeOms()
	{
		try
		{
			await ResubscribeOmsCore(CancellationToken.None);
		}
		catch (Exception error)
		{
			await _errorHandler(error, CancellationToken.None);
		}
	}

	private async Task ResubscribeOmsCore(CancellationToken cancellationToken)
	{
		if (_isOrderSubscribed)
			await _oms.InvokeAsync("SubscribeToOrderUpdates", cancellationToken);
		if (_isTradeSubscribed)
			await _oms.InvokeAsync("SubscribeToTradeUpdates", cancellationToken);
		if (_isPositionSubscribed)
			await _oms.InvokeAsync("SubscribeToPortfolioPositionUpdates", cancellationToken);
		if (_isCashSubscribed)
			await _oms.InvokeAsync("SubscribeToPortfolioCashDetailUpdates", cancellationToken);
	}

	protected override void DisposeManaged()
	{
		_market?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_oms?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_marketSync.Dispose();
		_omsSync.Dispose();
		base.DisposeManaged();
	}
}
