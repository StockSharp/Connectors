namespace StockSharp.Cex;

static class Extensions
{
	public static string ToNative(this Sides side)
	{
            return side switch
            {
                Sides.Buy => "buy",
                Sides.Sell => "sell",
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
            };
        }

	public static Sides ToSide(this string side)
	{
            return side switch
            {
                "bid" or "buy" => Sides.Buy,
                "ask" or "sell" => Sides.Sell,
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
            };
        }

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		//{ TimeSpan.FromMinutes(3), "3m" },
		//{ TimeSpan.FromMinutes(5), "5m" },
		//{ TimeSpan.FromMinutes(15), "15m" },
		//{ TimeSpan.FromMinutes(30), "30m" },
		//{ TimeSpan.FromHours(1), "1h" },
		//{ TimeSpan.FromHours(2), "2h" },
		//{ TimeSpan.FromHours(4), "4h" },
		//{ TimeSpan.FromHours(6), "6h" },
		//{ TimeSpan.FromHours(12), "12h" },
		//{ TimeSpan.FromDays(1), "1d" },
		//{ TimeSpan.FromDays(3), "3d" },
		//{ TimeSpan.FromDays(7), "1w" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		return TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		return TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
	}

	public static string[] ToCcy(this SecurityId securityId)
	{
		var parts = securityId.SecurityCode.Split('/');

		if (parts.Length != 2)
			throw new ArgumentException(securityId.ToString());

		return parts;
	}

	//public static string ToCurrency(this SecurityId securityId)
	//{
	//	return securityId.SecurityCode.Replace('/', ':').ToLowerInvariant();
	//}

	public static SecurityId ToStockSharp(this string[] currency)
	{
		if (currency == null)
			throw new ArgumentNullException(nameof(currency));

		if (currency.Length != 2)
			throw new ArgumentOutOfRangeException(nameof(currency), currency.Length, LocalizedStrings.InvalidValue);

		return new SecurityId
		{
			SecurityCode = (currency[0] + "/" + currency[1]).ToUpperInvariant(),
			BoardCode = BoardCodes.Cex,
		};
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		return new SecurityId
		{
			SecurityCode = currency.Replace(':', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.Cex,
		};
	}

	public static OrderStates ToOrderState(this int status)
	{
		switch (status)
		{
			case -1: // cancelled
				return OrderStates.Done;

			case 0: // unfilled
			case 1: // partially filled
				return OrderStates.Active;

			case 2: // fully filled
				return OrderStates.Done;

			case 4: // cancel request in process
				return OrderStates.Active;

			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
		}
	}
}