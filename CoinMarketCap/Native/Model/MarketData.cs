namespace StockSharp.CoinMarketCap.Native.Model;

sealed class CoinMarketCapMapEntry
{
	[JsonProperty("id")]
	public int Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("slug")]
	public string Slug { get; set; }

	[JsonProperty("rank")]
	public int? Rank { get; set; }

	[JsonProperty("is_active")]
	public int IsActive { get; set; }

	[JsonProperty("first_historical_data")]
	public string FirstHistoricalData { get; set; }

	[JsonProperty("last_historical_data")]
	public string LastHistoricalData { get; set; }
}

sealed class CoinMarketCapQuoteAsset
{
	[JsonProperty("id")]
	public int Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("slug")]
	public string Slug { get; set; }

	[JsonProperty("is_active")]
	public int IsActive { get; set; }

	[JsonProperty("infinite_supply")]
	public bool IsInfiniteSupply { get; set; }

	[JsonProperty("is_fiat")]
	public int IsFiat { get; set; }

	[JsonProperty("circulating_supply")]
	public decimal? CirculatingSupply { get; set; }

	[JsonProperty("total_supply")]
	public decimal? TotalSupply { get; set; }

	[JsonProperty("max_supply")]
	public decimal? MaximumSupply { get; set; }

	[JsonProperty("date_added")]
	public string DateAdded { get; set; }

	[JsonProperty("num_market_pairs")]
	public int? MarketPairCount { get; set; }

	[JsonProperty("cmc_rank")]
	public int? Rank { get; set; }

	[JsonProperty("last_updated")]
	public string LastUpdated { get; set; }

	[JsonProperty("quote")]
	public CoinMarketCapQuote[] Quotes { get; set; }
}

sealed class CoinMarketCapQuote
{
	[JsonProperty("id")]
	public int Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("volume_24h")]
	public decimal? Volume24Hours { get; set; }

	[JsonProperty("cex_volume_24h")]
	public decimal? CentralizedVolume24Hours { get; set; }

	[JsonProperty("dex_volume_24h")]
	public decimal? DecentralizedVolume24Hours { get; set; }

	[JsonProperty("volume_change_24h")]
	public decimal? VolumeChange24Hours { get; set; }

	[JsonProperty("percent_change_1h")]
	public decimal? PriceChange1Hour { get; set; }

	[JsonProperty("percent_change_24h")]
	public decimal? PriceChange24Hours { get; set; }

	[JsonProperty("percent_change_7d")]
	public decimal? PriceChange7Days { get; set; }

	[JsonProperty("percent_change_30d")]
	public decimal? PriceChange30Days { get; set; }

	[JsonProperty("percent_change_60d")]
	public decimal? PriceChange60Days { get; set; }

	[JsonProperty("percent_change_90d")]
	public decimal? PriceChange90Days { get; set; }

	[JsonProperty("market_cap")]
	public decimal? MarketCapitalization { get; set; }

	[JsonProperty("market_cap_dominance")]
	public decimal? MarketCapitalizationDominance { get; set; }

	[JsonProperty("fully_diluted_market_cap")]
	public decimal? FullyDilutedMarketCapitalization { get; set; }

	[JsonProperty("last_updated")]
	public string LastUpdated { get; set; }
}

sealed class CoinMarketCapHistoricalData
{
	[JsonProperty("id")]
	public int Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("quotes")]
	public CoinMarketCapOhlcv[] Quotes { get; set; }
}

sealed class CoinMarketCapOhlcv
{
	[JsonProperty("time_open")]
	public string OpenTime { get; set; }

	[JsonProperty("time_close")]
	public string CloseTime { get; set; }

	[JsonProperty("time_high")]
	public string HighTime { get; set; }

	[JsonProperty("time_low")]
	public string LowTime { get; set; }

	[JsonProperty("quote")]
	public CoinMarketCapOhlcvQuote Quote { get; set; }
}

[JsonConverter(typeof(CoinMarketCapOhlcvQuoteConverter))]
sealed class CoinMarketCapOhlcvQuote
{
	public string Currency { get; set; }
	public CoinMarketCapOhlcvValues Values { get; set; }
}

sealed class CoinMarketCapOhlcvValues
{
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

	[JsonProperty("market_cap")]
	public decimal? MarketCapitalization { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }
}

sealed class CoinMarketCapOhlcvQuoteConverter :
	JsonConverter<CoinMarketCapOhlcvQuote>
{
	public override bool CanWrite => false;

	public override CoinMarketCapOhlcvQuote ReadJson(JsonReader reader,
		Type objectType, CoinMarketCapOhlcvQuote existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartObject || !reader.Read() ||
			reader.TokenType != JsonToken.PropertyName)
			throw new JsonSerializationException(
				"CoinMarketCap OHLCV quote must contain a currency object.");
		var currency = reader.Value?.ToString();
		if (!reader.Read())
			throw new JsonSerializationException(
				"CoinMarketCap OHLCV quote is incomplete.");
		var values = serializer.Deserialize<CoinMarketCapOhlcvValues>(reader);
		if (!reader.Read() || reader.TokenType != JsonToken.EndObject)
			throw new JsonSerializationException(
				"CoinMarketCap OHLCV quote must contain exactly one currency.");
		return new()
		{
			Currency = currency,
			Values = values,
		};
	}

	public override void WriteJson(JsonWriter writer,
		CoinMarketCapOhlcvQuote value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}
