namespace StockSharp.Kraken;

static class Extensions
{
	public static Sides ToSide(this string value)
	{
		return (value?.ToLowerInvariant()) switch
		{
			"b" or "buy" => Sides.Buy,
			"s" or "sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this Sides side)
	{
		return side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static decimal GetBalance(this Native.Spot.Model.OrderInfo order)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));

		return order.Volume - order.VolumeExecuted;
	}

	public static OrderTypes GetOrderType(this Native.Spot.Model.OrderInfo order, out KrakenOrderCondition condition)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));

		condition = null;

		var type = order.Description.OrderType?.ToLowerInvariant();

		switch (type)
		{
			case "limit":
				return OrderTypes.Limit;

			case "market":
				return OrderTypes.Market;

			default:
				condition = new KrakenOrderCondition();

				switch (type)
				{
					case "trailing-stop":
						condition.Type = KrakenOrderConditionTypes.StopLoss;
						condition.IsTrailing = true;
						break;
					case "trailing-stop-limit":
						condition.Type = KrakenOrderConditionTypes.StopLoss;
						condition.IsTrailing = true;
						condition.StopPrice = order.Description.Price2;
						break;
					case "stop-loss":
						condition.Type = KrakenOrderConditionTypes.StopLoss;
						break;
					case "stop-loss-limit":
						condition.Type = KrakenOrderConditionTypes.StopLoss;
						condition.StopPrice = order.Description.Price2;
						break;
					case "take-profit":
						condition.Type = KrakenOrderConditionTypes.TakeProfit;
						break;
					case "take-profit-limit":
						condition.Type = KrakenOrderConditionTypes.TakeProfit;
						condition.StopPrice = order.Description.Price2;
						break;
					case "stop-loss-profit":
						condition.Type = KrakenOrderConditionTypes.StopLossTakeProfit;
						break;
					case "stop-loss-profit-limit":
						condition.Type = KrakenOrderConditionTypes.StopLossTakeProfit;
						condition.StopPrice = order.Description.Price2;
						break;
				}

				return OrderTypes.Market;
				//throw new ArgumentOutOfRangeException(nameof(order), order.Description.OrderType, LocalizedStrings.InvalidValue);
		}
	}

	public static OrderStates ToOrderState(this string status)
	{
		return (status?.ToLowerInvariant()) switch
		{
			"pending" => OrderStates.Pending,
			"open" => OrderStates.Active,
			"closed" or "canceled" or "expired" => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this OrderTypes? type, KrakenOrderCondition condition)
	{
		switch (type)
		{
			case null:
			case OrderTypes.Limit:
				return "limit";
			case OrderTypes.Market:
				return "market";
			case OrderTypes.Conditional:
			{
				if (condition == null)
					throw new ArgumentNullException(nameof(condition));

				switch (condition.Type)
				{
					case KrakenOrderConditionTypes.StopLoss:
					{
						if (condition.IsTrailing)
						{
							return condition.StopPrice == null ? "trailing-stop" : "trailing-stop-limit";
						}
						else
						{
							return condition.StopPrice == null ? "stop-loss" : "stop-loss-limit";
						}
					}
					case KrakenOrderConditionTypes.TakeProfit:
					{
						return condition.StopPrice == null ? "take-profit" : "take-profit-limit";
					}
					case KrakenOrderConditionTypes.StopLossTakeProfit:
					{
						return condition.StopPrice == null ? "stop-loss-profit" : "stop-loss-profit-limit";
					}
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToSymbol(this SecurityId securityId)
	{
		return securityId.SecurityCode?.Remove("/").ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string symbol)
	{
		return new()
		{
			SecurityCode = symbol.ToUpperInvariant(),
			BoardCode = BoardCodes.Kraken,
		};
	}

	public static SessionStates ToSessionState(this string state)
	{
		return state switch
		{
			"online" => SessionStates.Active,
			_ => SessionStates.ForceStopped,
		};
	}
}