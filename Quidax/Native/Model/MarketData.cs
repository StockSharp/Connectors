namespace StockSharp.Quidax.Native.Model;

sealed class QuidaxTradingRules
{
	[JsonProperty("base_precision")]
	public int BasePrecision { get; set; }

	[JsonProperty("quote_precision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("price_precision")]
	public int PricePrecision { get; set; }

	[JsonProperty("minimum_order_size")]
	public decimal? MinimumOrderSize { get; set; }
}

sealed class QuidaxMarket
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("base_unit")]
	public string BaseUnit { get; set; }

	[JsonProperty("quote_unit")]
	public string QuoteUnit { get; set; }

	[JsonProperty("trading_rules")]
	public QuidaxTradingRules TradingRules { get; set; }

	[JsonIgnore]
	public string SecurityCode
		=> QuidaxExtensions.CreateSecurityCode(BaseUnit, QuoteUnit);

	[JsonIgnore]
	public decimal? PriceStep
		=> QuidaxExtensions.GetStep(TradingRules?.PricePrecision ?? -1);

	[JsonIgnore]
	public decimal? VolumeStep
		=> QuidaxExtensions.GetStep(TradingRules?.BasePrecision ?? -1);

	[JsonIgnore]
	public decimal? MinimumOrderValue
		=> TradingRules?.MinimumOrderSize;
}

sealed class QuidaxTicker
{
	[JsonProperty("high")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("vol")]
	public decimal? Volume { get; set; }

	[JsonProperty("last")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("low")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("buy")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("sell")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("open")]
	public decimal? OpenPrice { get; set; }

	[JsonIgnore]
	public long Timestamp { get; set; }
}

sealed class QuidaxTickerEntry
{
	[JsonProperty("ticker")]
	public QuidaxTicker Ticker { get; set; }

	[JsonProperty("at")]
	public long Timestamp { get; set; }
}

sealed class QuidaxDepth
{
	[JsonProperty("asks")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("bids")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class QuidaxTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public JToken PriceValue { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("base_volume")]
	public decimal BaseVolume { get; set; }

	[JsonProperty("quote_volume")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("volume")]
	public QuidaxMoney Volume { get; set; }

	[JsonProperty("created_at")]
	public DateTime? CreatedAt { get; set; }

	[JsonIgnore]
	public string EffectiveId => TradeId ?? Id;

	[JsonIgnore]
	public decimal EffectiveVolume
		=> BaseVolume > 0 ? BaseVolume : Volume?.Amount ?? 0;

	[JsonIgnore]
	public decimal Price
		=> PriceValue switch
		{
			JObject value => value["amount"]?.Value<decimal>() ?? 0,
			null => 0,
			_ => PriceValue.Value<decimal>(),
		};

	[JsonIgnore]
	public string EffectiveSide => Side ?? Type;
}

[JsonConverter(typeof(QuidaxCandleConverter))]
sealed class QuidaxCandle
{
	public long Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal Close { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Volume { get; set; }
}

sealed class QuidaxCandleConverter : JsonConverter<QuidaxCandle>
{
	public override QuidaxCandle ReadJson(
		JsonReader reader,
		Type objectType,
		QuidaxCandle existingValue,
		bool hasExistingValue,
		JsonSerializer serializer)
	{
		var values = JArray.Load(reader);
		if (values.Count < 6)
			throw new JsonSerializationException(
				"Quidax candle must contain six values.");
		return new()
		{
			Timestamp = values[0].Value<long>(),
			Open = values[1].Value<decimal>(),
			Close = values[2].Value<decimal>(),
			High = values[3].Value<decimal>(),
			Low = values[4].Value<decimal>(),
			Volume = values[5].Value<decimal>(),
		};
	}

	public override void WriteJson(
		JsonWriter writer,
		QuidaxCandle value,
		JsonSerializer serializer)
		=> JArray.FromObject(new object[]
		{
			value.Timestamp,
			value.Open,
			value.Close,
			value.High,
			value.Low,
			value.Volume,
		}, serializer).WriteTo(writer);
}
