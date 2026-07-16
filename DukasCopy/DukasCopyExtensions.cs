namespace StockSharp.DukasCopy;

internal static class DukasCopyExtensions
{
	public const string BoardCode = BoardCodes.DukasCopy;

	public static PairSet<TimeSpan, string> TimeFrames { get; } = new()
	{
		{ TimeSpan.FromSeconds(10), "TEN_SECS" },
		{ TimeSpan.FromMinutes(1), "ONE_MIN" },
		{ TimeSpan.FromMinutes(5), "FIVE_MINS" },
		{ TimeSpan.FromMinutes(10), "TEN_MINS" },
		{ TimeSpan.FromMinutes(15), "FIFTEEN_MINS" },
		{ TimeSpan.FromMinutes(30), "THIRTY_MINS" },
		{ TimeSpan.FromHours(1), "ONE_HOUR" },
		{ TimeSpan.FromHours(4), "FOUR_HOURS" },
		{ TimeSpan.FromDays(1), "DAILY" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerWeek), "WEEKLY" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "MONTHLY" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan? ToTimeFrame(this string period)
		=> TimeFrames.FirstOrDefault(pair => pair.Value.EqualsIgnoreCase(period)).Key is var value && value != default
			? value
			: null;

	public static string NormalizeDukasSymbol(this string symbol)
	{
		if (symbol.IsEmpty())
			return symbol;

		return symbol.Trim().Replace('_', '/').ToUpperInvariant();
	}

	public static SecurityId ToSecurityId(this string symbol)
		=> new()
		{
			SecurityCode = symbol.NormalizeDukasSymbol(),
			BoardCode = BoardCode,
		};

	public static SecurityTypes? ToSecurityType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"FOREX" or "CURRENCY" => SecurityTypes.Currency,
			"METAL" => SecurityTypes.Commodity,
			"COMMODITY" => SecurityTypes.Commodity,
			"INDEX" => SecurityTypes.Index,
			"STOCK" or "CFD_STOCK" => SecurityTypes.Stock,
			"ETF" => SecurityTypes.Etf,
			"CRYPTO" or "CRYPTOCURRENCY" => SecurityTypes.CryptoCurrency,
			"BOND" => SecurityTypes.Bond,
			_ => null,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency) ? currency : null;

	public static string ToNative(this DukasCopyOrderCommands command, Sides side, OrderTypes orderType)
		=> command switch
		{
			DukasCopyOrderCommands.Auto => (side, orderType) switch
			{
				(Sides.Buy, OrderTypes.Market) => "BUY",
				(Sides.Sell, OrderTypes.Market) => "SELL",
				(Sides.Buy, OrderTypes.Limit) => "BUYLIMIT",
				(Sides.Sell, OrderTypes.Limit) => "SELLLIMIT",
				(Sides.Buy, OrderTypes.Conditional) => "BUYSTOP",
				(Sides.Sell, OrderTypes.Conditional) => "SELLSTOP",
				_ => throw new NotSupportedException($"JForex does not support StockSharp order type '{orderType}'."),
			},
			DukasCopyOrderCommands.BuyLimitByBid => "BUYLIMIT_BYBID",
			DukasCopyOrderCommands.SellLimitByAsk => "SELLLIMIT_BYASK",
			DukasCopyOrderCommands.BuyStopByBid => "BUYSTOP_BYBID",
			DukasCopyOrderCommands.SellStopByAsk => "SELLSTOP_BYASK",
			DukasCopyOrderCommands.PlaceBid => "PLACE_BID",
			DukasCopyOrderCommands.PlaceOffer => "PLACE_OFFER",
			_ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
		};

	public static Sides ToSide(this string command)
		=> command?.ToUpperInvariant().StartsWith("SELL", StringComparison.Ordinal) == true ||
			command?.Equals("PLACE_OFFER", StringComparison.OrdinalIgnoreCase) == true
				? Sides.Sell : Sides.Buy;

	public static OrderTypes ToOrderType(this string command)
	{
		command = command?.ToUpperInvariant();
		if (command is "BUY" or "SELL")
			return OrderTypes.Market;
		if (command?.Contains("STOP", StringComparison.Ordinal) == true)
			return OrderTypes.Conditional;
		return OrderTypes.Limit;
	}

	public static OrderStates ToOrderState(this string state)
		=> state?.ToUpperInvariant() switch
		{
			"CREATED" => OrderStates.Pending,
			"OPENED" => OrderStates.Active,
			"FILLED" or "CANCELED" or "CLOSED" => OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			_ => OrderStates.Pending,
		};

	public static DateTime ToUtc(this long milliseconds)
		=> milliseconds > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime : DateTime.UtcNow;

	public static decimal Mid(decimal bid, decimal ask)
		=> bid > 0 && ask > 0 ? (bid + ask) / 2 : Math.Max(bid, ask);
}
