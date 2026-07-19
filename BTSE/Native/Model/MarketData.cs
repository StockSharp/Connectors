namespace StockSharp.BTSE.Native.Model;

sealed class BTSEMarketSummary
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("lowestAsk")]
	public decimal? LowestAsk { get; set; }

	[JsonProperty("highestBid")]
	public decimal? HighestBid { get; set; }

	[JsonProperty("percentageChange")]
	public decimal? PercentageChange { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("high24Hr")]
	public decimal? High24Hours { get; set; }

	[JsonProperty("low24Hr")]
	public decimal? Low24Hours { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("active")]
	public bool IsActive { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("minValidPrice")]
	public decimal? MinimumValidPrice { get; set; }

	[JsonProperty("minPriceIncrement")]
	public decimal? MinimumPriceIncrement { get; set; }

	[JsonProperty("minOrderSize")]
	public decimal? MinimumOrderSize { get; set; }

	[JsonProperty("maxOrderSize")]
	public decimal? MaximumOrderSize { get; set; }

	[JsonProperty("minSizeIncrement")]
	public decimal? MinimumSizeIncrement { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("openInterestUSD")]
	public decimal? OpenInterestUsd { get; set; }

	[JsonProperty("contractStart")]
	public long? ContractStart { get; set; }

	[JsonProperty("contractEnd")]
	public long? ContractEnd { get; set; }

	[JsonProperty("timeBasedContract")]
	public bool IsTimeBasedContract { get; set; }

	[JsonProperty("fundingRate")]
	public decimal? FundingRate { get; set; }

	[JsonProperty("contractSize")]
	public decimal? ContractSize { get; set; }

	[JsonProperty("availableSettlement")]
	public string[] AvailableSettlement { get; set; }

	[JsonProperty("futures")]
	public bool IsFutures { get; set; }

	[JsonProperty("fundingIntervalMinutes")]
	public int? FundingIntervalMinutes { get; set; }

	[JsonProperty("fundingTime")]
	public long? FundingTime { get; set; }
}

sealed class BTSEMarketQuery : IBTSEQuery
{
	public string Symbol { get; init; }
	public bool IsFullAttributes { get; init; }

	public BTSEParameter[] GetParameters()
	{
		var result = new List<BTSEParameter>();
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		if (IsFullAttributes)
			result.Add(new("listFullAttributes", "true"));
		return [.. result];
	}
}

sealed class BTSEMarketPrice
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("indexPrice")]
	public decimal? IndexPrice { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("markPrice")]
	public decimal? MarkPrice { get; set; }
}

sealed class BTSEQuote
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }
}

sealed class BTSEOrderBook
{
	[JsonProperty("buyQuote")]
	public BTSEQuote[] Bids { get; set; }

	[JsonProperty("sellQuote")]
	public BTSEQuote[] Asks { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class BTSEDepthQuery : IBTSEQuery
{
	public string Symbol { get; init; }
	public int Depth { get; init; }

	public BTSEParameter[] GetParameters()
		=>
		[
			new("symbol", Symbol),
			new("depth", Depth.ToString(CultureInfo.InvariantCulture)),
		];
}

sealed class BTSEPublicTrade
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("serialId")]
	public long SerialId { get; set; }

	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class BTSETradesQuery : IBTSEQuery
{
	public string Symbol { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Count { get; init; }
	public bool? IsIncludeOld { get; init; }

	public BTSEParameter[] GetParameters()
	{
		var result = new List<BTSEParameter> { new("symbol", Symbol) };
		if (StartTime is long startTime)
			result.Add(new("startTime", startTime.ToString(CultureInfo.InvariantCulture)));
		if (EndTime is long endTime)
			result.Add(new("endTime", endTime.ToString(CultureInfo.InvariantCulture)));
		if (Count > 0)
			result.Add(new("count", Count.ToString(CultureInfo.InvariantCulture)));
		if (IsIncludeOld is bool isIncludeOld)
			result.Add(new("includeOld", isIncludeOld ? "true" : "false"));
		return [.. result];
	}
}

[JsonConverter(typeof(BTSECandleConverter))]
sealed class BTSECandle
{
	public long Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}

sealed class BTSECandlesQuery : IBTSEQuery
{
	public string Symbol { get; init; }
	public long? Start { get; init; }
	public long? End { get; init; }
	public string Resolution { get; init; }

	public BTSEParameter[] GetParameters()
	{
		var result = new List<BTSEParameter>
		{
			new("symbol", Symbol),
			new("resolution", Resolution),
		};
		if (Start is long start)
			result.Add(new("start", start.ToString(CultureInfo.InvariantCulture)));
		if (End is long end)
			result.Add(new("end", end.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class BTSECandleConverter : JsonConverter<BTSECandle>
{
	public override BTSECandle ReadJson(JsonReader reader, Type objectType,
		BTSECandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("BTSE candle must be an array.");

		var timestamp = ReadInt64(reader, "timestamp");
		var open = ReadDecimal(reader, "open");
		var high = ReadDecimal(reader, "high");
		var low = ReadDecimal(reader, "low");
		var close = ReadDecimal(reader, "close");
		var volume = ReadDecimal(reader, "volume");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("BTSE candle has unexpected fields.");

		return new()
		{
			Timestamp = timestamp,
			Open = open,
			High = high,
			Low = low,
			Close = close,
			Volume = volume,
		};
	}

	public override void WriteJson(JsonWriter writer, BTSECandle value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Timestamp);
		writer.WriteValue(value.Open);
		writer.WriteValue(value.High);
		writer.WriteValue(value.Low);
		writer.WriteValue(value.Close);
		writer.WriteValue(value.Volume);
		writer.WriteEndArray();
	}

	private static long ReadInt64(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"BTSE candle has no {field}.");
		return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
	}

	private static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"BTSE candle has no {field}.");
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}
}
