namespace StockSharp.NasdaqCloudDataService.Native.Model;

sealed class NasdaqCloudLastSale
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("conditions")]
	public string Conditions { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("securityClass")]
	public string SecurityClass { get; set; }

	[JsonProperty("changeIndicator")]
	public int? ChangeIndicator { get; set; }
}

sealed class NasdaqCloudLastQuote
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("bidVenue")]
	public string BidVenue { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("askVenue")]
	public string AskVenue { get; set; }
}

sealed class NasdaqCloudEquitySnapshot
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("lastTrade")]
	public decimal? LastTrade { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("lastSale")]
	public decimal? LastSale { get; set; }

	[JsonProperty("previousClose")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("dollarVolume")]
	public decimal? DollarVolume { get; set; }

	[JsonProperty("netChange")]
	public decimal? NetChange { get; set; }

	[JsonProperty("percentChange")]
	public decimal? PercentChange { get; set; }
}

sealed class NasdaqCloudBar
{
	[JsonProperty("t")]
	public string Timestamp { get; set; }

	[JsonProperty("o")]
	public decimal? Open { get; set; }

	[JsonProperty("h")]
	public decimal? High { get; set; }

	[JsonProperty("l")]
	public decimal? Low { get; set; }

	[JsonProperty("c")]
	public decimal? Close { get; set; }

	[JsonProperty("v")]
	public decimal? Volume { get; set; }
}

sealed class NasdaqCloudSymbolBars
{
	public string Symbol { get; set; }
	public NasdaqCloudBar[] Bars { get; set; }
}

[JsonConverter(typeof(NasdaqCloudBarsResponseConverter))]
sealed class NasdaqCloudBarsResponse
{
	public NasdaqCloudSymbolBars[] Series { get; set; }
}

sealed class NasdaqCloudBarsResponseConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(NasdaqCloudBarsResponse);

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return new NasdaqCloudBarsResponse { Series = [] };
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("Nasdaq Cloud bars response must be an object.");

		var series = new List<NasdaqCloudSymbolBars>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("Nasdaq Cloud bars response has an invalid property.");
			var symbol = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException("Nasdaq Cloud bars response is incomplete.");
			var bars = serializer.Deserialize<NasdaqCloudBar[]>(reader) ?? [];
			series.Add(new NasdaqCloudSymbolBars { Symbol = symbol, Bars = bars });
		}
		if (reader.TokenType != JsonToken.EndObject)
			throw new JsonSerializationException("Nasdaq Cloud bars response is not terminated.");

		return new NasdaqCloudBarsResponse { Series = series.ToArray() };
	}

	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class NasdaqCloudIndexValue
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("tickValue")]
	public decimal? TickValue { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }
}

class NasdaqCloudSummarySnapshot
{
	[JsonProperty("summaryType")]
	public string SummaryType { get; set; }

	[JsonProperty("sodValue")]
	public decimal? StartOfDayValue { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("eodValue")]
	public decimal? EndOfDayValue { get; set; }

	[JsonProperty("netChange")]
	public decimal? NetChange { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }
}

sealed class NasdaqCloudIndexSnapshot : NasdaqCloudSummarySnapshot
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }
}

sealed class NasdaqCloudEtpValue
{
	[JsonProperty("etpIpvSymbol")]
	public string IpvSymbol { get; set; }

	[JsonProperty("ipvValue")]
	public decimal? IpvValue { get; set; }

	[JsonProperty("timeStamp")]
	public string Timestamp { get; set; }
}

sealed class NasdaqCloudEtpSnapshot : NasdaqCloudSummarySnapshot
{
	[JsonProperty("etpIpvSymbol")]
	public string IpvSymbol { get; set; }
}
