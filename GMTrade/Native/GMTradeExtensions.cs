namespace StockSharp.GMTrade.Native;

sealed class GMTradeMarketInfo
{
	public string Code { get; init; }
	public GMTradeMarket Market { get; set; }

	public string MarketToken => Market.MarketToken;
	public string IndexToken => Market.Meta.IndexToken.PublicKey;
	public int IndexDecimals => Market.Meta.IndexToken.Meta.Decimals;
	public int PricePrecision => Market.Meta.IndexToken.Meta.Precision;
	public string IndexSymbol => Market.Meta.IndexToken.GetSymbol();
	public string Name => Market.Meta.Name;
}

sealed class GMTradeTokenInfo
{
	public string Mint { get; init; }
	public string Symbol { get; init; }
	public int Decimals { get; init; }
}

static class GMTradeExtensions
{
	private const int _marketDecimals = 20;
	private const int _candleDecimals = 18;
	private const string _base58Alphabet =
		"123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static int ToResolution(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => 60,
			{ TotalMinutes: 5 } => 300,
			{ TotalMinutes: 15 } => 900,
			{ TotalHours: 1 } => 3600,
			{ TotalHours: 2 } => 7200,
			{ TotalHours: 4 } => 14400,
			{ TotalDays: 1 } => 86400,
			{ TotalDays: 7 } => 604800,
			{ TotalDays: 30 } => 2592000,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Unsupported GMTrade candle time frame."),
		};

	public static decimal FromOraclePrice(this string value, int tokenDecimals,
		string fieldName)
	{
		if (tokenDecimals is < 0 or > 18)
			throw new InvalidDataException(
				$"GMTrade returned invalid token decimals {tokenDecimals}.");
		return value.FromFixed(_marketDecimals - tokenDecimals, fieldName);
	}

	public static decimal FromMarketUsd(this string value, string fieldName)
		=> value.FromFixed(_marketDecimals, fieldName);

	public static decimal FromCandlePrice(this string value, string fieldName)
		=> value.FromFixed(_candleDecimals, fieldName);

	public static decimal FromTokenAmount(this string value, int decimals,
		string fieldName)
	{
		if (decimals is < 0 or > 18)
			throw new InvalidDataException(
				$"GMTrade returned invalid token decimals {decimals}.");
		return value.FromFixed(decimals, fieldName);
	}

	public static decimal FromFixed(this string value, int decimals,
		string fieldName)
	{
		if (value.IsEmpty() || !BigInteger.TryParse(value,
			NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
			throw new InvalidDataException(
				$"GMTrade returned invalid {fieldName} '{value}'.");
		try
		{
			return (decimal)number / Pow10(decimals);
		}
		catch (OverflowException error)
		{
			throw new InvalidDataException(
				$"GMTrade {fieldName} is outside the decimal range.", error);
		}
	}

	public static decimal? TryFromOraclePrice(this string value,
		int tokenDecimals)
	{
		try
		{
			return value.IsEmpty()
				? null
				: value.FromOraclePrice(tokenDecimals, "oracle price");
		}
		catch (InvalidDataException)
		{
			return null;
		}
	}

	public static decimal PriceStep(int precision)
	{
		if (precision is < 0 or > 18)
			throw new InvalidDataException(
				$"GMTrade returned invalid price precision {precision}.");
		return 1m / Pow10(precision);
	}

	private static decimal Pow10(int exponent)
	{
		if (exponent is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(exponent));
		var value = 1m;
		for (var i = 0; i < exponent; i++)
			value *= 10m;
		return value;
	}

	public static DateTime ToUtcTime(this long unixSeconds)
	{
		if (unixSeconds <= 0)
			throw new InvalidDataException(
				$"GMTrade returned invalid Unix timestamp {unixSeconds}.");
		return unixSeconds.FromUnix();
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string GetSymbol(this GMTradeToken token)
	{
		var value = token?.Meta?.DisplaySymbol;
		if (value.IsEmpty())
			value = token?.Meta?.Name;
		return NormalizeSymbol(value, "TOKEN");
	}

	public static string CreateSecurityCode(string marketName)
	{
		marketName = marketName.ThrowIfEmpty(nameof(marketName)).Trim();
		var builder = new StringBuilder(marketName.Length);
		var isSeparator = false;
		foreach (var character in marketName)
		{
			if (char.IsLetterOrDigit(character))
			{
				if (isSeparator && builder.Length > 0)
					builder.Append('-');
				builder.Append(char.ToUpperInvariant(character));
				isSeparator = false;
			}
			else
			{
				isSeparator = true;
			}
		}
		var code = builder.ToString().Trim('-');
		return code.IsEmpty()
			? throw new InvalidDataException(
				"GMTrade returned a market with no usable name.")
			: code;
	}

	public static string NormalizeSymbol(string value, string fallback)
	{
		if (value.IsEmpty())
			return fallback;
		var chars = value.Trim().Where(char.IsLetterOrDigit)
			.Select(char.ToUpperInvariant).ToArray();
		return chars.Length == 0 ? fallback : new string(chars);
	}

	public static SecurityId ToStockSharp(this GMTradeMarketInfo market)
		=> new()
		{
			SecurityCode = market.Code,
			BoardCode = BoardCodes.GMTrade,
		};

	public static SecurityId ToCurrencySecurity(this string currency)
		=> new()
		{
			SecurityCode = currency.ThrowIfEmpty(nameof(currency)).Trim()
				.ToUpperInvariant(),
			BoardCode = BoardCodes.GMTrade,
		};

	public static bool IsIncrease(this GMTradeOrderKinds kind)
		=> kind is GMTradeOrderKinds.MarketIncrease or
			GMTradeOrderKinds.LimitIncrease;

	public static bool IsDecrease(this GMTradeOrderKinds kind)
		=> kind is GMTradeOrderKinds.MarketDecrease or
			GMTradeOrderKinds.LimitDecrease or
			GMTradeOrderKinds.StopLossDecrease or
			GMTradeOrderKinds.Liquidation or
			GMTradeOrderKinds.AutoDeleveraging;

	public static OrderTypes ToStockSharp(this GMTradeOrderKinds kind)
		=> kind switch
		{
			GMTradeOrderKinds.MarketSwap or
			GMTradeOrderKinds.MarketIncrease or
			GMTradeOrderKinds.MarketDecrease => OrderTypes.Market,
			GMTradeOrderKinds.LimitSwap or
			GMTradeOrderKinds.LimitIncrease or
			GMTradeOrderKinds.LimitDecrease => OrderTypes.Limit,
			GMTradeOrderKinds.StopLossDecrease or
			GMTradeOrderKinds.Liquidation or
			GMTradeOrderKinds.AutoDeleveraging => OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
				"Unsupported GMTrade order kind."),
		};

	public static OrderStates ToStockSharp(this GMTradeActionStates state)
		=> state switch
		{
			GMTradeActionStates.Pending => OrderStates.Active,
			GMTradeActionStates.Completed or
			GMTradeActionStates.Cancelled => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(state), state,
				"Unsupported GMTrade action state."),
		};

	public static Sides ToStockSharp(this GMTradePositionSides side)
		=> side switch
		{
			GMTradePositionSides.Long => Sides.Buy,
			GMTradePositionSides.Short => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported GMTrade position side."),
		};

	public static Sides GetExecutionSide(this GMTradeTrade trade)
	{
		if (!int.TryParse(trade.Flags, NumberStyles.None,
			CultureInfo.InvariantCulture, out var flags) || flags is < 0 or > 255)
			throw new InvalidDataException(
				$"GMTrade returned invalid trade flags '{trade.Flags}'.");
		var isLong = (flags & 1) != 0;
		var isIncrease = (flags & 4) != 0;
		return isLong == isIncrease ? Sides.Buy : Sides.Sell;
	}

	public static string NormalizePublicKey(this string value,
		string parameterName)
	{
		value = value.ThrowIfEmpty(parameterName).Trim();
		if (value.Length is < 32 or > 44)
			throw new ArgumentException(
				"A Solana public key must contain 32 decoded bytes.", parameterName);

		Span<byte> decoded = stackalloc byte[32];
		foreach (var character in value)
		{
			var carry = _base58Alphabet.IndexOf(character);
			if (carry < 0)
				throw new ArgumentException(
					"A Solana public key must use canonical base58.", parameterName);
			for (var index = decoded.Length - 1; index >= 0; index--)
			{
				carry += decoded[index] * 58;
				decoded[index] = (byte)carry;
				carry >>= 8;
			}
			if (carry != 0)
				throw new ArgumentException(
					"A Solana public key must contain 32 decoded bytes.",
					parameterName);
		}

		var encodedZeroes = value.TakeWhile(static c => c == '1').Count();
		var decodedZeroes = 0;
		while (decodedZeroes < decoded.Length && decoded[decodedZeroes] == 0)
			decodedZeroes++;
		if (encodedZeroes != decodedZeroes)
			throw new ArgumentException(
				"A Solana public key must use canonical base58.", parameterName);
		return value;
	}
}
