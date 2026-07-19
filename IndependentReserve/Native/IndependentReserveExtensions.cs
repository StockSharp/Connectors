namespace StockSharp.IndependentReserve.Native;

static class IndependentReserveExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromHours(1),
	];

	public static string ToSymbol(string primary, string secondary)
		=> $"{primary.ThrowIfEmpty(nameof(primary)).Trim().ToUpperInvariant()}/" +
			secondary.ThrowIfEmpty(nameof(secondary)).Trim().ToUpperInvariant();

	public static (string primary, string secondary) SplitSymbol(
		this string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant();
		var parts = symbol.Split(['/', '-', '_'],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new ArgumentException(
				$"Independent Reserve symbol '{symbol}' must use PRIMARY/SECONDARY format.",
				nameof(symbol));
		return (parts[0], parts[1]);
	}

	public static SecurityId ToStockSharp(string primary, string secondary)
		=> new()
		{
			SecurityCode = ToSymbol(primary, secondary),
			BoardCode = BoardCodes.IndependentReserve,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static decimal StepFromScale(int scale)
	{
		if (scale is < 0 or > 28)
			return 0m;
		var value = 1m;
		for (var i = 0; i < scale; i++)
			value /= 10m;
		return value;
	}

	public static bool IsBuy(this IndependentReserveOrderTypes type)
		=> type is IndependentReserveOrderTypes.MarketBid or
			IndependentReserveOrderTypes.LimitBid;

	public static bool IsLimit(this IndependentReserveOrderTypes type)
		=> type is IndependentReserveOrderTypes.LimitBid or
			IndependentReserveOrderTypes.LimitOffer;

	public static IndependentReserveOrderTypes ToIndependentReserve(
		this Sides side, bool isMarket)
		=> (side, isMarket) switch
		{
			(Sides.Buy, false) => IndependentReserveOrderTypes.LimitBid,
			(Sides.Sell, false) => IndependentReserveOrderTypes.LimitOffer,
			(Sides.Buy, true) => IndependentReserveOrderTypes.MarketBid,
			(Sides.Sell, true) => IndependentReserveOrderTypes.MarketOffer,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static Sides ToStockSharp(this IndependentReserveOrderTypes type)
		=> type.IsBuy() ? Sides.Buy : Sides.Sell;

	public static OrderTypes ToStockSharpOrderType(
		this IndependentReserveOrderTypes type)
		=> type.IsLimit() ? OrderTypes.Limit : OrderTypes.Market;

	public static OrderStates ToStockSharp(
		this IndependentReserveOrderStatuses status)
		=> status switch
		{
			IndependentReserveOrderStatuses.Open or
			IndependentReserveOrderStatuses.PartiallyFilled =>
				OrderStates.Active,
			IndependentReserveOrderStatuses.Failed or
			IndependentReserveOrderStatuses.PartiallyFilledAndFailed =>
				OrderStates.Failed,
			_ => OrderStates.Done,
		};

	public static IndependentReserveTimeInForce ToIndependentReserve(
		this TimeInForce? timeInForce, bool isPostOnly)
	{
		if (isPostOnly)
		{
			if (timeInForce is not null and not TimeInForce.PutInQueue)
				throw new InvalidOperationException(
					"Independent Reserve maker-only orders cannot use IOC or FOK.");
			return IndependentReserveTimeInForce.Moc;
		}
		return timeInForce switch
		{
			null or TimeInForce.PutInQueue =>
				IndependentReserveTimeInForce.Gtc,
			TimeInForce.CancelBalance => IndependentReserveTimeInForce.Ioc,
			TimeInForce.MatchOrCancel => IndependentReserveTimeInForce.Fok,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce,
				"Independent Reserve supports GTC, IOC, FOK, and MOC only."),
		};
	}

	public static TimeInForce ToStockSharp(
		this IndependentReserveTimeInForce timeInForce)
		=> timeInForce switch
		{
			IndependentReserveTimeInForce.Ioc => TimeInForce.CancelBalance,
			IndependentReserveTimeInForce.Fok => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static Sides ToStockSharp(this IndependentReserveTakers taker)
		=> taker switch
		{
			IndependentReserveTakers.Bid => Sides.Buy,
			IndependentReserveTakers.Offer => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(taker), taker,
				null),
		};

	public static Sides ToStockSharp(this IndependentReserveSocketSides side)
		=> side switch
		{
			IndependentReserveSocketSides.Buy => Sides.Buy,
			IndependentReserveSocketSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static decimal? GetPrice(this IndependentReserveSocketPrices prices,
		string secondary)
		=> secondary?.Trim().ToUpperInvariant() switch
		{
			"AUD" => prices?.Aud,
			"USD" => prices?.Usd,
			"NZD" => prices?.Nzd,
			"SGD" => prices?.Sgd,
			_ => null,
		};

	public static DateTime ToIndependentReserveTime(this long timestamp,
		DateTime fallback)
	{
		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
				.UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return fallback.Kind == DateTimeKind.Utc
				? fallback
				: fallback.ToUniversalTime();
		}
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string ToApiTime(this DateTime value)
		=> value.EnsureUtc().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
			CultureInfo.InvariantCulture);

	public static string ToWire(this decimal value)
		=> value.ToString("0.#############################",
			CultureInfo.InvariantCulture);

	public static DateTime AlignHour(this DateTime value)
	{
		value = value.EnsureUtc();
		return new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0,
			DateTimeKind.Utc);
	}
}
