namespace StockSharp.Mexc.Native.Spot;

using System.Globalization;

using Newtonsoft.Json.Linq;

using StockSharp.Mexc.Native.Spot.Model;

static class WsHelpers
{
	public static string ResolveSymbol(JObject obj, string channel)
	{
		var symbol = (string)obj["symbol"];
		if (!symbol.IsEmpty())
			return symbol.ToUpperInvariant();

		var parts = channel.SplitByAt(false);
		if (parts.Length >= 2 && parts[^1].All(char.IsLetterOrDigit))
			return parts[^1].ToUpperInvariant();

		if (parts.Length >= 3 && parts[^2].All(char.IsLetterOrDigit))
			return parts[^2].ToUpperInvariant();

		return null;
	}

	public static string ToSpotWsInterval(string interval)
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

	public static string FromSpotWsInterval(string interval)
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

	public static DateTime ToDateTime(JToken token)
	{
		var value = token?.To<long?>() ?? 0;
		if (value <= 0)
			return default;

		// Spot v3 sends both milliseconds and seconds depending on payload.
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

	public static OrderBookEntry[] ToOrderBookEntries(JToken token)
	{
		if (token is not JArray arr || arr.Count == 0)
			return [];

		return [.. arr
			.OfType<JObject>()
			.Select(level => new OrderBookEntry
			{
				Price = ToDouble(level["price"]),
				Quantity = ToDouble(level["quantity"]),
			})];
	}
}
