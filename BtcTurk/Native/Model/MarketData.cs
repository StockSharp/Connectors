namespace StockSharp.BtcTurk.Native.Model;

sealed class BtcTurkExchangeInfo
{
	[JsonProperty("timeZone")]
	public string TimeZone { get; set; }

	[JsonProperty("serverTime")]
	public long ServerTime { get; set; }

	[JsonProperty("symbols")]
	public BtcTurkMarket[] Symbols { get; set; }

	[JsonProperty("currencies")]
	public BtcTurkCurrency[] Currencies { get; set; }
}

sealed class BtcTurkMarket
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("name")]
	public string NativeSymbol { get; set; }

	[JsonProperty("nameNormalized")]
	public string NormalizedSymbol { get; set; }

	[JsonProperty("status")]
	public BtcTurkMarketStatuses Status { get; set; }

	[JsonProperty("numerator")]
	public string Numerator { get; set; }

	[JsonProperty("denominator")]
	public string Denominator { get; set; }

	[JsonProperty("numeratorScale")]
	public int NumeratorScale { get; set; }

	[JsonProperty("denominatorScale")]
	public int DenominatorScale { get; set; }

	[JsonProperty("hasFraction")]
	public bool IsFractional { get; set; }

	[JsonProperty("filters")]
	public BtcTurkMarketFilter[] Filters { get; set; }

	[JsonProperty("orderMethods")]
	public string[] OrderMethods { get; set; }

	[JsonProperty("maximumOrderAmount")]
	public decimal? MaximumOrderAmount { get; set; }

	[JsonIgnore]
	public string SecurityCode
		=> BtcTurkExtensions.CreateSecurityCode(Numerator, Denominator);

	[JsonIgnore]
	public BtcTurkMarketFilter PriceFilter
		=> Filters?.FirstOrDefault(static filter =>
			filter.FilterType.EqualsIgnoreCase("PRICE_FILTER"));
}

sealed class BtcTurkMarketFilter
{
	[JsonProperty("filterType")]
	public string FilterType { get; set; }

	[JsonProperty("minPrice")]
	public decimal? MinimumPrice { get; set; }

	[JsonProperty("maxPrice")]
	public decimal? MaximumPrice { get; set; }

	[JsonProperty("tickSize")]
	public decimal? TickSize { get; set; }

	[JsonProperty("minExchangeValue")]
	public decimal? MinimumExchangeValue { get; set; }

	[JsonProperty("minAmount")]
	public decimal? MinimumAmount { get; set; }

	[JsonProperty("maxAmount")]
	public decimal? MaximumAmount { get; set; }
}

sealed class BtcTurkCurrency
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("precision")]
	public int Precision { get; set; }
}

sealed class BtcTurkTicker
{
	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("pairNormalized")]
	public string PairNormalized { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("average")]
	public decimal? Average { get; set; }

	[JsonProperty("daily")]
	public decimal? Daily { get; set; }

	[JsonProperty("dailyPercent")]
	public decimal? DailyPercent { get; set; }

	[JsonProperty("denominatorSymbol")]
	public string Denominator { get; set; }

	[JsonProperty("numeratorSymbol")]
	public string Numerator { get; set; }
}

[JsonConverter(typeof(BtcTurkPriceLevelConverter))]
sealed class BtcTurkPriceLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
}

sealed class BtcTurkOrderBook
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("bids")]
	public BtcTurkPriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BtcTurkPriceLevel[] Asks { get; set; }
}

sealed class BtcTurkPublicTrade
{
	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("pairNormalized")]
	public string PairNormalized { get; set; }

	[JsonProperty("numerator")]
	public string Numerator { get; set; }

	[JsonProperty("denominator")]
	public string Denominator { get; set; }

	[JsonProperty("date")]
	public long Timestamp { get; set; }

	[JsonProperty("tid")]
	public string Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("side")]
	public BtcTurkSides? Side { get; set; }
}

sealed class BtcTurkKline
{
	[JsonProperty("s")]
	public string Status { get; set; }

	[JsonProperty("t")]
	public long[] Timestamps { get; set; }

	[JsonProperty("h")]
	public decimal[] Highs { get; set; }

	[JsonProperty("o")]
	public decimal[] Opens { get; set; }

	[JsonProperty("l")]
	public decimal[] Lows { get; set; }

	[JsonProperty("c")]
	public decimal[] Closes { get; set; }

	[JsonProperty("v")]
	public decimal[] Volumes { get; set; }
}

sealed class BtcTurkMarketQuery : IBtcTurkQuery
{
	public string PairSymbol { get; init; }

	public BtcTurkParameter[] GetParameters()
		=> PairSymbol.IsEmpty()
			? []
			: [new("pairSymbol", PairSymbol)];
}

sealed class BtcTurkPublicTradesQuery : IBtcTurkQuery
{
	public string PairSymbol { get; init; }
	public int Count { get; init; }

	public BtcTurkParameter[] GetParameters()
		=>
		[
			new("pairSymbol", PairSymbol.ThrowIfEmpty(nameof(PairSymbol))),
			new("last", Count.Max(1).Min(50)
				.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class BtcTurkKlineQuery : IBtcTurkQuery
{
	public string Symbol { get; init; }
	public string Resolution { get; init; }
	public long From { get; init; }
	public long To { get; init; }

	public BtcTurkParameter[] GetParameters()
		=>
		[
			new("symbol", Symbol.ThrowIfEmpty(nameof(Symbol))),
			new("resolution", Resolution.ThrowIfEmpty(nameof(Resolution))),
			new("from", From.ToString(CultureInfo.InvariantCulture)),
			new("to", To.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class BtcTurkPriceLevelConverter : JsonConverter<BtcTurkPriceLevel>
{
	public override BtcTurkPriceLevel ReadJson(JsonReader reader,
		Type objectType, BtcTurkPriceLevel existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				"BtcTurk price level must be an array.");
		var price = ReadDecimal(reader, "price");
		var volume = ReadDecimal(reader, "volume");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"BtcTurk price level has unexpected fields.");
		return new() { Price = price, Volume = volume };
	}

	public override void WriteJson(JsonWriter writer, BtcTurkPriceLevel value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Price);
		writer.WriteValue(value.Volume);
		writer.WriteEndArray();
	}

	private static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException(
				$"BtcTurk price level has no {field}.");
		return BtcTurkExtensions.ParseDecimal(reader.Value?.ToString());
	}
}
