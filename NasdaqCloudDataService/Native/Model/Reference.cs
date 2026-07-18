namespace StockSharp.NasdaqCloudDataService.Native.Model;

sealed class NasdaqCloudEquity
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("securityName")]
	public string SecurityName { get; set; }

	[JsonProperty("listingExchange")]
	public string ListingExchange { get; set; }

	[JsonProperty("etf")]
	public bool IsEtf { get; set; }

	[JsonProperty("ipoFlag")]
	public string IpoFlag { get; set; }
}

sealed class NasdaqCloudIndex
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("instrumentName")]
	public string InstrumentName { get; set; }

	[JsonProperty("fpType")]
	public string FinancialProductType { get; set; }

	[JsonProperty("assetType")]
	public string AssetType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("calculationMethod")]
	public string CalculationMethod { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("schedule")]
	public string Schedule { get; set; }

	[JsonProperty("frequency")]
	public string Frequency { get; set; }

	[JsonProperty("baseDate")]
	public int? BaseDate { get; set; }
}

sealed class NasdaqCloudEtp
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("fpType")]
	public string FinancialProductType { get; set; }

	[JsonProperty("etpIpvSymbol")]
	public string IpvSymbol { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("navSymbol")]
	public string NavSymbol { get; set; }

	[JsonProperty("ecuSymbol")]
	public string EstimatedCashPerUnitSymbol { get; set; }

	[JsonProperty("totalCashSymbol")]
	public string TotalCashSymbol { get; set; }

	[JsonProperty("ecsSymbol")]
	public string EstimatedCashPerShareSymbol { get; set; }

	[JsonProperty("tsoSymbol")]
	public string TotalSharesOutstandingSymbol { get; set; }

	[JsonProperty("effectiveDate")]
	public int? EffectiveDate { get; set; }
}

enum NasdaqCloudOptionTypes
{
	Call,
	Put,
}

class NasdaqCloudOptionContract
{
	[JsonProperty("identifier")]
	public string Identifier { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("expiration")]
	public DateTime? Expiration { get; set; }

	[JsonProperty("strikePrice")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("optionType")]
	public string OptionTypeCode { get; set; }

	[JsonIgnore]
	public NasdaqCloudOptionTypes? OptionType
		=> OptionTypeCode?.ToUpperInvariant() switch
		{
			"C" => NasdaqCloudOptionTypes.Call,
			"P" => NasdaqCloudOptionTypes.Put,
			_ => null,
		};
}

[JsonConverter(typeof(NasdaqCloudOptionContractsConverter))]
sealed class NasdaqCloudOptionContractsResponse
{
	public NasdaqCloudOptionContract[] Contracts { get; set; }
}

sealed class NasdaqCloudOptionContractsConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(NasdaqCloudOptionContractsResponse);

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
		JsonSerializer serializer)
	{
		var contracts = new List<NasdaqCloudOptionContract>();
		if (reader.TokenType == JsonToken.StartObject)
		{
			contracts.Add(serializer.Deserialize<NasdaqCloudOptionContract>(reader));
		}
		else if (reader.TokenType == JsonToken.StartArray)
		{
			while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			{
				var contract = serializer.Deserialize<NasdaqCloudOptionContract>(reader);
				if (contract != null)
					contracts.Add(contract);
			}
			if (reader.TokenType != JsonToken.EndArray)
				throw new JsonSerializationException("Nasdaq Cloud option contract list is not terminated.");
		}
		else if (reader.TokenType != JsonToken.Null)
			throw new JsonSerializationException(
				"Nasdaq Cloud option contract response must be an object or array.");

		return new NasdaqCloudOptionContractsResponse { Contracts = contracts.ToArray() };
	}

	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}
