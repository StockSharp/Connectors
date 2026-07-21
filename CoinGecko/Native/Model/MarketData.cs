namespace StockSharp.CoinGecko.Native.Model;

sealed class CoinGeckoCoinMarket
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("current_price")]
	public decimal? CurrentPrice { get; set; }

	[JsonProperty("market_cap")]
	public decimal? MarketCap { get; set; }

	[JsonProperty("market_cap_rank")]
	public int? MarketCapRank { get; set; }

	[JsonProperty("total_volume")]
	public decimal? TotalVolume { get; set; }

	[JsonProperty("high_24h")]
	public decimal? High24Hours { get; set; }

	[JsonProperty("low_24h")]
	public decimal? Low24Hours { get; set; }

	[JsonProperty("price_change_24h")]
	public decimal? PriceChange24Hours { get; set; }

	[JsonProperty("price_change_percentage_24h")]
	public decimal? PriceChangePercentage24Hours { get; set; }

	[JsonProperty("circulating_supply")]
	public decimal? CirculatingSupply { get; set; }

	[JsonProperty("total_supply")]
	public decimal? TotalSupply { get; set; }

	[JsonProperty("max_supply")]
	public decimal? MaximumSupply { get; set; }

	[JsonProperty("last_updated")]
	public string LastUpdated { get; set; }
}

[JsonConverter(typeof(CoinGeckoCoinOhlcConverter))]
sealed class CoinGeckoCoinOhlc
{
	public decimal Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
}

sealed class CoinGeckoCoinOhlcConverter : CoinGeckoArrayConverter<CoinGeckoCoinOhlc>
{
	protected override CoinGeckoCoinOhlc ReadValues(JsonReader reader)
		=> new()
		{
			Timestamp = ReadDecimal(reader, "timestamp"),
			Open = ReadDecimal(reader, "open"),
			High = ReadDecimal(reader, "high"),
			Low = ReadDecimal(reader, "low"),
			Close = ReadDecimal(reader, "close"),
		};

	protected override void WriteValues(JsonWriter writer, CoinGeckoCoinOhlc value)
	{
		writer.WriteValue(value.Timestamp);
		writer.WriteValue(value.Open);
		writer.WriteValue(value.High);
		writer.WriteValue(value.Low);
		writer.WriteValue(value.Close);
	}
}

sealed class CoinGeckoPoolSearchResponse
{
	[JsonProperty("data")]
	public CoinGeckoPoolResource[] Data { get; set; }

	[JsonProperty("included")]
	public CoinGeckoIncludedResource[] Included { get; set; }
}

sealed class CoinGeckoPoolResponse
{
	[JsonProperty("data")]
	public CoinGeckoPoolResource Data { get; set; }

	[JsonProperty("included")]
	public CoinGeckoIncludedResource[] Included { get; set; }
}

sealed class CoinGeckoPoolResource
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public CoinGeckoResourceTypes Type { get; set; }

	[JsonProperty("attributes")]
	public CoinGeckoPoolAttributes Attributes { get; set; }

	[JsonProperty("relationships")]
	public CoinGeckoPoolRelationships Relationships { get; set; }
}

sealed class CoinGeckoPoolAttributes
{
	[JsonProperty("base_token_price_usd")]
	public string BaseTokenPriceUsd { get; set; }

	[JsonProperty("base_token_price_native_currency")]
	public string BaseTokenPriceNative { get; set; }

	[JsonProperty("quote_token_price_usd")]
	public string QuoteTokenPriceUsd { get; set; }

	[JsonProperty("base_token_price_quote_token")]
	public string BaseTokenPriceQuote { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("pool_created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("fdv_usd")]
	public string FullyDilutedValueUsd { get; set; }

	[JsonProperty("market_cap_usd")]
	public string MarketCapUsd { get; set; }

	[JsonProperty("price_change_percentage")]
	public CoinGeckoPeriodValues PriceChangePercentage { get; set; }

	[JsonProperty("volume_usd")]
	public CoinGeckoPeriodValues VolumeUsd { get; set; }

	[JsonProperty("reserve_in_usd")]
	public string ReserveUsd { get; set; }
}

sealed class CoinGeckoPeriodValues
{
	[JsonProperty("m5")]
	public string Minutes5 { get; set; }

	[JsonProperty("m15")]
	public string Minutes15 { get; set; }

	[JsonProperty("m30")]
	public string Minutes30 { get; set; }

	[JsonProperty("h1")]
	public string Hours1 { get; set; }

	[JsonProperty("h6")]
	public string Hours6 { get; set; }

	[JsonProperty("h24")]
	public string Hours24 { get; set; }
}

sealed class CoinGeckoPoolRelationships
{
	[JsonProperty("base_token")]
	public CoinGeckoRelationship BaseToken { get; set; }

	[JsonProperty("quote_token")]
	public CoinGeckoRelationship QuoteToken { get; set; }

	[JsonProperty("dex")]
	public CoinGeckoRelationship Dex { get; set; }
}

sealed class CoinGeckoRelationship
{
	[JsonProperty("data")]
	public CoinGeckoResourceReference Data { get; set; }
}

sealed class CoinGeckoResourceReference
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public CoinGeckoResourceTypes Type { get; set; }
}

sealed class CoinGeckoIncludedResource
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public CoinGeckoResourceTypes Type { get; set; }

	[JsonProperty("attributes")]
	public CoinGeckoIncludedAttributes Attributes { get; set; }
}

sealed class CoinGeckoIncludedAttributes
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("decimals")]
	public int? Decimals { get; set; }

	[JsonProperty("coingecko_coin_id")]
	public string CoinId { get; set; }
}

sealed class CoinGeckoTradesResponse
{
	[JsonProperty("data")]
	public CoinGeckoTradeResource[] Data { get; set; }
}

sealed class CoinGeckoTradeResource
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public CoinGeckoResourceTypes Type { get; set; }

	[JsonProperty("attributes")]
	public CoinGeckoTradeAttributes Attributes { get; set; }
}

sealed class CoinGeckoTradeAttributes
{
	[JsonProperty("block_number")]
	public long? BlockNumber { get; set; }

	[JsonProperty("tx_hash")]
	public string TransactionHash { get; set; }

	[JsonProperty("from_token_amount")]
	public string FromTokenAmount { get; set; }

	[JsonProperty("to_token_amount")]
	public string ToTokenAmount { get; set; }

	[JsonProperty("price_from_in_usd")]
	public string FromPriceUsd { get; set; }

	[JsonProperty("price_to_in_usd")]
	public string ToPriceUsd { get; set; }

	[JsonProperty("block_timestamp")]
	public string BlockTimestamp { get; set; }

	[JsonProperty("kind")]
	public CoinGeckoTradeKinds Kind { get; set; }

	[JsonProperty("volume_in_usd")]
	public string VolumeUsd { get; set; }

	[JsonProperty("from_token_address")]
	public string FromTokenAddress { get; set; }

	[JsonProperty("to_token_address")]
	public string ToTokenAddress { get; set; }
}

sealed class CoinGeckoPoolOhlcvResponse
{
	[JsonProperty("data")]
	public CoinGeckoPoolOhlcvResource Data { get; set; }

	[JsonProperty("meta")]
	public CoinGeckoPoolOhlcvMeta Meta { get; set; }
}

sealed class CoinGeckoPoolOhlcvResource
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public CoinGeckoResourceTypes Type { get; set; }

	[JsonProperty("attributes")]
	public CoinGeckoPoolOhlcvAttributes Attributes { get; set; }
}

sealed class CoinGeckoPoolOhlcvAttributes
{
	[JsonProperty("ohlcv_list")]
	public CoinGeckoPoolOhlcv[] Items { get; set; }
}

sealed class CoinGeckoPoolOhlcvMeta
{
	[JsonProperty("base")]
	public CoinGeckoPoolOhlcvToken BaseToken { get; set; }

	[JsonProperty("quote")]
	public CoinGeckoPoolOhlcvToken QuoteToken { get; set; }
}

sealed class CoinGeckoPoolOhlcvToken
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("coingecko_coin_id")]
	public string CoinId { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }
}

[JsonConverter(typeof(CoinGeckoPoolOhlcvConverter))]
sealed class CoinGeckoPoolOhlcv
{
	public decimal Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}

sealed class CoinGeckoPoolOhlcvConverter : CoinGeckoArrayConverter<CoinGeckoPoolOhlcv>
{
	protected override CoinGeckoPoolOhlcv ReadValues(JsonReader reader)
		=> new()
		{
			Timestamp = ReadDecimal(reader, "timestamp"),
			Open = ReadDecimal(reader, "open"),
			High = ReadDecimal(reader, "high"),
			Low = ReadDecimal(reader, "low"),
			Close = ReadDecimal(reader, "close"),
			Volume = ReadDecimal(reader, "volume"),
		};

	protected override void WriteValues(JsonWriter writer, CoinGeckoPoolOhlcv value)
	{
		writer.WriteValue(value.Timestamp);
		writer.WriteValue(value.Open);
		writer.WriteValue(value.High);
		writer.WriteValue(value.Low);
		writer.WriteValue(value.Close);
		writer.WriteValue(value.Volume);
	}
}

abstract class CoinGeckoArrayConverter<TValue> : JsonConverter<TValue>
	where TValue : class
{
	public override TValue ReadJson(JsonReader reader, Type objectType,
		TValue existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				$"Expected a CoinGecko array, received {reader.TokenType}.");
		var value = ReadValues(reader);
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"CoinGecko array has an unexpected number of fields.");
		return value;
	}

	public override void WriteJson(JsonWriter writer, TValue value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		WriteValues(writer, value);
		writer.WriteEndArray();
	}

	protected abstract TValue ReadValues(JsonReader reader);
	protected abstract void WriteValues(JsonWriter writer, TValue value);

	protected static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not JsonToken.Integer and
			not JsonToken.Float and not JsonToken.String ||
			!decimal.TryParse(reader.Value?.ToString(), NumberStyles.Float,
				CultureInfo.InvariantCulture, out var value))
			throw new JsonSerializationException(
				$"CoinGecko array field '{field}' is not a decimal value.");
		return value;
	}
}
