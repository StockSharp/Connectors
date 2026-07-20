namespace StockSharp.Reya.Native;

static class ReyaExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames = new()
	{
		[TimeSpan.FromMinutes(1)] = "1m",
		[TimeSpan.FromMinutes(5)] = "5m",
		[TimeSpan.FromMinutes(15)] = "15m",
		[TimeSpan.FromMinutes(30)] = "30m",
		[TimeSpan.FromHours(1)] = "1h",
		[TimeSpan.FromHours(4)] = "4h",
		[TimeSpan.FromDays(1)] = "1d",
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToReyaInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new NotSupportedException(
				"Reya does not support the " + timeFrame + " candle interval.");

	public static DateTime EnsureReyaUtc(this DateTime time)
		=> time.Kind switch
		{
			DateTimeKind.Utc => time,
			DateTimeKind.Local => time.ToUniversalTime(),
			_ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
		};

	public static DateTime FromReyaMilliseconds(this long timestamp)
	{
		if (timestamp <= 0)
			throw new InvalidDataException(
				"Reya returned a non-positive millisecond timestamp.");
		try
		{
			return DateTime.UnixEpoch.AddMilliseconds(timestamp);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"Reya returned an out-of-range millisecond timestamp.", error);
		}
	}

	public static DateTime FromReyaMillisecondsOrNow(this long timestamp)
		=> timestamp > 0 ? timestamp.FromReyaMilliseconds() : DateTime.UtcNow;

	public static DateTime FromReyaSeconds(this long timestamp)
	{
		if (timestamp <= 0)
			throw new InvalidDataException(
				"Reya returned a non-positive second timestamp.");
		try
		{
			return DateTime.UnixEpoch.AddSeconds(timestamp);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"Reya returned an out-of-range second timestamp.", error);
		}
	}

	public static long ToReyaMilliseconds(this DateTime time)
		=> checked((long)(time.EnsureReyaUtc() - DateTime.UnixEpoch)
			.TotalMilliseconds);

	public static long ToReyaSeconds(this DateTime time)
		=> checked((long)(time.EnsureReyaUtc() - DateTime.UnixEpoch)
			.TotalSeconds);

	public static decimal ParseReyaDecimal(this string value, string field,
		bool isZeroAllowed = false)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ||
			(isZeroAllowed ? result < 0 : result <= 0))
			throw new InvalidDataException(
				"Reya returned an invalid " + field + ".");
		return result;
	}

	public static decimal? TryParseReyaDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static string ToReyaDecimal(this decimal value)
		=> value.ToString("G29", CultureInfo.InvariantCulture);

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static BigInteger ParseReyaInteger(this string value, string field)
	{
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var result) || result < 0)
			throw new InvalidDataException(
				"Reya " + field + " is invalid.");
		return result;
	}

	public static BigInteger ToScaledInteger(this string value, int decimals,
		string field)
	{
		if (decimals < 0)
			throw new ArgumentOutOfRangeException(nameof(decimals), decimals, null);
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var isNegative = value[0] == '-';
		if (isNegative || value[0] == '+')
			value = value[1..];
		if (value.IsEmpty())
			throw new InvalidDataException("Reya " + field + " is invalid.");

		var exponent = 0;
		var exponentIndex = value.IndexOfAny(['e', 'E']);
		if (exponentIndex >= 0)
		{
			if (!int.TryParse(value[(exponentIndex + 1)..],
				NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
				out exponent))
				throw new InvalidDataException(
					"Reya " + field + " exponent is invalid.");
			value = value[..exponentIndex];
		}

		var pointIndex = value.IndexOf('.');
		if (pointIndex != value.LastIndexOf('.'))
			throw new InvalidDataException("Reya " + field + " is invalid.");
		var fractionDigits = pointIndex < 0 ? 0 : value.Length - pointIndex - 1;
		var digits = pointIndex < 0 ? value : value.Remove(pointIndex, 1);
		if (digits.IsEmpty() || digits.Any(static ch => !char.IsDigit(ch)) ||
			!BigInteger.TryParse(digits, NumberStyles.None,
				CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException("Reya " + field + " is invalid.");

		var scale = checked(decimals + exponent - fractionDigits);
		if (scale >= 0)
			result *= BigInteger.Pow(10, scale);
		else
		{
			var divisor = BigInteger.Pow(10, -scale);
			var remainder = result % divisor;
			if (remainder != 0)
				throw new InvalidOperationException(
					"Reya " + field + " has more than " + decimals +
					" supported decimals.");
			result /= divisor;
		}
		return isNegative ? -result : result;
	}

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = BoardCodes.Reya,
		};

	public static Sides ToStockSharp(this ReyaSides side)
		=> side switch
		{
			ReyaSides.Buy => Sides.Buy,
			ReyaSides.Sell => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static ReyaSides ToReya(this Sides side)
		=> side switch
		{
			Sides.Buy => ReyaSides.Buy,
			Sides.Sell => ReyaSides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
		};

	public static OrderStates ToStockSharp(this ReyaOrderStates state)
		=> state switch
		{
			ReyaOrderStates.Open => OrderStates.Active,
			ReyaOrderStates.Filled or ReyaOrderStates.Cancelled => OrderStates.Done,
			ReyaOrderStates.Rejected => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
		};

	public static OrderTypes ToStockSharp(this ReyaOrderTypes type)
		=> type == ReyaOrderTypes.Limit
			? OrderTypes.Limit
			: OrderTypes.Conditional;

	public static SecurityTypes ToStockSharp(this ReyaMarket market)
		=> market.IsSpot ? SecurityTypes.CryptoCurrency : SecurityTypes.Future;

	public static ReyaMarket ToMarket(this ReyaPerpetualMarketDefinition value)
	{
		if (value?.Symbol.IsEmpty() != false || value.MarketId < 0)
			throw new InvalidDataException(
				"Reya returned an invalid perpetual market definition.");
		var (baseAsset, quoteAsset) = SplitPerpetualSymbol(value.Symbol);
		return new()
		{
			Symbol = value.Symbol,
			MarketId = value.MarketId,
			BaseAsset = baseAsset,
			QuoteAsset = quoteAsset,
			IsSpot = false,
			MinimumQuantity = value.MinimumOrderQuantity.ParseReyaDecimal(
				"minimum order quantity"),
			QuantityStep = value.QuantityStep.ParseReyaDecimal("quantity step"),
			PriceStep = value.PriceStep.ParseReyaDecimal("price step"),
			MaximumLeverage = value.MaximumLeverage > 0
				? value.MaximumLeverage
				: null,
		};
	}

	public static ReyaMarket ToMarket(this ReyaSpotMarketDefinition value)
	{
		if (value?.Symbol.IsEmpty() != false || value.MarketId < 0 ||
			value.BaseAsset.IsEmpty() || value.QuoteAsset.IsEmpty())
			throw new InvalidDataException(
				"Reya returned an invalid spot market definition.");
		return new()
		{
			Symbol = value.Symbol,
			MarketId = value.MarketId,
			BaseAsset = value.BaseAsset,
			QuoteAsset = value.QuoteAsset,
			IsSpot = true,
			MinimumQuantity = value.MinimumOrderQuantity.ParseReyaDecimal(
				"minimum order quantity"),
			QuantityStep = value.QuantityStep.ParseReyaDecimal("quantity step"),
			PriceStep = value.PriceStep.ParseReyaDecimal("price step"),
		};
	}

	private static (string BaseAsset, string QuoteAsset) SplitPerpetualSymbol(
		string symbol)
	{
		const string suffix = "RUSDPERP";
		if (symbol.EndsWith(suffix, StringComparison.Ordinal) &&
			symbol.Length > suffix.Length)
			return (symbol[..^suffix.Length], "RUSD");
		const string perpetual = "PERP";
		if (symbol.EndsWith(perpetual, StringComparison.Ordinal) &&
			symbol.Length > perpetual.Length)
			return (symbol[..^perpetual.Length], "RUSD");
		throw new InvalidDataException(
			"Reya returned an unsupported perpetual symbol '" + symbol + "'.");
	}
}
