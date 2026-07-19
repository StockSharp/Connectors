namespace StockSharp.CoinDCX.Native.Model;

sealed class CoinDCXMarket
{
	[JsonProperty("coindcx_name")]
	public string Name { get; set; }

	[JsonProperty("base_currency_short_name")]
	public string QuoteAsset { get; set; }

	[JsonProperty("target_currency_short_name")]
	public string BaseAsset { get; set; }

	[JsonProperty("base_currency_precision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("target_currency_precision")]
	public int BasePrecision { get; set; }

	[JsonProperty("min_quantity")]
	public decimal MinimumQuantity { get; set; }

	[JsonProperty("max_quantity")]
	public decimal MaximumQuantity { get; set; }

	[JsonProperty("max_quantity_market")]
	public decimal MaximumMarketQuantity { get; set; }

	[JsonProperty("min_price")]
	public decimal MinimumPrice { get; set; }

	[JsonProperty("max_price")]
	public decimal MaximumPrice { get; set; }

	[JsonProperty("min_notional")]
	public decimal MinimumNotional { get; set; }

	[JsonProperty("step")]
	public decimal QuantityStep { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ecode")]
	public string ExchangeCode { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("order_types")]
	public CoinDCXOrderTypes[] OrderTypes { get; set; }
}

sealed class CoinDCXTicker
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("change_24_hour")]
	public decimal Change24Hours { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("last_price")]
	public decimal LastPrice { get; set; }

	[JsonProperty("bid")]
	public decimal Bid { get; set; }

	[JsonProperty("ask")]
	public decimal Ask { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class CoinDCXOrderBook
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("bids")]
	[JsonConverter(typeof(CoinDCXBookLevelsConverter))]
	public CoinDCXBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	[JsonConverter(typeof(CoinDCXBookLevelsConverter))]
	public CoinDCXBookLevel[] Asks { get; set; }
}

sealed class CoinDCXBookLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class CoinDCXBookLevelsConverter : JsonConverter<CoinDCXBookLevel[]>
{
	public override bool CanWrite => false;

	public override CoinDCXBookLevel[] ReadJson(JsonReader reader, Type objectType,
		CoinDCXBookLevel[] existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return [];
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"CoinDCX order-book levels must be a JSON object.");

		var levels = new List<CoinDCXBookLevel>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"CoinDCX order-book level has an invalid price key.");
			var text = reader.Value as string;
			if (!decimal.TryParse(text, NumberStyles.Float,
				CultureInfo.InvariantCulture, out var price))
				throw new JsonSerializationException(
					$"CoinDCX order-book price '{text}' is invalid.");
			if (!reader.Read())
				throw new JsonSerializationException(
					"CoinDCX order-book level is missing its volume.");
			var volume = serializer.Deserialize<decimal>(reader);
			levels.Add(new() { Price = price, Volume = volume });
		}
		return [.. levels];
	}

	public override void WriteJson(JsonWriter writer, CoinDCXBookLevel[] value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class CoinDCXPublicTrade
{
	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("T")]
	public long Timestamp { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }
}

sealed class CoinDCXCandle
{
	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("time")]
	public long Timestamp { get; set; }
}

sealed class CoinDCXWebSocketTrade
{
	[JsonProperty("T")]
	public long Timestamp { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("m")]
	public int BuyerMaker { get; set; }

	[JsonProperty("s")]
	public string Pair { get; set; }

	[JsonProperty("pr")]
	public string Product { get; set; }
}

sealed class CoinDCXWebSocketDepth
{
	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("vs")]
	public long Version { get; set; }

	[JsonProperty("bids")]
	[JsonConverter(typeof(CoinDCXBookLevelsConverter))]
	public CoinDCXBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	[JsonConverter(typeof(CoinDCXBookLevelsConverter))]
	public CoinDCXBookLevel[] Asks { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("pts")]
	public long PreviousTimestamp { get; set; }

	[JsonProperty("E")]
	public long EventTimestamp { get; set; }

	[JsonProperty("s")]
	public string Pair { get; set; }

	[JsonProperty("pr")]
	public string Product { get; set; }
}

sealed class CoinDCXWebSocketCandle
{
	[JsonProperty("t")]
	public long OpenTimestamp { get; set; }

	[JsonProperty("T")]
	public long CloseTimestamp { get; set; }

	[JsonProperty("s")]
	public string Pair { get; set; }

	[JsonProperty("i")]
	public string Interval { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("n")]
	public long TradeCount { get; set; }

	[JsonProperty("x")]
	public bool IsFinished { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }
}
