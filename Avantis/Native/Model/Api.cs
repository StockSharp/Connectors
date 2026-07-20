namespace StockSharp.Avantis.Native.Model;

sealed class AvantisMarketsResponse
{
	[JsonProperty("data")]
	public AvantisMarketsData Data { get; set; }
}

sealed class AvantisMarketsData
{
	[JsonProperty("dataVersion")]
	public string DataVersion { get; set; }

	[JsonProperty("pairCount")]
	public int PairCount { get; set; }

	[JsonProperty("maxTradesPerPair")]
	public int MaximumTradesPerPair { get; set; }

	[JsonProperty("pairInfos")]
	[JsonConverter(typeof(AvantisPairCollectionConverter))]
	public AvantisPair[] Pairs { get; set; }
}

sealed class AvantisPair
{
	[JsonProperty("index")]
	public int Index { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }

	[JsonProperty("to")]
	public string To { get; set; }

	[JsonProperty("isPairListed")]
	public bool IsPairListed { get; set; }

	[JsonProperty("feed")]
	public AvantisPairFeed Feed { get; set; }

	[JsonProperty("leverages")]
	public AvantisLeverages Leverages { get; set; }

	[JsonProperty("openInterest")]
	public AvantisOpenInterest OpenInterest { get; set; }

	[JsonProperty("minLevPosUSDC")]
	public decimal MinimumPositionValue { get; set; }

	[JsonProperty("pairMinLevPosUSDC")]
	public decimal PairMinimumPositionValue { get; set; }

	[JsonProperty("pairOI")]
	public decimal PairOpenInterest { get; set; }

	[JsonProperty("lazerFeed")]
	public AvantisLazerFeed LazerFeed { get; set; }
}

sealed class AvantisPairFeed
{
	[JsonProperty("feedId")]
	public string FeedId { get; set; }

	[JsonProperty("attributes")]
	public AvantisFeedAttributes Attributes { get; set; }
}

sealed class AvantisFeedAttributes
{
	[JsonProperty("asset_type")]
	public string AssetType { get; set; }

	[JsonProperty("isOpen")]
	public bool IsOpen { get; set; }

	[JsonProperty("nextOpen")]
	public long NextOpen { get; set; }

	[JsonProperty("nextClose")]
	public long NextClose { get; set; }
}

sealed class AvantisLeverages
{
	[JsonProperty("minLeverage")]
	public decimal Minimum { get; set; }

	[JsonProperty("maxLeverage")]
	public decimal Maximum { get; set; }

	[JsonProperty("pnlMinLeverage")]
	public decimal MinimumPnl { get; set; }

	[JsonProperty("pnlMaxLeverage")]
	public decimal MaximumPnl { get; set; }
}

sealed class AvantisOpenInterest
{
	[JsonProperty("long")]
	public decimal Long { get; set; }

	[JsonProperty("short")]
	public decimal Short { get; set; }
}

sealed class AvantisLazerFeed
{
	[JsonProperty("feedId")]
	public int FeedId { get; set; }

	[JsonProperty("exponent")]
	public int Exponent { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }
}

sealed class AvantisFeedPriceResponse
{
	[JsonProperty("core")]
	public AvantisFeedPrice Core { get; set; }

	[JsonProperty("pro")]
	public AvantisFeedPrice Pro { get; set; }
}

sealed class AvantisFeedPrice
{
	[JsonProperty("priceUpdateData")]
	public string PriceUpdateData { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("publishTimestampMs")]
	public long PublishTimestampMilliseconds { get; set; }
}

sealed class AvantisLazerPriceResponse
{
	[JsonProperty("timestampUs")]
	public string TimestampMicroseconds { get; set; }

	[JsonProperty("priceFeeds")]
	public AvantisLazerPrice[] Prices { get; set; }
}

sealed class AvantisLazerPrice
{
	[JsonProperty("priceFeedId")]
	public int FeedId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("bestBidPrice")]
	public string Bid { get; set; }

	[JsonProperty("bestAskPrice")]
	public string Ask { get; set; }

	[JsonProperty("confidence")]
	public string Confidence { get; set; }

	[JsonProperty("exponent")]
	public int Exponent { get; set; }
}

sealed class AvantisHermesSubscribeRequest
{
	[JsonProperty("type")]
	public string Type { get; set; } = "subscribe";

	[JsonProperty("ids")]
	public string[] Ids { get; set; }
}

sealed class AvantisHermesEnvelope
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price_feed")]
	public AvantisHermesFeed PriceFeed { get; set; }
}

sealed class AvantisHermesFeed
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("price")]
	public AvantisHermesPrice Price { get; set; }
}

sealed class AvantisHermesPrice
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("conf")]
	public string Confidence { get; set; }

	[JsonProperty("expo")]
	public int Exponent { get; set; }

	[JsonProperty("publish_time")]
	public long PublishTime { get; set; }
}

sealed class AvantisUserData
{
	[JsonProperty("positions")]
	public AvantisPosition[] Positions { get; set; }

	[JsonProperty("limitOrders")]
	public AvantisLimitOrder[] LimitOrders { get; set; }
}

sealed class AvantisPosition
{
	[JsonProperty("trader")]
	public string Trader { get; set; }

	[JsonProperty("pairIndex")]
	public int PairIndex { get; set; }

	[JsonProperty("index")]
	public int Index { get; set; }

	[JsonProperty("buy")]
	public bool IsBuy { get; set; }

	[JsonProperty("collateral")]
	public string Collateral { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("openPrice")]
	public string OpenPrice { get; set; }

	[JsonProperty("sl")]
	public string StopLoss { get; set; }

	[JsonProperty("tp")]
	public string TakeProfit { get; set; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("rolloverFee")]
	public string RolloverFee { get; set; }

	[JsonProperty("openedAt")]
	public long OpenedAt { get; set; }

	[JsonProperty("isPnl")]
	public bool IsPnl { get; set; }
}

sealed class AvantisLimitOrder
{
	[JsonProperty("trader")]
	public string Trader { get; set; }

	[JsonProperty("pairIndex")]
	public int PairIndex { get; set; }

	[JsonProperty("index")]
	public int Index { get; set; }

	[JsonProperty("buy")]
	public bool IsBuy { get; set; }

	[JsonProperty("collateral")]
	public string Collateral { get; set; }

	[JsonProperty("positionSize")]
	public string PositionSize { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("sl")]
	public string StopLoss { get; set; }

	[JsonProperty("tp")]
	public string TakeProfit { get; set; }

	[JsonProperty("slippageP")]
	public string Slippage { get; set; }

	[JsonProperty("block")]
	public long Block { get; set; }

	[JsonProperty("executionFee")]
	public string ExecutionFee { get; set; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("limitOrderType")]
	public AvantisOpenOrderTypes LimitOrderType { get; set; }
}

sealed class AvantisPairCollectionConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(AvantisPair[]);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return Array.Empty<AvantisPair>();
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"Avantis pair collection must be a JSON object.");

		var pairs = new List<AvantisPair>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"Avantis pair collection contains an invalid key.");
			var key = reader.Value?.ToString();
			if (!reader.Read())
				throw new JsonSerializationException(
					"Avantis pair collection is truncated.");
			var pair = serializer.Deserialize<AvantisPair>(reader);
			if (pair is null)
				continue;
			if (int.TryParse(key, NumberStyles.None,
				CultureInfo.InvariantCulture, out var index))
				pair.Index = index;
			pairs.Add(pair);
		}
		return pairs.ToArray();
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}
