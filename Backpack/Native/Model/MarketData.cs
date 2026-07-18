namespace StockSharp.Backpack.Native.Model;

sealed class BackpackMarket
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("baseSymbol")]
	public string BaseSymbol { get; set; }

	[JsonProperty("quoteSymbol")]
	public string QuoteSymbol { get; set; }

	[JsonProperty("marketType")]
	public BackpackMarketTypes MarketType { get; set; }

	[JsonProperty("filters")]
	public BackpackMarketFilters Filters { get; set; }

	[JsonProperty("fundingInterval")]
	public long? FundingInterval { get; set; }

	[JsonProperty("fundingRateUpperBound")]
	public decimal? FundingRateUpperBound { get; set; }

	[JsonProperty("fundingRateLowerBound")]
	public decimal? FundingRateLowerBound { get; set; }

	[JsonProperty("openInterestLimit")]
	public decimal? OpenInterestLimit { get; set; }

	[JsonProperty("orderBookState")]
	public string OrderBookState { get; set; }

	[JsonProperty("rwaMarketType")]
	public BackpackRealWorldAssetMarketTypes? RealWorldAssetMarketType { get; set; }

	[JsonProperty("visible")]
	public bool IsVisible { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackRealWorldAssetMarketTypes
{
	[EnumMember(Value = "STOCK")]
	Stock,
}

sealed class BackpackMarketFilters
{
	[JsonProperty("price")]
	public BackpackPriceFilter Price { get; set; }

	[JsonProperty("quantity")]
	public BackpackQuantityFilter Quantity { get; set; }
}

sealed class BackpackPriceFilter
{
	[JsonProperty("minPrice")]
	public decimal? MinimumPrice { get; set; }

	[JsonProperty("maxPrice")]
	public decimal? MaximumPrice { get; set; }

	[JsonProperty("tickSize")]
	public decimal TickSize { get; set; }
}

sealed class BackpackQuantityFilter
{
	[JsonProperty("minQuantity")]
	public decimal MinimumQuantity { get; set; }

	[JsonProperty("maxQuantity")]
	public decimal? MaximumQuantity { get; set; }

	[JsonProperty("stepSize")]
	public decimal StepSize { get; set; }
}

sealed class BackpackMarketsQuery : IBackpackParameters
{
	public BackpackMarketTypes[] MarketTypes { get; init; }

	public BackpackParameter[] GetParameters()
		=> MarketTypes is { Length: > 0 }
			? [.. MarketTypes.Select(static type =>
				new BackpackParameter("marketType", type switch
				{
					BackpackMarketTypes.Spot => "SPOT",
					BackpackMarketTypes.Perpetual => "PERP",
					BackpackMarketTypes.InversePerpetual => "IPERP",
					BackpackMarketTypes.Dated => "DATED",
					BackpackMarketTypes.Prediction => "PREDICTION",
					BackpackMarketTypes.RequestForQuote => "RFQ",
					_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
				}))]
			: [];
}

[JsonConverter(typeof(BackpackPriceLevelConverter))]
sealed class BackpackPriceLevel
{
	public decimal Price { get; set; }
	public decimal Quantity { get; set; }
}

sealed class BackpackDepth
{
	[JsonProperty("asks")]
	public BackpackPriceLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public BackpackPriceLevel[] Bids { get; set; }

	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class BackpackDepthQuery : IBackpackParameters
{
	public string Symbol { get; init; }
	public int Limit { get; init; }

	public BackpackParameter[] GetParameters()
		=>
		[
			new("symbol", Symbol),
			new("limit", Limit.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class BackpackTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("firstPrice")]
	public decimal FirstPrice { get; set; }

	[JsonProperty("lastPrice")]
	public decimal LastPrice { get; set; }

	[JsonProperty("priceChange")]
	public decimal PriceChange { get; set; }

	[JsonProperty("priceChangePercent")]
	public decimal PriceChangePercent { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("trades")]
	public long Trades { get; set; }
}

sealed class BackpackSymbolQuery : IBackpackParameters
{
	public string Symbol { get; init; }

	public BackpackParameter[] GetParameters() => [new("symbol", Symbol)];
}

sealed class BackpackTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("quoteQuantity")]
	public decimal QuoteQuantity { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; set; }
}

sealed class BackpackTradesQuery : IBackpackParameters
{
	public string Symbol { get; init; }
	public int Limit { get; init; }

	public BackpackParameter[] GetParameters()
		=>
		[
			new("symbol", Symbol),
			new("limit", Limit.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class BackpackHistoricalTradesQuery : IBackpackParameters
{
	public string Symbol { get; init; }
	public int Limit { get; init; }
	public int Offset { get; init; }

	public BackpackParameter[] GetParameters()
		=>
		[
			new("symbol", Symbol),
			new("limit", Limit.ToString(CultureInfo.InvariantCulture)),
			new("offset", Offset.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class BackpackKline
{
	[JsonProperty("start")]
	public string Start { get; set; }

	[JsonProperty("end")]
	public string End { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("trades")]
	public long Trades { get; set; }
}

sealed class BackpackKlinesQuery : IBackpackParameters
{
	public string Symbol { get; init; }
	public string Interval { get; init; }
	public long StartTime { get; init; }
	public long? EndTime { get; init; }

	public BackpackParameter[] GetParameters()
	{
		var result = new List<BackpackParameter>
		{
			new("symbol", Symbol),
			new("interval", Interval),
			new("startTime", StartTime.ToString(CultureInfo.InvariantCulture)),
		};
		if (EndTime is long endTime)
			result.Add(new("endTime", endTime.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class BackpackMarkPrice
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("fundingRate")]
	public decimal? FundingRate { get; set; }

	[JsonProperty("indexPrice")]
	public decimal? IndexPrice { get; set; }

	[JsonProperty("markPrice")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("nextFundingTimestamp")]
	public long? NextFundingTimestamp { get; set; }
}

sealed class BackpackPriceLevelConverter : JsonConverter<BackpackPriceLevel>
{
	public override BackpackPriceLevel ReadJson(JsonReader reader, Type objectType,
		BackpackPriceLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				"Backpack Exchange price level must be an array.");
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException(
				"Backpack Exchange price level has no price.");
		var price = Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException(
				"Backpack Exchange price level has no quantity.");
		var quantity = Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"Backpack Exchange price level has unexpected fields.");
		return new() { Price = price, Quantity = quantity };
	}

	public override void WriteJson(JsonWriter writer, BackpackPriceLevel value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Price);
		writer.WriteValue(value.Quantity);
		writer.WriteEndArray();
	}
}
