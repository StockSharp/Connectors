namespace StockSharp.CryptoCom.Native.Model;

sealed class CryptoComInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("inst_type")]
	public string InstrumentType { get; set; }

	[JsonProperty("display_name")]
	public string DisplayName { get; set; }

	[JsonProperty("base_ccy")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quote_ccy")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("quote_decimals")]
	public int? QuoteDecimals { get; set; }

	[JsonProperty("quantity_decimals")]
	public int? QuantityDecimals { get; set; }

	[JsonProperty("price_tick_size")]
	public string PriceTickSize { get; set; }

	[JsonProperty("qty_tick_size")]
	public string QuantityTickSize { get; set; }

	[JsonProperty("max_leverage")]
	public string MaxLeverage { get; set; }

	[JsonProperty("tradable")]
	public bool IsTradable { get; set; }

	[JsonProperty("expiry_timestamp_ms")]
	public long? ExpiryTimestamp { get; set; }

	[JsonProperty("underlying_symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("contract_size")]
	public string ContractSize { get; set; }

	[JsonProperty("product_type")]
	public string ProductType { get; set; }

	[JsonProperty("margin_buy_enabled")]
	public bool IsMarginBuyEnabled { get; set; }

	[JsonProperty("margin_sell_enabled")]
	public bool IsMarginSellEnabled { get; set; }
}

sealed class CryptoComTicker
{
	[JsonProperty("i")]
	public string InstrumentName { get; set; }

	[JsonProperty("h")]
	public string High { get; set; }

	[JsonProperty("l")]
	public string Low { get; set; }

	[JsonProperty("a")]
	public string Last { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }

	[JsonProperty("vv")]
	public string QuoteVolume { get; set; }

	[JsonProperty("c")]
	public string ChangeRatio { get; set; }

	[JsonProperty("b")]
	public string BestBid { get; set; }

	[JsonProperty("bs")]
	public string BestBidSize { get; set; }

	[JsonProperty("k")]
	public string BestAsk { get; set; }

	[JsonProperty("ks")]
	public string BestAskSize { get; set; }

	[JsonProperty("oi")]
	public string OpenInterest { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }
}

sealed class CryptoComPublicTrade
{
	[JsonProperty("d")]
	public string TradeId { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("tn")]
	public long? TimeNanoseconds { get; set; }

	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("q")]
	public string Quantity { get; set; }

	[JsonProperty("s")]
	public CryptoComSides Side { get; set; }

	[JsonProperty("i")]
	public string InstrumentName { get; set; }

	[JsonProperty("m")]
	public string MatchId { get; set; }
}

sealed class CryptoComCandle
{
	[JsonProperty("o")]
	public string Open { get; set; }

	[JsonProperty("h")]
	public string High { get; set; }

	[JsonProperty("l")]
	public string Low { get; set; }

	[JsonProperty("c")]
	public string Close { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }

	[JsonProperty("t")]
	public long OpenTime { get; set; }
}

[JsonConverter(typeof(CryptoComBookLevelConverter))]
sealed class CryptoComBookLevel
{
	public string Price { get; set; }
	public string Quantity { get; set; }
	public string OrderCount { get; set; }
}

sealed class CryptoComBookLevelConverter : JsonConverter<CryptoComBookLevel>
{
	public override CryptoComBookLevel ReadJson(JsonReader reader, Type objectType,
		CryptoComBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Crypto.com order-book level must be a JSON array.");

		var level = new CryptoComBookLevel
		{
			Price = Read<string>(reader, serializer),
			Quantity = Read<string>(reader, serializer),
			OrderCount = Read<string>(reader, serializer),
		};

		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Crypto.com order-book level has an invalid number of fields.");

		return level;
	}

	private static T Read<T>(JsonReader reader, JsonSerializer serializer)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException("Crypto.com order-book level is incomplete.");
		return serializer.Deserialize<T>(reader);
	}

	public override void WriteJson(JsonWriter writer, CryptoComBookLevel value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

class CryptoComBookSnapshot
{
	[JsonProperty("bids")]
	public CryptoComBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public CryptoComBookLevel[] Asks { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("tt")]
	public long? TransactionTime { get; set; }

	[JsonProperty("u")]
	public long? Sequence { get; set; }
}

sealed class CryptoComBookUpdate
{
	[JsonProperty("bids")]
	public CryptoComBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public CryptoComBookLevel[] Asks { get; set; }
}

sealed class CryptoComWsBookItem : CryptoComBookSnapshot
{
	[JsonProperty("update")]
	public CryptoComBookUpdate Update { get; set; }

	[JsonProperty("pu")]
	public long? PreviousSequence { get; set; }
}
