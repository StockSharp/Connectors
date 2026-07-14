namespace StockSharp.Binance.Native;

static class Extensions
{
	public static bool IsCommonFutures(this BinanceSections section)
		=> section == BinanceSections.Futures || section == BinanceSections.FuturesCoin;

	public static QuoteChange ToChange(this OrderBookEntry entry)
		=> new((decimal)entry.Price, (decimal)entry.Size);

	public static string ToNative(this OrderTypes? type, BinanceSections section, decimal price, BinanceOrderCondition condition, bool? postOnly, out bool isTif)
	{
		isTif = false;

		switch (type)
		{
			case null:
			case OrderTypes.Limit:
				isTif = true;
				return !section.IsCommonFutures() && postOnly == true ? "LIMIT_MAKER" : "LIMIT";
			case OrderTypes.Market:
				return "MARKET";
			case OrderTypes.Conditional:
			{
				if (condition == null)
					throw new ArgumentNullException(nameof(condition));

				var isFutures = section.IsCommonFutures();

				switch (condition.Type)
				{
					case BinanceOrderConditionTypes.StopLoss:
					{
						isTif = price != 0;

						if (isFutures)
						{
							if (condition.IsTrailing)
								return "TRAILING_STOP_MARKET";

							return price == 0 ? "STOP_MARKET" : "STOP";
						}
						else
							return price == 0 ? "STOP_LOSS" : "STOP_LOSS_LIMIT";
					}
					case BinanceOrderConditionTypes.TakeProfit:
					{
						isTif = price != 0;

						if (isFutures)
							return price == 0 ? "TAKE_MARKET_MARKET" : "TAKE_PROFIT";
						else
							return price == 0 ? "TAKE_PROFIT" : "TAKE_PROFIT_LIMIT";
					}
					default:
						throw new ArgumentOutOfRangeException(nameof(condition), condition.Type, LocalizedStrings.InvalidValue);
				}
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static OrderTypes? ToOrderType(this string type, out bool? postOnly, out BinanceOrderCondition condition)
	{
		postOnly = null;
		condition = null;

		switch (type?.ToUpperInvariant())
		{
			case null:
				return null;
			case "LIMIT_MAKER":
				postOnly = true;
				return OrderTypes.Limit;
			case "LIMIT":
				return OrderTypes.Limit;
			case "MARKET":
				return OrderTypes.Market;
			case "STOP_LOSS":
			case "STOP_MARKET":
				condition = new BinanceOrderCondition { Type = BinanceOrderConditionTypes.StopLoss };
				return OrderTypes.Conditional;
			case "STOP_LOSS_LIMIT":
			case "STOP":
				condition = new BinanceOrderCondition { Type = BinanceOrderConditionTypes.StopLoss };
				return OrderTypes.Conditional;
			case "TAKE_PROFIT":
			case "TAKE_PROFIT_MARKET":
				condition = new BinanceOrderCondition { Type = BinanceOrderConditionTypes.TakeProfit };
				return OrderTypes.Conditional;
			case "TAKE_PROFIT_LIMIT":
				condition = new BinanceOrderCondition { Type = BinanceOrderConditionTypes.TakeProfit };
				return OrderTypes.Conditional;
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToNative(this TimeInForce? tif, BinanceSections section, bool? postOnly)
	{
		return tif switch
		{
			null or TimeInForce.PutInQueue
				=> section.IsCommonFutures() && postOnly == true ? "GTX" : "GTC",
			
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel => "FOK",

			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static TimeInForce? ToTimeInForce(this string tif, out bool? postOnly)
	{
		postOnly = null;

		switch (tif?.ToUpperInvariant())
		{
			case null:
				return null;
			case "GTX":
				postOnly = true;
				return TimeInForce.PutInQueue;
			case "GTC":
			case "GTE_GTC":
				return TimeInForce.PutInQueue;
			case "IOC":
				return TimeInForce.CancelBalance;
			case "FOK":
				return TimeInForce.MatchOrCancel;
			default:
				throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToNative(this Sides side)
	{
		return side switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides ToSide(this string side)
	{
		return (side?.ToLowerInvariant()) switch
		{
			"bid" or "buy" or "long" => Sides.Buy,
			"ask" or "sell" or "short" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(3), "3d" },
		{ TimeSpan.FromDays(7), "1w" },
		{ TimeSpan.FromDays(30), "1M" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan ToTimeFrame(this string name)
		=> TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

	public static string ToBoard(this BinanceSections section)
		=> section switch
		{
			BinanceSections.FuturesCoin => BoardCodes.BinanceCoin,
			BinanceSections.Futures => BoardCodes.BinanceFut,
			_ => BoardCodes.Binance,
		};

	public static BinanceSections ToSection(this string boardCode)
		=> boardCode switch
		{
			BoardCodes.BinanceCoin => BinanceSections.FuturesCoin,
			BoardCodes.BinanceFut => BinanceSections.Futures,
			_ => BinanceSections.Spot,
		};

	public static SecurityId ToStockSharp(this string secCode, BinanceSections section)
	{
		if (secCode.IsEmpty())
			throw new ArgumentNullException(nameof(secCode));

		return new()
		{
			SecurityCode = secCode.ToUpperInvariant(),
			BoardCode = section.ToBoard(),
		};
	}

	public static (BinanceSections section, string symbol) ToNative(this SecurityId securityId)
		=> (securityId.BoardCode.ToSection(), securityId.SecurityCode);

	public static SecurityTypes ToSecurityType(this BinanceSections section)
	{
		return section switch
		{
			BinanceSections.Spot or BinanceSections.Margin
				=> SecurityTypes.CryptoCurrency,

			BinanceSections.Futures or BinanceSections.FuturesCoin
				=> SecurityTypes.Future,

			_ => throw new ArgumentOutOfRangeException(nameof(section), section, LocalizedStrings.InvalidValue),
		};
	}

	public static decimal GetBalance(this Order order)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));

		return (decimal)(order.OrigQuantity - order.ExecutedQuantity);
	}

	public static bool IsStopOrderType(this string ordType)
	{
		return (ordType?.ToUpperInvariant()) switch
		{
			"STOP_LOSS" or "STOP_MARKET" or "STOP_LOSS_LIMIT" or "STOP" or
			"TAKE_PROFIT" or "TAKE_PROFIT_MARKET" or "TAKE_PROFIT_LIMIT"
				=> true,

			_ => false,
		};
	}

	public static bool IsStopTriggered(this ExecutionReport report)
	{
		const string exp = "EXPIRED";

		if(report.ExecType?.ToUpperInvariant() != exp || report.OrderStatus?.ToUpperInvariant() != exp)
			return false;

		return report.OriginalOrderType?.IsStopOrderType() == true;
	}

	public static OrderStates ToOrderState(this string status)
	{
		return status switch
		{
			"NEW" or "PARTIALLY_FILLED" or "PENDING_CANCEL" or "NEW_INSURANCE" or "NEW_ADL"
				=> OrderStates.Active,

			"FILLED" or "CANCELED" or "EXPIRED" or "REPLACED" or "STOPPED"
				=> OrderStates.Done,
			
			"REJECTED" => OrderStates.Failed,

			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}

	public static SecurityStates? ToSecurityState(this string status)
	{
		if (status.IsEmpty())
			return null;

		switch (status)
		{
			case "PRE_TRADING":
			case "TRADING":
			case "POST_TRADING":
			case "AUCTION_MATCH":
				return SecurityStates.Trading;

			case "END_OF_DAY":
			case "HALT":
			case "BREAK":
			case "CLOSE":
				return SecurityStates.Stoped;

			default:
			{
				return null;
				//throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
			}
		}
	}

	public static string ToNative(this BinanceTriggerTypes trigger)
	{
		return trigger switch
		{
			BinanceTriggerTypes.MarkPrice => "MARK_PRICE",
			BinanceTriggerTypes.ContractPrice => "CONTRACT_PRICE",
			_ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, LocalizedStrings.InvalidValue),
		};
	}

	public static BinanceTriggerTypes? ToTrigger(this string trigger)
	{
		if (trigger.IsEmpty())
			return null;

		return trigger switch
		{
			"MARK_PRICE" => (BinanceTriggerTypes?)BinanceTriggerTypes.MarkPrice,
			"CONTRACT_PRICE" => (BinanceTriggerTypes?)BinanceTriggerTypes.ContractPrice,
			_ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, LocalizedStrings.InvalidValue),
		};
	}

	public static void CheckHostName(this string hostname, string name)
	{
		var hostWithoutPort = hostname;

		var colonIndex = hostname.LastIndexOf(':');
		if (colonIndex != -1)
		{
			hostWithoutPort = hostname[..colonIndex];

			if (int.TryParse(hostname.AsSpan(colonIndex + 1), out int port))
			{
				if (port < 1 || port > 65535)
					throw new InvalidOperationException($"Port in hostname '{name}' is invalid: '{port}'");
			}
			else
			{
				throw new InvalidOperationException($"Invalid port format in hostname '{name}': '{hostname}'");
			}
		}

		if (Uri.CheckHostName(hostWithoutPort) is not UriHostNameType.Basic and not UriHostNameType.Dns and not UriHostNameType.IPv4)
		{
			throw new InvalidOperationException($"Hostname '{name}' is invalid: '{hostWithoutPort}'");
		}
	}
}
