namespace StockSharp.Oanda;

static class Extensions
{
	public static string ToOanda(this SecurityId securityId)
	{
		if (securityId.SecurityCode.IsEmpty())
			throw new ArgumentException(nameof(securityId));

		return securityId.SecurityCode.Replace('/', '_');
	}

	public static SecurityId ToStockSharp(this string instrument)
	{
		if (instrument.IsEmpty())
			throw new ArgumentNullException(nameof(instrument));

		return new SecurityId
		{
			SecurityCode = instrument.Replace('_', '/'),
			BoardCode = BoardCodes.Ond,
		};
	}

	private static readonly Dictionary<TimeSpan, string> _timeFrames = new()
	{
		{ TimeSpan.FromSeconds(5), "S5" },
		{ TimeSpan.FromSeconds(10), "S10" },
		{ TimeSpan.FromSeconds(15), "S15" },
		{ TimeSpan.FromSeconds(30), "S30" },
		{ TimeSpan.FromMinutes(1), "M1" },
		{ TimeSpan.FromMinutes(2), "M2" },
		{ TimeSpan.FromMinutes(3), "M3" },
		{ TimeSpan.FromMinutes(5), "M5" },
		{ TimeSpan.FromMinutes(10), "M10" },
		{ TimeSpan.FromMinutes(15), "M15" },
		{ TimeSpan.FromMinutes(30), "M30" },
		{ TimeSpan.FromHours(1), "H1" },
		{ TimeSpan.FromHours(2), "H2" },
		{ TimeSpan.FromHours(3), "H3" },
		{ TimeSpan.FromHours(4), "H4" },
		{ TimeSpan.FromHours(6), "H6" },
		{ TimeSpan.FromHours(8), "H8" },
		{ TimeSpan.FromHours(12), "H12" },
		{ TimeSpan.FromDays(1), "D" },
		{ TimeSpan.FromDays(7), "W" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "M" },
	};

	public static string ToOanda(this TimeSpan timeFrame)
	{
		var name = _timeFrames.TryGetValue(timeFrame);

		if (name == null)
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

		return name;
	}

	public static string ToUnixStr(this DateTime time)
	{
		if (time == default)
			return null;

		return $"{time.ToUnix()}";
	}

	public static DateTime? FromUnixStr(this string time)
	{
		if (time.IsEmpty())
			return null;

		return time.To<double>().FromUnix();
	}

	public static RestRequest AddParameterIfNotNull<T>(this RestRequest request, string paramName, T? value)
		where T : struct
	{
		return value == null ? request : request.AddParameter(paramName, value.Value);
	}

	public static SecurityTypes? ToSecurityType(this string type)
	{
		return (type?.ToUpperInvariant()) switch
		{
			"CURRENCY"	=> (SecurityTypes?)SecurityTypes.Currency,
			"CFD"		=> (SecurityTypes?)SecurityTypes.Cfd,
			"METAL"		=> (SecurityTypes?)SecurityTypes.Commodity,
			_			=> null,
		};
	}

	public static QuoteChange ToQuoteChange(this Quote quote)
	{
		return new QuoteChange((decimal)quote.Price, (decimal)quote.Liquidity);
	}

	public static string ToOandaTif(this TimeInForce? tif, DateTime? tillDate, out string gtdTime)
	{
		gtdTime = null;

		switch (tif)
		{
			case null:
			case TimeInForce.PutInQueue:
			{
				if (tillDate == null)
					return "GTC";
				else
				{
					gtdTime = tillDate.Value.ToUnixStr();
					return "GTD";
				}
			}

			case TimeInForce.MatchOrCancel:
				return "FOK";

			case TimeInForce.CancelBalance:
				return "IOC";

			default:
				throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
		}
	}

	public static TimeInForce? ToTimeInForce(this string value)
	{
		return (value?.ToUpperInvariant()) switch
		{
			"GTC"	=> (TimeInForce?)TimeInForce.PutInQueue,
			"FOK"	=> (TimeInForce?)TimeInForce.MatchOrCancel,
			"IOC"	=> (TimeInForce?)TimeInForce.CancelBalance,

			_		=> null,
		};
	}

	private static readonly Dictionary<int, string> _formats = new();

	static Extensions()
	{
		for (var i = 0; i < 10; i++)
		{
			_formats.Add(i, "0." + Enumerable.Repeat("0", i).Join(string.Empty));
		}
	}

	public static string GetPriceStr(this OrderRegisterMessage message)
	{
		return message.OrderType == OrderTypes.Market
			? null
			: message.Price.ToString(_formats[message.Decimals ?? 5]);
	}

	public static decimal? ToPrice(this string value)
	{
		if (value.IsEmpty())
			return null;

		return (decimal)value.To<double>();
	}

	public static Sides ToSide(this double units)
	{
		if (units == 0)
			throw new ArgumentOutOfRangeException(nameof(units));

		return units > 0 ? Sides.Buy : Sides.Sell;
	}

	public static OrderStates? ToOrderState(this string state)
	{
		return (state?.ToUpperInvariant()) switch
		{
			"PENDING" or "TRIGGERED"	=> (OrderStates?)OrderStates.Active,
			"FILLED" or "CANCELLED"		=> (OrderStates?)OrderStates.Done,

			_		=> null,
		};
	}

	public static OrderPositionEffects? ToPositionEffect(this string positionFill)
	{
		if (positionFill.IsEmpty())
			return null;

		return positionFill switch
		{
			"DEFAULT"		=> (OrderPositionEffects?)OrderPositionEffects.Default,
			"OPEN_ONLY"		=> (OrderPositionEffects?)OrderPositionEffects.OpenOnly,
			"REDUCE_ONLY"	=> (OrderPositionEffects?)OrderPositionEffects.CloseOnly,

			_ => null,
		};
	}

	public static string ToNative(this OrderPositionEffects? effect)
	{
		return effect switch
		{
			null or OrderPositionEffects.Default	=> "DEFAULT",
			OrderPositionEffects.OpenOnly			=> "OPEN_ONLY",
			OrderPositionEffects.CloseOnly			=> "REDUCE_ONLY",

			_ => throw new ArgumentOutOfRangeException(nameof(effect), effect, LocalizedStrings.InvalidValue),
		};
	}

	public static string CreateOrderSpecifier(this long? orderId, long transactionId)
	{
		return orderId.To<string>() ?? $"@{transactionId}";
	}

	private const string _marketStop = "MARKET_IF_TOUCHED";
	private const string _marketOrder = "MARKET";
	private const string _limitOrder = "LIMIT";

	public static Task SendOrderCommand(this OandaRestClient client, OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		if (client == null)
			throw new ArgumentNullException(nameof(client));

		if (message == null)
			throw new ArgumentNullException(nameof(message));

		switch (message.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
			case OrderTypes.Conditional:
				break;
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(message.OrderType, message.TransactionId));
		}

		var condition = (OandaOrderCondition)message.Condition;

		string type;

		if (condition == null)
			type = message.OrderType == OrderTypes.Market ? _marketOrder : _limitOrder;
		else
			type = condition.IsMarket == true ? _marketStop : "STOP";

		var tif = message.TimeInForce;

		if (message.OrderType == OrderTypes.Market)
		{
			if (tif == null || tif == TimeInForce.PutInQueue)
				tif = TimeInForce.MatchOrCancel;
		}

		var tifStr = tif.ToOandaTif(message.TillDate, out var gtdTime);
		var units = message.Volume * (message.Side == Sides.Buy ? 1 : -1);
		var instrument = message.SecurityId.ToOanda();
		var priceStr = message.GetPriceStr();
		var positionFill = message.PositionEffect.ToNative();

		if (message is not OrderReplaceMessage replaceMsg)
		{
			return client.CreateOrderAsync(message.PortfolioName,
				instrument,
				units,
				tifStr,
				gtdTime,
				type,
				positionFill,
				message.TillDate?.ToUnixStr(),
				priceStr,
				message.TransactionId,
				message.Comment,
				condition?.LowerBound,
				condition?.UpperBound,
				condition?.StopLossOffset,
				condition?.TakeProfitOffset,
				condition?.TrailingStopLossOffset,
				cancellationToken);
		}
		else
		{
			return client.ReplaceOrderAsync(message.TransactionId, message.PortfolioName,
				replaceMsg.OldOrderId.CreateOrderSpecifier(replaceMsg.OriginalTransactionId),
				instrument,
				units,
				tifStr,
				gtdTime,
				type,
				positionFill,
				message.TillDate?.ToUnixStr(),
				priceStr,
				message.TransactionId,
				message.Comment,
				condition?.LowerBound,
				condition?.UpperBound,
				condition?.StopLossOffset,
				condition?.TakeProfitOffset,
				condition?.TrailingStopLossOffset,
				cancellationToken);
		}
	}
}