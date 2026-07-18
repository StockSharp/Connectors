namespace StockSharp.Toobit.Native;

interface IToobitRequest
{
	void Write(ToobitQueryBuilder query);
}

sealed class ToobitQueryBuilder
{
	private readonly StringBuilder _builder = new();

	public void Add(string name, string value)
	{
		if (name.IsEmpty() || value is null)
			return;

		if (_builder.Length > 0)
			_builder.Append('&');

		_builder
			.Append(Uri.EscapeDataString(name))
			.Append('=')
			.Append(Uri.EscapeDataString(value));
	}

	public void Add(string name, long? value)
	{
		if (value is long number)
			Add(name, number.ToString(CultureInfo.InvariantCulture));
	}

	public void Add(string name, int? value)
	{
		if (value is int number)
			Add(name, number.ToString(CultureInfo.InvariantCulture));
	}

	public void Add(string name, decimal? value)
	{
		if (value is decimal number)
			Add(name, number.ToString(CultureInfo.InvariantCulture));
	}

	public override string ToString() => _builder.ToString();
}

sealed class ToobitEmptyRequest : IToobitRequest
{
	public static ToobitEmptyRequest Instance { get; } = new();
	private ToobitEmptyRequest() { }
	public void Write(ToobitQueryBuilder query) { }
}

sealed class ToobitSymbolRequest : IToobitRequest
{
	public string Symbol { get; init; }
	public void Write(ToobitQueryBuilder query) => query.Add("symbol", Symbol);
}

sealed class ToobitDepthRequest : IToobitRequest
{
	public string Symbol { get; init; }
	public int? Limit { get; init; }

	public void Write(ToobitQueryBuilder query)
	{
		query.Add("symbol", Symbol);
		query.Add("limit", Limit);
	}
}

sealed class ToobitKlinesRequest : IToobitRequest
{
	public string Symbol { get; init; }
	public string Interval { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int? Limit { get; init; }

	public void Write(ToobitQueryBuilder query)
	{
		query.Add("symbol", Symbol);
		query.Add("interval", Interval);
		query.Add("startTime", StartTime);
		query.Add("endTime", EndTime);
		query.Add("limit", Limit);
	}
}

sealed class ToobitSpotOrderRequest : IToobitRequest
{
	public string Symbol { get; init; }
	public ToobitOrderSides Side { get; init; }
	public ToobitOrderTypes Type { get; init; }
	public ToobitTimeInForce? TimeInForce { get; init; }
	public decimal Quantity { get; init; }
	public decimal? Price { get; init; }
	public string ClientOrderId { get; init; }

	public void Write(ToobitQueryBuilder query)
	{
		query.Add("symbol", Symbol);
		query.Add("side", Side.ToWire());
		query.Add("type", Type.ToWire());
		if (TimeInForce is { } timeInForce)
			query.Add("timeInForce", timeInForce.ToWire());
		query.Add("quantity", Quantity);
		query.Add("price", Price);
		query.Add("newClientOrderId", ClientOrderId);
	}
}

sealed class ToobitFuturesOrderRequest : IToobitRequest
{
	public string Symbol { get; init; }
	public ToobitOrderSides Side { get; init; }
	public ToobitOrderTypes Type { get; init; }
	public ToobitTimeInForce? TimeInForce { get; init; }
	public ToobitPriceTypes PriceType { get; init; }
	public decimal Quantity { get; init; }
	public decimal? Price { get; init; }
	public decimal? StopPrice { get; init; }
	public string ClientOrderId { get; init; }

	public void Write(ToobitQueryBuilder query)
	{
		query.Add("symbol", Symbol);
		query.Add("side", Side.ToWire());
		query.Add("type", Type.ToWire());
		if (TimeInForce is { } timeInForce)
			query.Add("timeInForce", timeInForce.ToWire());
		query.Add("priceType", PriceType.ToWire());
		query.Add("quantity", Quantity);
		query.Add("price", Price);
		query.Add("stopPrice", StopPrice);
		query.Add("newClientOrderId", ClientOrderId);
	}
}

sealed class ToobitOrderRequest : IToobitRequest
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }
	public ToobitOrderTypes? Type { get; init; }

	public void Write(ToobitQueryBuilder query)
	{
		query.Add("symbol", Symbol);
		query.Add("orderId", OrderId);
		query.Add("origClientOrderId", ClientOrderId);
		if (Type is { } type)
			query.Add("type", type.ToWire());
	}
}

sealed class ToobitCancelSpotOrderRequest : IToobitRequest
{
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }

	public void Write(ToobitQueryBuilder query)
	{
		query.Add("orderId", OrderId);
		query.Add("clientOrderId", ClientOrderId);
	}
}

sealed class ToobitOpenOrdersRequest : IToobitRequest
{
	public string Symbol { get; init; }
	public int? Limit { get; init; }
	public ToobitOrderTypes? Type { get; init; }

	public void Write(ToobitQueryBuilder query)
	{
		query.Add("symbol", Symbol);
		query.Add("limit", Limit);
		if (Type is { } type)
			query.Add("type", type.ToWire());
	}
}

sealed class ToobitOrderHistoryRequest : IToobitRequest
{
	public string Symbol { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int? Limit { get; init; }

	public void Write(ToobitQueryBuilder query)
	{
		query.Add("symbol", Symbol);
		query.Add("startTime", StartTime);
		query.Add("endTime", EndTime);
		query.Add("limit", Limit);
	}
}

sealed class ToobitUserTradesRequest : IToobitRequest
{
	public string Symbol { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public string FromId { get; init; }
	public int? Limit { get; init; }

	public void Write(ToobitQueryBuilder query)
	{
		query.Add("symbol", Symbol);
		query.Add("startTime", StartTime);
		query.Add("endTime", EndTime);
		query.Add("fromId", FromId);
		query.Add("limit", Limit);
	}
}

sealed class ToobitListenKeyRequest : IToobitRequest
{
	public string ListenKey { get; init; }
	public void Write(ToobitQueryBuilder query) => query.Add("listenKey", ListenKey);
}

static class ToobitWireExtensions
{
	public static string ToWire(this ToobitOrderSides side)
		=> side switch
		{
			ToobitOrderSides.Buy => "BUY",
			ToobitOrderSides.Sell => "SELL",
			ToobitOrderSides.BuyOpen => "BUY_OPEN",
			ToobitOrderSides.SellOpen => "SELL_OPEN",
			ToobitOrderSides.BuyClose => "BUY_CLOSE",
			ToobitOrderSides.SellClose => "SELL_CLOSE",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this ToobitOrderTypes type)
		=> type switch
		{
			ToobitOrderTypes.Limit => "LIMIT",
			ToobitOrderTypes.Market => "MARKET",
			ToobitOrderTypes.LimitMaker => "LIMIT_MAKER",
			ToobitOrderTypes.Stop => "STOP",
			ToobitOrderTypes.StopLimit => "STOP_LIMIT",
			ToobitOrderTypes.StopProfitLoss => "STOP_PROFIT_LOSS",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this ToobitPriceTypes type)
		=> type switch
		{
			ToobitPriceTypes.Input => "INPUT",
			ToobitPriceTypes.Opponent => "OPPONENT",
			ToobitPriceTypes.Queue => "QUEUE",
			ToobitPriceTypes.Over => "OVER",
			ToobitPriceTypes.Market => "MARKET",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static string ToWire(this ToobitTimeInForce timeInForce)
		=> timeInForce switch
		{
			ToobitTimeInForce.Gtc => "GTC",
			ToobitTimeInForce.Ioc => "IOC",
			ToobitTimeInForce.Fok => "FOK",
			ToobitTimeInForce.PostOnly => "POST_ONLY",
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue),
		};
}
