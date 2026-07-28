namespace StockSharp.BigOne.Native;

sealed class BigOneWsClient : BaseLogReceiver
{
	private readonly BigOneSpotWsClient _spot;
	private readonly BigOneContractWsClient _contract;
	private readonly BigOneAuthenticator _authenticator;

	public BigOneWsClient(
		string spotEndpoint,
		string contractEndpoint,
		string contractPrivateEndpoint,
		SecureString key,
		SecureString secret,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_authenticator = new(key, secret);
		_spot = new(
			spotEndpoint, workingTime, reconnectAttempts)
		{
			Parent = this,
		};
		_contract = new(
			contractEndpoint,
			contractPrivateEndpoint,
			_authenticator.IsAvailable
				? _authenticator.CreateContractToken
				: null,
			workingTime,
			reconnectAttempts)
		{
			Parent = this,
		};

		_spot.TickerReceived += OnSpotTickerAsync;
		_spot.OrderBookReceived += OnOrderBookAsync;
		_spot.TradesReceived += OnTradesAsync;
		_spot.CandleReceived += OnCandleAsync;
		_spot.BalanceReceived += OnBalanceAsync;
		_spot.OrderReceived += OnOrderAsync;
		_spot.Error += OnErrorAsync;
		_spot.StateChanged += OnStateChangedAsync;

		_contract.InstrumentReceived += OnInstrumentAsync;
		_contract.OrderBookReceived += OnContractOrderBookAsync;
		_contract.TradesReceived += OnContractTradesAsync;
		_contract.CandlesReceived += OnContractCandlesAsync;
		_contract.PrivateReceived += OnContractPrivateAsync;
		_contract.Error += OnErrorAsync;
		_contract.StateChanged += OnStateChangedAsync;
	}

	public override string Name => "BigONE_WS";

	public event Func<BigOneTicker,
		CancellationToken, ValueTask> TickerReceived;
	public event Func<BigOneOrderBook,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<BigOneTradePush,
		CancellationToken, ValueTask> TradesReceived;
	public event Func<BigOneKlineEvent,
		CancellationToken, ValueTask> KlineReceived;
	public event Func<BigOneBalance,
		CancellationToken, ValueTask> BalanceReceived;
	public event Func<BigOneContractPosition,
		CancellationToken, ValueTask> PositionReceived;
	public event Func<BigOneOrder,
		CancellationToken, ValueTask> OrderReceived;
	public event Func<BigOnePrivateTrade,
		CancellationToken, ValueTask> PrivateTradeReceived;
	public event Func<Exception,
		CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates,
		CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		UnsubscribeEvents();
		_spot.Dispose();
		_contract.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		await _spot.ConnectAsync(cancellationToken);
		try
		{
			await _contract.ConnectAsync(cancellationToken);
			if (_authenticator.IsAvailable)
			{
				await _spot.AuthenticateAsync(
					_authenticator.CreateSpotToken(),
					cancellationToken);
				await _spot.SubscribeAccountsAsync(cancellationToken);
				await _spot.SubscribeOrdersAsync(cancellationToken);
			}
		}
		catch
		{
			await _spot.DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		await _contract.DisconnectAsync(cancellationToken);
		await _spot.DisconnectAsync(cancellationToken);
	}

	public ValueTask SubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> IsContract(symbol)
			? _contract.SubscribeInstrumentAsync(
				symbol, cancellationToken)
			: _spot.SubscribeTickerAsync(
				symbol, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> IsContract(symbol)
			? _contract.UnsubscribeInstrumentAsync(
				symbol, cancellationToken)
			: _spot.UnsubscribeTickerAsync(
				symbol, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> IsContract(symbol)
			? _contract.SubscribeOrderBookAsync(
				symbol, cancellationToken)
			: _spot.SubscribeOrderBookAsync(
				symbol, depth, cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> IsContract(symbol)
			? _contract.UnsubscribeOrderBookAsync(
				symbol, cancellationToken)
			: _spot.UnsubscribeOrderBookAsync(
				symbol, depth, cancellationToken);

	public ValueTask SubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> IsContract(symbol)
			? _contract.SubscribeTradesAsync(
				symbol, cancellationToken)
			: _spot.SubscribeTradesAsync(
				symbol, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> IsContract(symbol)
			? _contract.UnsubscribeTradesAsync(
				symbol, cancellationToken)
			: _spot.UnsubscribeTradesAsync(
				symbol, cancellationToken);

	public ValueTask SubscribeKlineAsync(
		string symbol,
		string period,
		CancellationToken cancellationToken)
		=> IsContract(symbol)
			? _contract.SubscribeCandlesAsync(
				symbol, ToContractPeriod(period),
				cancellationToken)
			: _spot.SubscribeCandlesAsync(
				symbol, ToSpotStreamPeriod(period),
				cancellationToken);

	public ValueTask UnsubscribeKlineAsync(
		string symbol,
		string period,
		CancellationToken cancellationToken)
		=> IsContract(symbol)
			? _contract.UnsubscribeCandlesAsync(
				symbol, ToContractPeriod(period),
				cancellationToken)
			: _spot.UnsubscribeCandlesAsync(
				symbol, ToSpotStreamPeriod(period),
				cancellationToken);

	private ValueTask OnSpotTickerAsync(
		BigOneTicker ticker,
		CancellationToken cancellationToken)
		=> TickerReceived is { } handler
			? handler(ticker, cancellationToken)
			: default;

	private ValueTask OnInstrumentAsync(
		BigOneContractInstrument instrument,
		CancellationToken cancellationToken)
		=> TickerReceived is { } handler
			? handler(instrument.ToTicker(), cancellationToken)
			: default;

	private ValueTask OnOrderBookAsync(
		BigOneOrderBook book,
		CancellationToken cancellationToken)
		=> OrderBookReceived is { } handler
			? handler(book, cancellationToken)
			: default;

	private ValueTask OnContractOrderBookAsync(
		BigOneContractDepth book,
		CancellationToken cancellationToken)
		=> OnOrderBookAsync(
			book.ToOrderBook(book.Symbol),
			cancellationToken);

	private ValueTask OnTradesAsync(
		BigOneTradePush trades,
		CancellationToken cancellationToken)
		=> TradesReceived is { } handler
			? handler(trades, cancellationToken)
			: default;

	private ValueTask OnContractTradesAsync(
		BigOneContractTrade[] trades,
		CancellationToken cancellationToken)
	{
		var symbol = trades?.FirstOrDefault()?.Symbol;
		return symbol.IsEmpty()
			? default
			: OnTradesAsync(new()
			{
				Pair = symbol,
				EventId = DateTime.UtcNow
					.ToBigOneMilliseconds()
					.ToString(CultureInfo.InvariantCulture),
				Data = [.. trades.Select(
					static trade => trade.ToTrade())],
			}, cancellationToken);
	}

	private ValueTask OnCandleAsync(
		BigOneKlineEvent candle,
		CancellationToken cancellationToken)
		=> KlineReceived is { } handler
			? handler(candle, cancellationToken)
			: default;

	private async ValueTask OnContractCandlesAsync(
		BigOneContractCandle[] candles,
		CancellationToken cancellationToken)
	{
		if (KlineReceived is not { } handler)
			return;
		foreach (var candle in candles ?? [])
		{
			var converted = candle.ToCandle();
			await handler(new()
			{
				Market = candle.Symbol,
				Kline = new()
				{
					StartTime = converted.Timestamp,
					EndTime = candle.NextTimestamp,
					Resolution = candle.Type,
					Open = converted.Open,
					High = converted.High,
					Low = converted.Low,
					Close = converted.Close,
					Volume = converted.Volume,
					IsFinished = converted.IsFinished,
				},
			}, cancellationToken);
		}
	}

	private ValueTask OnBalanceAsync(
		BigOneBalance balance,
		CancellationToken cancellationToken)
		=> BalanceReceived is { } handler
			? handler(balance, cancellationToken)
			: default;

	private ValueTask OnOrderAsync(
		BigOneOrder order,
		CancellationToken cancellationToken)
		=> OrderReceived is { } handler
			? handler(order, cancellationToken)
			: default;

	private async ValueTask OnContractPrivateAsync(
		BigOneContractStream update,
		CancellationToken cancellationToken)
	{
		if (update?.Cash is { } cash &&
			BalanceReceived is { } balanceHandler)
			await balanceHandler(
				cash.ToBalance(), cancellationToken);
		if (PositionReceived is { } positionHandler)
			foreach (var position in update?.Positions ?? [])
				await positionHandler(
					position, cancellationToken);
		if (OrderReceived is { } orderHandler)
			foreach (var order in update?.Orders ?? [])
				await orderHandler(
					order.ToOrder(), cancellationToken);
		if (PrivateTradeReceived is { } tradeHandler)
			foreach (var trade in update?.Trades ?? [])
				await tradeHandler(
					trade.ToTrade(), cancellationToken);
	}

	private ValueTask OnErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private ValueTask OnStateChangedAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
		=> StateChanged is { } handler
			? handler(state, cancellationToken)
			: default;

	private void UnsubscribeEvents()
	{
		_spot.TickerReceived -= OnSpotTickerAsync;
		_spot.OrderBookReceived -= OnOrderBookAsync;
		_spot.TradesReceived -= OnTradesAsync;
		_spot.CandleReceived -= OnCandleAsync;
		_spot.BalanceReceived -= OnBalanceAsync;
		_spot.OrderReceived -= OnOrderAsync;
		_spot.Error -= OnErrorAsync;
		_spot.StateChanged -= OnStateChangedAsync;
		_contract.InstrumentReceived -= OnInstrumentAsync;
		_contract.OrderBookReceived -= OnContractOrderBookAsync;
		_contract.TradesReceived -= OnContractTradesAsync;
		_contract.CandlesReceived -= OnContractCandlesAsync;
		_contract.PrivateReceived -= OnContractPrivateAsync;
		_contract.Error -= OnErrorAsync;
		_contract.StateChanged -= OnStateChangedAsync;
	}

	private static bool IsContract(string symbol)
		=> !symbol.ThrowIfEmpty(nameof(symbol)).Contains('-');

	private static string ToSpotStreamPeriod(string period)
	{
		period = period.ThrowIfEmpty(nameof(period)).Trim();
		foreach (var timeFrame in BigOneExtensions.TimeFrames)
			if (timeFrame.ToBigOneSpotPeriod()
				.EqualsIgnoreCase(period) ||
				timeFrame.ToBigOneSpotStreamPeriod()
					.EqualsIgnoreCase(period))
				return timeFrame.ToBigOneSpotStreamPeriod();
		throw new ArgumentOutOfRangeException(
			nameof(period), period,
			"Unsupported BigONE spot candle period.");
	}

	private static string ToContractPeriod(string period)
	{
		period = period.ThrowIfEmpty(nameof(period)).Trim();
		foreach (var timeFrame in BigOneExtensions.TimeFrames.Where(
			static value => value.IsContractPeriodSupported()))
			if (timeFrame.ToBigOneSpotPeriod()
				.EqualsIgnoreCase(period) ||
				timeFrame.ToBigOneSpotStreamPeriod()
					.EqualsIgnoreCase(period) ||
				timeFrame.ToBigOneContractPeriod()
					.EqualsIgnoreCase(period))
				return timeFrame.ToBigOneContractPeriod();
		throw new ArgumentOutOfRangeException(
			nameof(period), period,
			"Unsupported BigONE contract candle period.");
	}
}
