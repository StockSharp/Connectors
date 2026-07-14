namespace StockSharp.Mexc.Native.Futures;

using System.Globalization;

using Newtonsoft.Json.Linq;

using StockSharp.Mexc.Native.Futures.Model;

static class WsHelpers
{
	public static string ResolveSymbol(JObject root, JObject data)
	{
		var symbol = (string)data["symbol"] ?? (string)root["symbol"];
		return symbol.IsEmpty() ? null : symbol.ToFuturesWsSymbol();
	}

	public static DateTime ToDateTime(JToken token)
	{
		var value = token?.To<long?>() ?? 0;
		if (value <= 0)
			return default;

		return value > 9_999_999_999 ? value.FromUnix(false) : value.FromUnix(true);
	}

	public static double? ToDouble(JToken token)
	{
		if (token is null)
			return null;

		var str = token.To<string>();
		if (str.IsEmpty())
			return null;

		if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
			return value;

		return null;
	}

	public static OrderBookEntry[] ToBookEntries(JToken token)
	{
		if (token is not JArray arr || arr.Count == 0)
			return [];

		return [.. arr
			.OfType<JArray>()
			.Select(level => new OrderBookEntry
			{
				Price = ToDouble(level.ElementAtOrDefault(0)),
				Quantity = ToDouble(level.ElementAtOrDefault(1)),
			})];
	}

	public static string ToWsKlineInterval(string interval)
	{
		return interval switch
		{
			"1m" => "Min1",
			"5m" => "Min5",
			"15m" => "Min15",
			"30m" => "Min30",
			"1h" => "Min60",
			"4h" => "Hour4",
			"1d" => "Day1",
			"1w" => "Week1",
			"1M" => "Month1",
			_ => interval,
		};
	}

	public static string FromWsKlineInterval(string interval)
	{
		return interval switch
		{
			"Min1" => "1m",
			"Min5" => "5m",
			"Min15" => "15m",
			"Min30" => "30m",
			"Min60" => "1h",
			"Hour4" => "4h",
			"Day1" => "1d",
			"Week1" => "1w",
			"Month1" => "1M",
			_ => interval,
		};
	}

	public static TimeSpan ToTimeFrame(string interval)
	{
		return interval switch
		{
			"1m" => TimeSpan.FromMinutes(1),
			"5m" => TimeSpan.FromMinutes(5),
			"15m" => TimeSpan.FromMinutes(15),
			"30m" => TimeSpan.FromMinutes(30),
			"1h" => TimeSpan.FromHours(1),
			"4h" => TimeSpan.FromHours(4),
			"1d" => TimeSpan.FromDays(1),
			"1w" => TimeSpan.FromDays(7),
			"1M" => TimeSpan.FromTicks(TimeHelper.TicksPerMonth),
			_ => TimeSpan.FromMinutes(1),
		};
	}

	public static Order ToOrder(JObject data)
	{
		var side = data["side"]?.To<int?>() ?? 0;
		var dealVol = ToDouble(data["dealVol"]) ?? 0;
		var remainVol = ToDouble(data["remainVol"]) ?? 0;

		return new Order
		{
			OrderId = data["orderId"]?.To<long?>() ?? 0,
			ClientOrderId = (string)data["externalOid"],
			Symbol = ((string)data["symbol"]).ToFuturesWsSymbol(),
			Price = ToDouble(data["price"]),
			OrigQty = ToDouble(data["vol"]),
			ExecutedQty = dealVol,
			ReduceOnly = side is 2 or 4,
			Side = side is 1 or 2 ? "BUY" : "SELL",
			Status = ToOrderStatus(data["state"]?.To<int?>(), dealVol, remainVol),
			Type = ToOrderType(data["orderType"]?.To<int?>()),
			Time = ToDateTime(data["createTime"]),
			UpdateTime = ToDateTime(data["updateTime"]),
		};
	}

	public static UserTrade ToUserTrade(JObject data, ref long tradeIdSeed)
	{
		var dealVol = ToDouble(data["dealVol"]) ?? 0;
		if (dealVol <= 0)
			return null;

		var side = data["side"]?.To<int?>() ?? 0;
		var makerFee = ToDouble(data["makerFee"]);
		var takerFee = ToDouble(data["takerFee"]);

		return new UserTrade
		{
			Id = Interlocked.Increment(ref tradeIdSeed),
			OrderId = data["orderId"]?.To<long?>() ?? 0,
			Price = ToDouble(data["dealAvgPrice"]) ?? ToDouble(data["price"]),
			Qty = dealVol,
			Commission = (takerFee ?? 0) > 0 ? takerFee : makerFee,
			CommissionAsset = (string)data["feeCurrency"],
			Side = side is 1 or 2 ? "BUY" : "SELL",
			PositionSide = side is 1 or 4 ? "LONG" : "SHORT",
			Symbol = ((string)data["symbol"]).ToFuturesWsSymbol(),
			Maker = (makerFee ?? 0) > 0,
			Time = ToDateTime(data["updateTime"]),
		};
	}

	private static string ToOrderStatus(int? state, double dealVol, double remainVol)
	{
		return state switch
		{
			1 => "NEW",
			2 => dealVol > 0 && remainVol > 0 ? "PARTIALLY_FILLED" : "NEW",
			3 => "FILLED",
			4 => "CANCELED",
			5 => "REJECTED",
			_ => "NEW",
		};
	}

	private static string ToOrderType(int? orderType)
	{
		return orderType switch
		{
			2 => "MARKET",
			_ => "LIMIT",
		};
	}
}
