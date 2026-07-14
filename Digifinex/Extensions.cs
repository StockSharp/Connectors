namespace StockSharp.Digifinex;

static class Extensions
{
	private static readonly Dictionary<int, string> _errorDescriptions = new()
	{
		{ 10001, "Wrong request method, please check it's a GET ot POST request" },
		{ 10002, "Invalid ApiKey" },
		{ 10003, "Sign doesn't match" },
		{ 10004, "Illegal request parameters" },
		{ 10005, "Request frequency exceeds the limit" },
		{ 10006, "Unauthorized to execute this request" },
		{ 10007, "IP address Unauthorized" },
		{ 10008, "Timestamp for this request is invalid" },
		{ 10009, "Unexist endpoint, please check endpoint URL" },
		{ 10011, "ApiKey expired. Please go to client side to re-create an ApiKey." },
		{ 20001, "Trade is not open for this trading pair" },
		{ 20002, "Trade of this trading pair is suspended" },
		{ 20003, "Invalid price or amount" },
		{ 20004, "Price exceeds daily limit" },
		{ 20005, "Price exceeds down limit" },
		{ 20006, "Cash Amount is less than 10CNY" },
		{ 20007, "Price precision error" },
		{ 20008, "Amount precision error" },
		{ 20009, "Amount is less than the minimum requirement" },
		{ 20010, "Cash Amount is less than the minimum requirement" },
		{ 20011, "Insufficient balance" },
		{ 20012, "Invalid trade type (valid value: buy/sell)" },
		{ 20013, "No such order" },
		{ 20014, "Invalid date (Valid format: 2018-07-25)" },
		{ 20015, "Dates exceed the limit" },
		{ 20018, "Your trading rights have been banned by the system" },
		{ 20019, "Wrong trading pair symbol, correct format:'usdt_btc', quote asset is in the front" },
		{ 20020, "You have violated the API operation trading rules and temporarily forbid trading. At present, we have certain restrictions on the user's transaction rate and withdrawal rate." },
		{ 50000, "Exception error" },
	};

	public static string GetErrorText(this int code)
	{
		return _errorDescriptions.TryGetValue(code, out var text) ? text : code.To<string>();
	}

	public static string ToNative(this Sides side, bool isMarket)
	{
		return side switch
		{
			Sides.Buy => "buy" + (isMarket ? "_market" : string.Empty),
			Sides.Sell => "sell" + (isMarket ? "_market" : string.Empty),
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides ToSide(this string side, out bool isMarket)
	{
		isMarket = false;

		switch (side)
		{
			case "buy":
				return Sides.Buy;
			case "buy_market":
			{
				isMarket = true;
				return Sides.Buy;
			}
			case "sell":
				return Sides.Sell;
			case "sell_market":
			{
				isMarket = true;
				return Sides.Sell;
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
		}
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1" },
		{ TimeSpan.FromMinutes(5), "5" },
		{ TimeSpan.FromMinutes(15), "15" },
		{ TimeSpan.FromMinutes(30), "30" },
		{ TimeSpan.FromHours(1), "60" },
		{ TimeSpan.FromHours(4), "240" },
		{ TimeSpan.FromHours(12), "720" },
		{ TimeSpan.FromDays(1), "1D" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1W" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		return TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		return TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
	}

	public static string ToCurrency(this SecurityId securityId)
	{
		return securityId.SecurityCode.ToLowerInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		return new()
		{
			SecurityCode = currency.ToUpperInvariant(),
			BoardCode = BoardCodes.Digifinex,
		};
	}

	public static decimal GetBalance(this Order order)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));

		return (decimal)(order.Amount - order.ExecutedAmount.Value);
	}

	public static OrderStates ToOrderState(this int status)
	{
		return status switch
		{
			// unfilled
			0 or 1 => OrderStates.Active,
			// fulfilled
			2 => OrderStates.Done,
			// unfilled and cancelled
			3 or 4 => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}

	public static DateTime GetTime(this Native.Model.Trade trade)
	{
		return (trade.Time ?? trade.Date).Value;
	}
}