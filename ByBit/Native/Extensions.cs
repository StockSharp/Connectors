namespace StockSharp.ByBit.Native;

static class Extensions
{
	public static string ToNative(this Sides side)
		=> side switch
		{
			Sides.Buy => "Buy",
			Sides.Sell => "Sell",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides? ToSide(this string side)
		=> side?.ToLowerInvariant() switch
		{
			null or "" => null,
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static string ToNative(this OrderTypes type)
		=> type switch
		{
			OrderTypes.Limit => "Limit",
			OrderTypes.Market => "Market",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this string type)
		=> type?.ToLowerInvariant() switch
		{
			"limit" => OrderTypes.Limit,
			"market" => OrderTypes.Market,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static string ToNative(this TimeInForce? timeInForce, bool? postOnly)
	{
		if (postOnly == true)
			return "PostOnly";

		return timeInForce switch
		{
			TimeInForce.PutInQueue or null => "GTC",
			TimeInForce.MatchOrCancel => "FOK",
			TimeInForce.CancelBalance => "IOC",
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue),
		};
	}

	public static TimeInForce? ToTif(this string timeInForce, out bool? postOnly)
	{
		postOnly = null;

		if (timeInForce.EqualsIgnoreCase("postOnly"))
		{
			postOnly = true;
			return null;
		}

		return timeInForce?.ToUpperInvariant() switch
		{
			"GTC" => TimeInForce.PutInQueue,
			"FOK" => TimeInForce.MatchOrCancel,
			"IOC" => TimeInForce.CancelBalance,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue),
		};
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1" },
		{ TimeSpan.FromMinutes(3), "3" },
		{ TimeSpan.FromMinutes(5), "5" },
		{ TimeSpan.FromMinutes(15), "15" },
		{ TimeSpan.FromMinutes(30), "30" },
		{ TimeSpan.FromHours(1), "60" },
		{ TimeSpan.FromHours(2), "120" },
		{ TimeSpan.FromHours(4), "240" },
		{ TimeSpan.FromHours(6), "360" },
		{ TimeSpan.FromHours(12), "720" },
		{ TimeSpan.FromDays(1), "d" },
		{ TimeSpan.FromDays(7), "w" },
		{ TimeSpan.FromDays(30), "m" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		if (!TimeFrames.TryGetValue(timeFrame, out var name))
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

		return name;
	}

	public static TimeSpan ToTimeFrame(this string timeFrame)
	{
		if (!TimeFrames.TryGetKey(timeFrame?.ToLowerInvariant(), out var value))
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

		return value;
	}

	private static readonly PairSet<ByBitSections, string> _boardCodes = new()
	{
		{ ByBitSections.Spot, BoardCodes.ByBit },
		{ ByBitSections.Linear, BoardCodes.ByBitLin },
		{ ByBitSections.Inverse, BoardCodes.ByBitInv },
		{ ByBitSections.Options, BoardCodes.ByBitOpt },
	};

	private static string ToBoardCode(this ByBitSections section)
		=> _boardCodes[section];

	public static ByBitSections ToSection(this SecurityId secId)
		=> _boardCodes[secId.BoardCode];

	public static string ToCategory(this SecurityId secId)
		=> secId.ToSection().ToNative();

	public static SecurityId ToStockSharp(this string symbol, ByBitSections section = ByBitSections.Spot)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = section.ToBoardCode(),
		};

	public static string ToNative(this SecurityId securityId)
	{
		if (securityId.IsAllSecurity())
			return string.Empty;

		return securityId.SecurityCode;
	}

	public static string ToNative(this ByBitSections section)
		=> section switch
		{
			ByBitSections.Spot => "spot",
			ByBitSections.Linear => "linear",
			ByBitSections.Inverse => "inverse",
			ByBitSections.Options => "option",
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, LocalizedStrings.InvalidValue),
		};

	public static OrderStates? ToOrderState(this string status)
		=> status?.ToLowerInvariant() switch
		{
			null => null,
			"created" => OrderStates.Pending,
			"new" or "partfilled" => OrderStates.Active,
			"filled" or "cancelled" => OrderStates.Done,
			"rejected" => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};

	public static OptionTypes ToOptionType(this string type)
		=> type?.ToLowerInvariant() switch
		{
			"call" => OptionTypes.Call,
			"put" => OptionTypes.Put,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static bool? ToUpTick(this string type)
		=> type?.ToLowerInvariant() switch
		{
			"plustick" or "zeroplustick" => true,
			"minustick" or "zerominustick" => false,
			_ => null,
		};

	public static bool ToNative(this OrderPositionEffects effect)
		=> effect switch
		{
			OrderPositionEffects.CloseOnly => true,
			_ => false,
		};

	public static string ToNative(this ByBitMarketUnits marketUnit)
		=> marketUnit switch
		{
			ByBitMarketUnits.BaseCoin => "baseCoin",
			ByBitMarketUnits.QuoteCoin => "quoteCoin",
			_ => throw new ArgumentOutOfRangeException(nameof(marketUnit), marketUnit, LocalizedStrings.InvalidValue),
		};

	public static int ToNative(this ByBitTriggerDirections triggerDirection)
		=> triggerDirection switch
		{
			ByBitTriggerDirections.Rise => 1,
			ByBitTriggerDirections.Fall => 2,
			_ => throw new ArgumentOutOfRangeException(nameof(triggerDirection), triggerDirection, LocalizedStrings.InvalidValue),
		};

	public static ByBitTriggerDirections? ToTriggerDirection(this int triggerDirection)
		=> triggerDirection switch
		{
			0 => null,
			1 => ByBitTriggerDirections.Rise,
			2 => ByBitTriggerDirections.Fall,
			_ => throw new ArgumentOutOfRangeException(nameof(triggerDirection), triggerDirection, LocalizedStrings.InvalidValue),
		};

	public static string ToNative(this ByBitTriggerBy triggerBy)
		=> triggerBy switch
		{
			ByBitTriggerBy.LastPrice => "LastPrice",
			ByBitTriggerBy.IndexPrice => "IndexPrice",
			ByBitTriggerBy.MarkPrice => "MarkPrice",
			_ => throw new ArgumentOutOfRangeException(nameof(triggerBy), triggerBy, LocalizedStrings.InvalidValue),
		};

	public static ByBitTriggerBy? ToTriggerBy(this string triggerBy)
		=> triggerBy?.ToLowerInvariant() switch
		{
			"" or null => null,
			"lastprice" => ByBitTriggerBy.LastPrice,
			"indexprice" => ByBitTriggerBy.IndexPrice,
			"markprice" => ByBitTriggerBy.MarkPrice,
			_ => throw new ArgumentOutOfRangeException(nameof(triggerBy), triggerBy, LocalizedStrings.InvalidValue),
		};

	public static int ToNative(this ByBitPositionIdx positionIdx)
		=> positionIdx switch
		{
			ByBitPositionIdx.OneWay => 0,
			ByBitPositionIdx.BuySide => 1,
			ByBitPositionIdx.SellSide => 2,
			_ => throw new ArgumentOutOfRangeException(nameof(positionIdx), positionIdx, LocalizedStrings.InvalidValue),
		};

	public static ByBitPositionIdx ToPositionIdx(this int positionIdx)
		=> positionIdx switch
		{
			0 => ByBitPositionIdx.OneWay,
			1 => ByBitPositionIdx.BuySide,
			2 => ByBitPositionIdx.SellSide,
			_ => throw new ArgumentOutOfRangeException(nameof(positionIdx), positionIdx, LocalizedStrings.InvalidValue),
		};

	public static string ToNative(this ByBitSmpTypes smpType)
		=> smpType.ToString();

	public static ByBitSmpTypes? ToSmpType(this string smpType)
	{
		if (smpType.TryParse<ByBitSmpTypes>(out var type))
			return type;

		return null;
	}

	public static string ToNative(this ByBitTpSlModes tpSlMode)
		=> tpSlMode.ToString();

	public static ByBitTpSlModes? ToTpSlMode(this string tpSlMode)
	{
		if (tpSlMode.TryParse<ByBitTpSlModes>(out var mode))
			return mode;

		return null;
	}
}