namespace StockSharp.ApexOmni.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniConfiguration
{
	[JsonProperty("contractConfig", Required = Required.Always)]
	public ApexOmniContractConfiguration Contracts { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniContractConfiguration
{
	[JsonProperty("assets", Required = Required.Always)]
	public ApexOmniAsset[] Assets { get; set; }

	[JsonProperty("perpetualContract")]
	public ApexOmniContract[] Perpetuals { get; set; }

	[JsonProperty("prelaunchContract")]
	public ApexOmniContract[] Prelaunch { get; set; }

	[JsonProperty("predictionContract")]
	public ApexOmniContract[] Predictions { get; set; }

	[JsonProperty("stockContract")]
	public ApexOmniContract[] Stocks { get; set; }

	public ApexOmniContract[] GetInstruments()
	{
		var result = new List<ApexOmniContract>();
		Add(Perpetuals, ApexOmniInstrumentGroups.Perpetual, result);
		Add(Prelaunch, ApexOmniInstrumentGroups.Prelaunch, result);
		Add(Predictions, ApexOmniInstrumentGroups.Prediction, result);
		Add(Stocks, ApexOmniInstrumentGroups.Stock, result);
		return [.. result];
	}

	private static void Add(ApexOmniContract[] source,
		ApexOmniInstrumentGroups group, List<ApexOmniContract> target)
	{
		foreach (var instrument in source ?? [])
		{
			if (instrument is null)
				continue;
			instrument.Group = group;
			target.Add(instrument);
		}
	}
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniAsset
{
	[JsonProperty("tokenId")]
	public string TokenId { get; set; }

	[JsonProperty("token", Required = Required.Always)]
	public string Token { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("decimals", Required = Required.Always)]
	public int Decimals { get; set; }

	[JsonProperty("showStep")]
	public string ShowStep { get; set; }

	[JsonProperty("enableCollateral")]
	public bool IsCollateralEnabled { get; set; }

	[JsonProperty("enableCrossCollateral")]
	public bool IsCrossCollateralEnabled { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniContract
{
	[JsonProperty("crossSymbolName", Required = Required.Always)]
	public string CrossSymbolName { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("symbolDisplayName")]
	public string DisplayName { get; set; }

	[JsonProperty("baseTokenId", Required = Required.Always)]
	public string BaseTokenId { get; set; }

	[JsonProperty("settleAssetId", Required = Required.Always)]
	public string SettleAssetId { get; set; }

	[JsonProperty("tokenName")]
	public string TokenName { get; set; }

	[JsonProperty("stepSize", Required = Required.Always)]
	public string StepSize { get; set; }

	[JsonProperty("tickSize", Required = Required.Always)]
	public string TickSize { get; set; }

	[JsonProperty("minOrderSize", Required = Required.Always)]
	public string MinOrderSize { get; set; }

	[JsonProperty("maxOrderSize")]
	public string MaxOrderSize { get; set; }

	[JsonProperty("maxPositionSize")]
	public string MaxPositionSize { get; set; }

	[JsonProperty("l2PairId", Required = Required.Always)]
	public string L2PairId { get; set; }

	[JsonProperty("enableDisplay")]
	public bool IsDisplayEnabled { get; set; }

	[JsonProperty("enableOpenPosition")]
	public bool IsOpenPositionEnabled { get; set; }

	[JsonProperty("enableTrade")]
	public bool IsTradingEnabled { get; set; }

	[JsonProperty("isPrelaunch")]
	public bool IsPrelaunch { get; set; }

	[JsonIgnore]
	public ApexOmniInstrumentGroups Group { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("lastPrice")]
	public string LastPrice { get; set; }

	[JsonProperty("price24hPcnt")]
	public string PriceChange24h { get; set; }

	[JsonProperty("highPrice24h")]
	public string HighPrice24h { get; set; }

	[JsonProperty("lowPrice24h")]
	public string LowPrice24h { get; set; }

	[JsonProperty("turnover24h")]
	public string Turnover24h { get; set; }

	[JsonProperty("volume24h")]
	public string Volume24h { get; set; }

	[JsonProperty("fundingRate")]
	public string FundingRate { get; set; }

	[JsonProperty("predictedFundingRate")]
	public string PredictedFundingRate { get; set; }

	[JsonProperty("nextFundingTime")]
	public string NextFundingTime { get; set; }

	[JsonProperty("openInterest")]
	public string OpenInterest { get; set; }

	[JsonProperty("oraclePrice")]
	public string OraclePrice { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("indexPrice")]
	public string IndexPrice { get; set; }

	[JsonProperty("tradeCount")]
	public long? TradeCount { get; set; }
}

[JsonConverter(typeof(ApexOmniBookLevelConverter))]
sealed class ApexOmniBookLevel
{
	public string Price { get; init; }
	public string Size { get; init; }
}

sealed class ApexOmniBookLevelConverter : JsonConverter<ApexOmniBookLevel>
{
	public override ApexOmniBookLevel ReadJson(JsonReader reader,
		Type objectType, ApexOmniBookLevel existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				"ApeX Omni book level must be an array.");
		if (!reader.Read() || reader.TokenType != JsonToken.String)
			throw new JsonSerializationException(
				"ApeX Omni book level has no price.");
		var price = (string)reader.Value;
		if (!reader.Read() || reader.TokenType != JsonToken.String)
			throw new JsonSerializationException(
				"ApeX Omni book level has no size.");
		var size = (string)reader.Value;
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"ApeX Omni book level has unexpected values.");
		return new() { Price = price, Size = size };
	}

	public override void WriteJson(JsonWriter writer, ApexOmniBookLevel value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniOrderBook
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("b")]
	public ApexOmniBookLevel[] Bids { get; set; }

	[JsonProperty("a")]
	public ApexOmniBookLevel[] Asks { get; set; }

	[JsonProperty("u")]
	public long UpdateId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniTrade
{
	[JsonProperty("i")]
	public string Id { get; set; }

	[JsonProperty("p", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("S", Required = Required.Always)]
	public ApexOmniNativeSides Side { get; set; }

	[JsonProperty("v", Required = Required.Always)]
	public string Size { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("T", Required = Required.Always)]
	public long Time { get; set; }

}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniCandle
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("i")]
	public string Interval { get; set; }

	[JsonProperty("t")]
	public long Start { get; set; }

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

	[JsonProperty("tr")]
	public string Turnover { get; set; }

	[JsonProperty("start")]
	private long LongStart { set => Start = value; }

	[JsonProperty("symbol")]
	private string LongSymbol { set => Symbol = value; }

	[JsonProperty("interval")]
	private string LongInterval { set => Interval = value; }

	[JsonProperty("open")]
	private string LongOpen { set => Open = value; }

	[JsonProperty("high")]
	private string LongHigh { set => High = value; }

	[JsonProperty("low")]
	private string LongLow { set => Low = value; }

	[JsonProperty("close")]
	private string LongClose { set => Close = value; }

	[JsonProperty("volume")]
	private string LongVolume { set => Volume = value; }

	[JsonProperty("turnover")]
	private string LongTurnover { set => Turnover = value; }
}

[JsonConverter(typeof(ApexOmniKlineDataConverter))]
sealed class ApexOmniKlineData
{
	public ApexOmniCandle[] Candles { get; init; }
}

sealed class ApexOmniKlineDataConverter : JsonConverter<ApexOmniKlineData>
{
	public override ApexOmniKlineData ReadJson(JsonReader reader,
		Type objectType, ApexOmniKlineData existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType == JsonToken.StartArray)
			return new()
			{
				Candles = serializer.Deserialize<ApexOmniCandle[]>(reader) ?? [],
			};
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"ApeX Omni kline data must be an object or array.");
		var candles = new List<ApexOmniCandle>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName || !reader.Read())
				throw new JsonSerializationException(
					"ApeX Omni kline data is malformed.");
			if (reader.TokenType != JsonToken.StartArray)
				throw new JsonSerializationException(
					"ApeX Omni symbol klines must be an array.");
			candles.AddRange(serializer.Deserialize<ApexOmniCandle[]>(reader) ?? []);
		}
		return new() { Candles = [.. candles] };
	}

	public override void WriteJson(JsonWriter writer, ApexOmniKlineData value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniWebSocketCandle
{
	[JsonProperty("start", Required = Required.Always)]
	public long Start { get; set; }

	[JsonProperty("end")]
	public long End { get; set; }

	[JsonProperty("interval", Required = Required.Always)]
	public string Interval { get; set; }

	[JsonProperty("open", Required = Required.Always)]
	public string Open { get; set; }

	[JsonProperty("close", Required = Required.Always)]
	public string Close { get; set; }

	[JsonProperty("high", Required = Required.Always)]
	public string High { get; set; }

	[JsonProperty("low", Required = Required.Always)]
	public string Low { get; set; }

	[JsonProperty("volume", Required = Required.Always)]
	public string Volume { get; set; }

	[JsonProperty("turnover")]
	public string Turnover { get; set; }

	[JsonProperty("confirm")]
	public bool IsConfirmed { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }
}
