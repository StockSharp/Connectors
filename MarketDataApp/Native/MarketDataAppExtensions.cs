namespace StockSharp.MarketDataApp.Native;

static class MarketDataAppExtensions
{
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromMinutes(45),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

	public static string ToResolution(this TimeSpan value,
		MarketDataAppAssetKinds kind)
	{
		if (!_timeFrames.Contains(value))
			throw new NotSupportedException(
				$"MarketData.app candle interval '{value}' is unsupported.");
		if (kind == MarketDataAppAssetKinds.Fund &&
			value < TimeSpan.FromDays(1))
			throw new NotSupportedException(
				"MarketData.app mutual funds provide daily and " +
					"longer candles only.");
		if (value < TimeSpan.FromHours(1))
			return ((int)value.TotalMinutes).ToString(
				CultureInfo.InvariantCulture);
		if (value < TimeSpan.FromDays(1))
			return $"{(int)value.TotalHours}H";
		if (value == TimeSpan.FromDays(1))
			return "D";
		if (value == TimeSpan.FromDays(7))
			return "W";
		return "M";
	}

	public static bool IsOptionSymbol(this string value)
	{
		if (value.IsEmpty())
			return false;
		value = value.Trim();
		if (value.Length < 16)
			return false;
		var optionTypeIndex = value.Length - 9;
		if (value[optionTypeIndex] is not ('C' or 'P' or 'c' or 'p'))
			return false;
		return value[(optionTypeIndex - 6)..optionTypeIndex]
				.All(char.IsDigit) &&
			value[(optionTypeIndex + 1)..].All(char.IsDigit);
	}

	public static MarketDataAppQuote[] ToQuotes(this JObject value,
		string fallbackSymbol)
	{
		if (!value.IsOk())
			return [];
		var symbols = value["optionSymbol"] as JArray ??
			value["symbol"] as JArray;
		var count = symbols?.Count ?? GetLargestArray(value);
		if (count == 0 && !fallbackSymbol.IsEmpty())
			count = 1;
		var result = new List<MarketDataAppQuote>(count);
		for (var index = 0; index < count; index++)
		{
			var symbol = symbols.ValueAt<string>(index)
				.IsEmpty(fallbackSymbol);
			if (symbol.IsEmpty())
				continue;
			result.Add(new()
			{
				Symbol = symbol,
				Underlying = value.ArrayValue<string>(
					"underlying", index),
				Expiry = value.ArrayUnixTime("expiration", index),
				OptionType = value.ArrayValue<string>(
					"side", index)?.ToLowerInvariant() switch
				{
					"call" => OptionTypes.Call,
					"put" => OptionTypes.Put,
					_ => null,
				},
				Strike = value.ArrayValue<decimal?>("strike", index),
				ServerTime = value.ArrayUnixTime("updated", index) ??
					DateTime.UtcNow,
				Bid = value.ArrayValue<decimal?>("bid", index),
				BidSize = value.ArrayValue<decimal?>("bidSize", index),
				Ask = value.ArrayValue<decimal?>("ask", index),
				AskSize = value.ArrayValue<decimal?>("askSize", index),
				Last = value.ArrayValue<decimal?>("last", index),
				Change = value.ArrayValue<decimal?>("change", index),
				Volume = value.ArrayValue<decimal?>("volume", index),
				OpenInterest = value.ArrayValue<decimal?>(
					"openInterest", index),
				UnderlyingPrice = value.ArrayValue<decimal?>(
					"underlyingPrice", index),
				ImpliedVolatility = value.ArrayValue<decimal?>(
					"iv", index),
				Delta = value.ArrayValue<decimal?>("delta", index),
				Gamma = value.ArrayValue<decimal?>("gamma", index),
				Theta = value.ArrayValue<decimal?>("theta", index),
				Vega = value.ArrayValue<decimal?>("vega", index),
			});
		}
		return [.. result];
	}

	public static MarketDataAppCandle[] ToCandles(this JObject value)
	{
		if (!value.IsOk() || value["t"] is not JArray times)
			return [];
		var result = new List<MarketDataAppCandle>(times.Count);
		for (var index = 0; index < times.Count; index++)
		{
			var time = times.ValueAt<long?>(index);
			var open = value.ArrayValue<decimal?>("o", index);
			var high = value.ArrayValue<decimal?>("h", index);
			var low = value.ArrayValue<decimal?>("l", index);
			var close = value.ArrayValue<decimal?>("c", index);
			if (time is null || open is null || high is null ||
				low is null || close is null)
				continue;
			result.Add(new()
			{
				OpenTime = DateTimeOffset
					.FromUnixTimeSeconds(time.Value).UtcDateTime,
				Open = open.Value,
				High = high.Value,
				Low = low.Value,
				Close = close.Value,
				Volume = value.ArrayValue<decimal?>("v", index),
			});
		}
		return [.. result];
	}

	public static MarketDataAppInstrument ToInstrument(
		this MarketDataAppQuote quote,
		MarketDataAppAssetKinds kind,
		SecurityTypes securityType)
		=> new()
		{
			Symbol = quote.Symbol,
			Kind = kind,
			SecurityType = securityType,
			Underlying = quote.Underlying,
			Expiry = quote.Expiry,
			OptionType = quote.OptionType,
			Strike = quote.Strike,
		};

	public static SecurityMessage ToSecurityMessage(
		this MarketDataAppInstrument instrument,
		long transactionId)
		=> new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = instrument.ToSecurityId(),
			Name = instrument.Symbol,
			ShortName = instrument.Symbol,
			SecurityType = instrument.SecurityType,
			Currency = CurrencyTypes.USD,
			UnderlyingSecurityId =
				instrument.Underlying.IsEmpty()
					? default
					: new()
					{
						SecurityCode = instrument.Underlying,
						BoardCode = BoardCodes.MarketDataApp,
						Native = $"stock:{instrument.Underlying}",
					},
			ExpiryDate = instrument.Expiry,
			OptionType = instrument.OptionType,
			Strike = instrument.Strike,
		};

	public static SecurityId ToSecurityId(
		this MarketDataAppInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Symbol,
			BoardCode = BoardCodes.MarketDataApp,
			Native = instrument.NativeId,
		};

	public static Level1ChangeMessage ToLevel1(
		this MarketDataAppQuote quote,
		MarketDataAppInstrument instrument,
		long transactionId)
		=> new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = instrument.ToSecurityId(),
			ServerTime = quote.ServerTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.Bid, true)
		.TryAdd(Level1Fields.BestBidVolume, quote.BidSize, true)
		.TryAdd(Level1Fields.BestAskPrice, quote.Ask, true)
		.TryAdd(Level1Fields.BestAskVolume, quote.AskSize, true)
		.TryAdd(Level1Fields.LastTradePrice, quote.Last, true)
		.TryAdd(Level1Fields.Change, quote.Change, true)
		.TryAdd(Level1Fields.Volume, quote.Volume, true)
		.TryAdd(Level1Fields.OpenInterest, quote.OpenInterest, true)
		.TryAdd(Level1Fields.UnderlyingPrice,
			quote.UnderlyingPrice, true)
		.TryAdd(Level1Fields.ImpliedVolatility,
			quote.ImpliedVolatility, true)
		.TryAdd(Level1Fields.Delta, quote.Delta, true)
		.TryAdd(Level1Fields.Gamma, quote.Gamma, true)
		.TryAdd(Level1Fields.Theta, quote.Theta, true)
		.TryAdd(Level1Fields.Vega, quote.Vega, true);

	public static MarketDataAppAssetKinds ToAssetKind(
		this string native)
	{
		var prefix = native?.Split(':', 2)[0];
		return prefix?.ToLowerInvariant() switch
		{
			"option" => MarketDataAppAssetKinds.Option,
			"index" => MarketDataAppAssetKinds.Index,
			"fund" => MarketDataAppAssetKinds.Fund,
			_ => MarketDataAppAssetKinds.Stock,
		};
	}

	public static string WithoutAssetPrefix(this string value)
		=> value?.IndexOf(':') is > 0 and var index
			? value[(index + 1)..]
			: value;

	private static bool IsOk(this JObject value)
		=> value?.Value<string>("s").EqualsIgnoreCase("ok") == true;

	private static int GetLargestArray(JObject value)
		=> value.Properties()
			.Select(static property =>
				(property.Value as JArray)?.Count ?? 0)
			.DefaultIfEmpty()
			.Max();

	private static T ArrayValue<T>(this JObject value,
		string name, int index)
		=> (value?[name] as JArray).ValueAt<T>(index);

	private static DateTime? ArrayUnixTime(this JObject value,
		string name, int index)
	{
		var time = value.ArrayValue<long?>(name, index);
		return time is null
			? null
			: DateTimeOffset.FromUnixTimeSeconds(time.Value)
				.UtcDateTime;
	}

	private static T ValueAt<T>(this JArray value, int index)
	{
		if (value is null || index < 0 || index >= value.Count)
			return default;
		var token = value[index];
		if (token is null ||
			token.Type is JTokenType.Null or JTokenType.Undefined)
			return default;
		return token.Value<T>();
	}
}
