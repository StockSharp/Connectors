namespace StockSharp.MetaApi;

static class MetaApiExtensions
{
	public const string BoardCode = BoardCodes.MetaApi;

	public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(2)] = "2m",
			[TimeSpan.FromMinutes(3)] = "3m",
			[TimeSpan.FromMinutes(4)] = "4m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(6)] = "6m",
			[TimeSpan.FromMinutes(10)] = "10m",
			[TimeSpan.FromMinutes(12)] = "12m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(20)] = "20m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromHours(2)] = "2h",
			[TimeSpan.FromHours(3)] = "3h",
			[TimeSpan.FromHours(4)] = "4h",
			[TimeSpan.FromHours(6)] = "6h",
			[TimeSpan.FromHours(8)] = "8h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "1d",
			[TimeSpan.FromDays(7)] = "1w",
			[TimeSpan.FromDays(30)] = "1mn",
		};

	public static SecurityId ToSecurityId(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)),
			BoardCode = BoardCode,
			Native = symbol,
		};

	public static string ToSymbol(this SecurityId securityId)
		=> (securityId.Native as string).IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty(nameof(securityId));

	public static SecurityMessage ToSecurityMessage(
		this MetaApiSymbolSpecification specification, long originalTransactionId)
	{
		if (specification is null)
			throw new ArgumentNullException(nameof(specification));

		var type = specification.ToSecurityType();
		return new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = specification.Symbol.ToSecurityId(),
			SecurityType = type,
			Name = specification.Description.IsEmpty(specification.Symbol),
			ShortName = specification.Symbol,
			Class = specification.Path,
			Currency = specification.ProfitCurrency.ToCurrency(),
			PriceStep = Positive(specification.TickSize),
			VolumeStep = Positive(specification.VolumeStep),
			MinVolume = Positive(specification.MinVolume),
			MaxVolume = Positive(specification.MaxVolume),
			Multiplier = Positive(specification.ContractSize),
			Decimals = specification.Digits,
		};
	}

	public static SecurityTypes? ToSecurityType(
		this MetaApiSymbolSpecification specification)
	{
		var path = specification?.Path;
		var calcMode = specification?.PriceCalculationMode;
		if (path?.Contains("option", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Option;
		if (path?.Contains("future", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Future;
		if (path?.Contains("cfd", StringComparison.OrdinalIgnoreCase) == true ||
			calcMode?.Contains("cfd", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Cfd;
		if (path?.Contains("stock", StringComparison.OrdinalIgnoreCase) == true ||
			path?.Contains("share", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Stock;
		if (path?.Contains("index", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Index;
		if (path?.Contains("crypto", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.CryptoCurrency;
		if (path?.Contains("commodity", StringComparison.OrdinalIgnoreCase) == true ||
			path?.Contains("metal", StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Commodity;
		if (path?.Contains("forex", StringComparison.OrdinalIgnoreCase) == true ||
			calcMode?.Contains("forex", StringComparison.OrdinalIgnoreCase) == true ||
			(specification?.BaseCurrency.IsEmpty() == false &&
				specification.ProfitCurrency.IsEmpty() == false))
			return SecurityTypes.Currency;
		return null;
	}

	public static MetaApiTradeRequest ToTradeRequest(this OrderRegisterMessage message)
	{
		if (message is null)
			throw new ArgumentNullException(nameof(message));
		if (message.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Volume), message.Volume,
				"MetaApi order volume must be positive.");

		var condition = message.Condition as MetaApiOrderCondition;
		var orderType = message.OrderType ?? OrderTypes.Limit;
		var action = orderType switch
		{
			OrderTypes.Market => message.Side == Sides.Buy
				? "ORDER_TYPE_BUY" : "ORDER_TYPE_SELL",
			OrderTypes.Limit => message.Side == Sides.Buy
				? "ORDER_TYPE_BUY_LIMIT" : "ORDER_TYPE_SELL_LIMIT",
			OrderTypes.Conditional => message.Side == Sides.Buy
				? "ORDER_TYPE_BUY_STOP" : "ORDER_TYPE_SELL_STOP",
			_ => throw new NotSupportedException(
				$"MetaApi does not support {orderType} orders."),
		};
		var openPrice = orderType switch
		{
			OrderTypes.Market => null,
			OrderTypes.Conditional => condition?.ActivationPrice ?? Positive(message.Price),
			_ => Positive(message.Price),
		};
		if (orderType != OrderTypes.Market && openPrice is null)
			throw new InvalidOperationException("MetaApi pending orders require an open price.");

		return new()
		{
			ActionType = action,
			Symbol = message.SecurityId.ToSymbol(),
			Volume = message.Volume,
			OpenPrice = openPrice,
			StopLoss = condition?.StopLoss,
			TakeProfit = condition?.TakeProfit,
			StopLossUnits = condition?.StopLoss is null ? null : "ABSOLUTE_PRICE",
			TakeProfitUnits = condition?.TakeProfit is null ? null : "ABSOLUTE_PRICE",
			Comment = condition?.Comment,
			ClientId = condition?.ClientId,
			Magic = condition?.Magic,
			Slippage = message.Slippage,
			FillingModes = message.ToMetaApiFillingModes(),
			Expiration = orderType == OrderTypes.Market
				? null : message.ToMetaApiExpiration(),
		};
	}

	public static Sides ToSide(this string value)
		=> value?.Contains("SELL", StringComparison.OrdinalIgnoreCase) == true
			? Sides.Sell : Sides.Buy;

	public static OrderTypes ToOrderType(this string value)
		=> value?.Contains("STOP", StringComparison.OrdinalIgnoreCase) == true
			? OrderTypes.Conditional
			: value?.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) == true
				? OrderTypes.Limit
				: OrderTypes.Market;

	public static OrderStates ToOrderState(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"ORDER_STATE_PLACED" or "ORDER_STATE_STARTED" or "ORDER_STATE_PARTIAL" or
				"ORDER_STATE_REQUEST_ADD"
				=> OrderStates.Active,
			"ORDER_STATE_REJECTED" => OrderStates.Failed,
			"ORDER_STATE_FILLED" or "ORDER_STATE_CANCELED" or "ORDER_STATE_EXPIRED"
				=> OrderStates.Done,
			_ => OrderStates.Pending,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency : null;

	public static long? ToNumericId(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var id) && id > 0 ? id : null;

	public static string ToMetaApiTimeFrame(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new NotSupportedException(
				$"MetaApi does not support the {timeFrame} candle timeframe.");

	private static MetaApiExpiration ToMetaApiExpiration(
		this OrderRegisterMessage message)
		=> new()
		{
			Type = message.TillDate is null
				? "ORDER_TIME_GTC" : "ORDER_TIME_SPECIFIED",
			Time = message.TillDate?.ToUniversalTime(),
		};

	private static string[] ToMetaApiFillingModes(this OrderRegisterMessage message)
		=> message.TimeInForce switch
		{
			TimeInForce.MatchOrCancel => ["ORDER_FILLING_FOK"],
			TimeInForce.CancelBalance => ["ORDER_FILLING_IOC"],
			_ => null,
		};

	private static decimal? Positive(decimal value) => value > 0 ? value : null;
}
