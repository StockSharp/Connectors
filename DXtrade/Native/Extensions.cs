namespace StockSharp.DXtrade.Native;

static class Extensions
{
	private static readonly PairSet<Sides, string> _sidesMap = new()
	{
		{ Sides.Buy, "BUY" },
		{ Sides.Sell, "SELL" }
	};

	public static string ToNative(this Sides side)
	{
		if (!_sidesMap.TryGetValue(side, out var nativeSide))
			throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);

		return nativeSide;
	}

	public static Sides? ToSide(this string side)
	{
		if (side.IsEmpty())
			return null;
		
		if (!_sidesMap.TryGetKey(side.ToUpperInvariant(), out var stockSharpSide))
			throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);

		return stockSharpSide;
	}

	private static readonly PairSet<OrderPositionEffects, string> _positionEffectsMap = new()
	{
		{ OrderPositionEffects.OpenOnly, "OPEN" },
		{ OrderPositionEffects.CloseOnly, "CLOSE" }
	};

	public static string ToNative(this OrderPositionEffects positionEffect)
	{
		if (!_positionEffectsMap.TryGetValue(positionEffect, out var nativeEffect))
			throw new ArgumentOutOfRangeException(nameof(positionEffect), positionEffect, LocalizedStrings.InvalidValue);
		
		return nativeEffect;
	}

	public static OrderPositionEffects? ToPositionEffect(this string positionEffect)
	{
		if (positionEffect.IsEmpty())
			return null;
		
		if (!_positionEffectsMap.TryGetKey(positionEffect.ToUpperInvariant(), out var stockSharpEffect))
			throw new ArgumentOutOfRangeException(nameof(positionEffect), positionEffect, LocalizedStrings.InvalidValue);

		return stockSharpEffect;
	}

	private static readonly PairSet<OrderTypes, string> _orderTypesMap = new()
	{
		{ OrderTypes.Limit, "LIMIT" },
		{ OrderTypes.Market, "MARKET" },
		{ OrderTypes.Conditional, "STOP" }
	};

	public static string ToNative(this OrderTypes type)
	{
		if (!_orderTypesMap.TryGetValue(type, out var nativeType))
			throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);

		return nativeType;
	}

	public static OrderTypes? ToOrderType(this string type)
	{
		if (type.IsEmpty())
			return null;

		if (!_orderTypesMap.TryGetKey(type.ToUpperInvariant(), out var stockSharpType))
			throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);

		return stockSharpType;
	}

	private static readonly PairSet<TimeInForce, string> _timeInForceMap = new()
	{
		{ TimeInForce.PutInQueue, "GTC" },
		{ TimeInForce.MatchOrCancel, "FOK" },
		{ TimeInForce.CancelBalance, "IOC" }
	};

	private const string _gtd = "GTD";
	private const string _day = "DAY";

	public static string ToNative(this TimeInForce? timeInForce, DateTime? expiry)
	{
		if (expiry is not null)
			return expiry.Value.IsToday() ? _day : _gtd;

		if (timeInForce is null)
			return null;

		if (!_timeInForceMap.TryGetValue(timeInForce.Value, out var nativeTimeInForce))
			throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue);

		return nativeTimeInForce;
	}

	public static TimeInForce? ToTimeInForce(this string timeInForce)
	{
		if (timeInForce.IsEmpty() || timeInForce.EqualsIgnoreCase(_gtd) || timeInForce.EqualsIgnoreCase(_day))
			return null;

		if (!_timeInForceMap.TryGetKey(timeInForce.ToUpperInvariant(), out var stockSharpTimeInForce))
			throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue);

		return stockSharpTimeInForce;
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromDays(1), "d" },
		{ TimeSpan.FromDays(7), "w" },
		{ TimeSpan.FromDays(30), "mo" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		if (!TimeFrames.TryGetValue(timeFrame, out var name))
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

		return name;
	}

	public static TimeSpan ToTimeSpan(this string timeFrame)
	{
		if (!TimeFrames.TryGetKey(timeFrame, out var value))
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

		return value;
	}

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = BoardCodes.DevExperts,
		};

	public static string ToNative(this SecurityId securityId)
		=> securityId.SecurityCode;

	public static OrderStates? ToOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			null or "" => null,
			"ACCEPTED" => OrderStates.Pending,
			"WORKING" => OrderStates.Active,
			"CANCELED" => OrderStates.Done,
			"COMPLETED" => OrderStates.Done,
			"EXPIRED" => OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};

	private static readonly PairSet<SecurityTypes, string> _securityTypesMap = new()
	{
		{ SecurityTypes.Currency, "FOREX" },
		{ SecurityTypes.Stock, "STOCK" },
		{ SecurityTypes.Future, "FUTURES" },
		{ SecurityTypes.Option, "OPTION" },
		{ SecurityTypes.Index, "INDEX" },
		//{ SecurityTypes.Currency, "CURRENCY" },
		{ SecurityTypes.Cfd, "CFD" },
		{ SecurityTypes.CryptoCurrency, "CRYPTO_CURRENCY" },
		{ SecurityTypes.Spread, "SPREAD_BET" }
	};

	public static string ToNative(this SecurityTypes securityType)
	{
		if (!_securityTypesMap.TryGetValue(securityType, out var nativeType))
			throw new ArgumentOutOfRangeException(nameof(securityType), securityType, LocalizedStrings.InvalidValue);

		return nativeType;
	}

	public static SecurityTypes? ToSecurityType(this string type)
	{
		if (type.IsEmpty())
			return null;

		return _securityTypesMap.TryGetKey(type.ToUpperInvariant(), out var stockSharpType) ? stockSharpType : null;
	}

	public static string ToTimeStamp(this DateTime dt)
		=> dt.ToString("yyyy-MM-dd'T'HH:mm:ss.ffZ");
}