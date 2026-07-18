namespace StockSharp.Benzinga.Native.Model;

[JsonConverter(typeof(BenzingaDelayedQuoteResponseConverter))]
sealed class BenzingaDelayedQuoteResponse
{
	public List<BenzingaDelayedQuote> Quotes { get; } = [];
}

sealed class BenzingaDelayedQuote
{
	[JsonIgnore]
	public string ResponseKey { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("isoExchange")]
	public string IsoExchange { get; set; }

	[JsonProperty("bzExchange")]
	public string BenzingaExchange { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("companyStandardName")]
	public string CompanyStandardName { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("sector")]
	public string Sector { get; set; }

	[JsonProperty("industry")]
	public string Industry { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("cusip")]
	public string Cusip { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("bidTime")]
	public long? BidTime { get; set; }

	[JsonProperty("askTime")]
	public long? AskTime { get; set; }

	[JsonProperty("lastTradePrice")]
	public decimal? LastTradePrice { get; set; }

	[JsonProperty("lastTradeTime")]
	public long? LastTradeTime { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("change")]
	public decimal? Change { get; set; }

	[JsonProperty("changePercent")]
	public decimal? ChangePercent { get; set; }

	[JsonProperty("previousClosePrice")]
	public decimal? PreviousClosePrice { get; set; }

	[JsonProperty("previousCloseDate")]
	public string PreviousCloseDate { get; set; }

	[JsonProperty("closeDate")]
	public string CloseDate { get; set; }

	[JsonProperty("fiftyDayAveragePrice")]
	public decimal? FiftyDayAveragePrice { get; set; }

	[JsonProperty("hundredDayAveragePrice")]
	public decimal? HundredDayAveragePrice { get; set; }

	[JsonProperty("twoHundredDayAveragePrice")]
	public decimal? TwoHundredDayAveragePrice { get; set; }

	[JsonProperty("averageVolume")]
	public decimal? AverageVolume { get; set; }

	[JsonProperty("fiftyTwoWeekHigh")]
	public decimal? FiftyTwoWeekHigh { get; set; }

	[JsonProperty("fiftyTwoWeekLow")]
	public decimal? FiftyTwoWeekLow { get; set; }

	[JsonProperty("marketCap")]
	public decimal? MarketCap { get; set; }

	[JsonProperty("sharesOutstanding")]
	public decimal? SharesOutstanding { get; set; }

	[JsonProperty("sharesFloat")]
	public decimal? SharesFloat { get; set; }

	[JsonProperty("pe")]
	public decimal? PriceEarnings { get; set; }

	[JsonProperty("forwardPE")]
	public decimal? ForwardPriceEarnings { get; set; }

	[JsonProperty("dividendYield")]
	public decimal? DividendYield { get; set; }

	[JsonProperty("dividend")]
	public decimal? Dividend { get; set; }

	[JsonProperty("payoutRatio")]
	public decimal? PayoutRatio { get; set; }

	[JsonProperty("ethPrice")]
	public decimal? ExtendedHoursPrice { get; set; }

	[JsonProperty("ethVolume")]
	public decimal? ExtendedHoursVolume { get; set; }

	[JsonProperty("ethTime")]
	public long? ExtendedHoursTime { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("issuerName")]
	public string IssuerName { get; set; }

	[JsonProperty("issuerShortName")]
	public string IssuerShortName { get; set; }
}

sealed class BenzingaBarsResponse
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("interval")]
	public long? Interval { get; set; }

	[JsonProperty("candles")]
	public BenzingaCandle[] Candles { get; set; }
}

sealed class BenzingaCandle
{
	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("dateTime")]
	public string DateTime { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }
}

sealed class BenzingaDelayedQuoteResponseConverter : JsonConverter<BenzingaDelayedQuoteResponse>
{
	public override bool CanWrite => false;

	public override BenzingaDelayedQuoteResponse ReadJson(JsonReader reader, Type objectType,
		BenzingaDelayedQuoteResponse existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.None && !reader.Read())
			return null;
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("Benzinga delayed quote response must be an object.");

		var response = hasExistingValue && existingValue != null
			? existingValue : new BenzingaDelayedQuoteResponse();
		while (reader.Read())
		{
			if (reader.TokenType == JsonToken.EndObject)
				return response;
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("Invalid Benzinga delayed quote property.");
			var responseKey = reader.Value as string;
			if (!reader.Read())
				throw new JsonSerializationException("Unexpected end of Benzinga delayed quote response.");
			if (reader.TokenType == JsonToken.Null)
				continue;
			var quote = serializer.Deserialize<BenzingaDelayedQuote>(reader);
			if (quote == null)
				continue;
			quote.ResponseKey = responseKey;
			quote.Symbol = quote.Symbol.IsEmpty(responseKey);
			response.Quotes.Add(quote);
		}
		throw new JsonSerializationException("Unexpected end of Benzinga delayed quote response.");
	}

	public override void WriteJson(JsonWriter writer, BenzingaDelayedQuoteResponse value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}
