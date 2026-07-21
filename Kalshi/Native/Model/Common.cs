namespace StockSharp.Kalshi.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum KalshiMarketTypes
{
	[EnumMember(Value = "binary")]
	Binary,

	[EnumMember(Value = "scalar")]
	Scalar,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KalshiMarketStatuses
{
	[EnumMember(Value = "initialized")]
	Initialized,

	[EnumMember(Value = "inactive")]
	Inactive,

	[EnumMember(Value = "active")]
	Active,

	[EnumMember(Value = "closed")]
	Closed,

	[EnumMember(Value = "determined")]
	Determined,

	[EnumMember(Value = "disputed")]
	Disputed,

	[EnumMember(Value = "amended")]
	Amended,

	[EnumMember(Value = "finalized")]
	Finalized,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KalshiMarketSides
{
	[EnumMember(Value = "yes")]
	Yes,

	[EnumMember(Value = "no")]
	No,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KalshiBookSides
{
	[EnumMember(Value = "bid")]
	Bid,

	[EnumMember(Value = "ask")]
	Ask,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KalshiOrderStatuses
{
	[EnumMember(Value = "resting")]
	Resting,

	[EnumMember(Value = "canceled")]
	Canceled,

	[EnumMember(Value = "executed")]
	Executed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KalshiTimeInForces
{
	[EnumMember(Value = "fill_or_kill")]
	FillOrKill,

	[EnumMember(Value = "good_till_canceled")]
	GoodTillCanceled,

	[EnumMember(Value = "immediate_or_cancel")]
	ImmediateOrCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KalshiSocketChannels
{
	[EnumMember(Value = "orderbook_delta")]
	OrderBookDelta,

	[EnumMember(Value = "ticker")]
	Ticker,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "fill")]
	Fill,

	[EnumMember(Value = "market_positions")]
	MarketPositions,

	[EnumMember(Value = "user_orders")]
	UserOrders,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KalshiSocketCommands
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KalshiSocketEventTypes
{
	[EnumMember(Value = "subscribed")]
	Subscribed,

	[EnumMember(Value = "unsubscribed")]
	Unsubscribed,

	[EnumMember(Value = "ok")]
	Ok,

	[EnumMember(Value = "error")]
	Error,

	[EnumMember(Value = "orderbook_snapshot")]
	OrderBookSnapshot,

	[EnumMember(Value = "orderbook_delta")]
	OrderBookDelta,

	[EnumMember(Value = "ticker")]
	Ticker,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "fill")]
	Fill,

	[EnumMember(Value = "market_position")]
	MarketPosition,

	[EnumMember(Value = "user_order")]
	UserOrder,
}

sealed class KalshiApiError
{
	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("details")]
	public string Details { get; init; }

	[JsonProperty("service")]
	public string Service { get; init; }
}

sealed class KalshiPriceRange
{
	[JsonProperty("start")]
	public string Start { get; init; }

	[JsonProperty("end")]
	public string End { get; init; }

	[JsonProperty("step")]
	public string Step { get; init; }
}

sealed class KalshiMarket
{
	[JsonProperty("ticker")]
	public string Ticker { get; init; }

	[JsonProperty("event_ticker")]
	public string EventTicker { get; init; }

	[JsonProperty("market_type")]
	public KalshiMarketTypes MarketType { get; init; }

	[JsonProperty("title")]
	public string Title { get; init; }

	[JsonProperty("yes_sub_title")]
	public string YesSubTitle { get; init; }

	[JsonProperty("no_sub_title")]
	public string NoSubTitle { get; init; }

	[JsonProperty("open_time")]
	public string OpenTime { get; init; }

	[JsonProperty("close_time")]
	public string CloseTime { get; init; }

	[JsonProperty("latest_expiration_time")]
	public string LatestExpirationTime { get; init; }

	[JsonProperty("status")]
	public KalshiMarketStatuses Status { get; set; }

	[JsonProperty("yes_bid_dollars")]
	public string YesBid { get; set; }

	[JsonProperty("yes_bid_size_fp")]
	public string YesBidSize { get; set; }

	[JsonProperty("yes_ask_dollars")]
	public string YesAsk { get; set; }

	[JsonProperty("yes_ask_size_fp")]
	public string YesAskSize { get; set; }

	[JsonProperty("last_price_dollars")]
	public string LastPrice { get; set; }

	[JsonProperty("volume_fp")]
	public string Volume { get; set; }

	[JsonProperty("volume_24h_fp")]
	public string Volume24Hours { get; set; }

	[JsonProperty("open_interest_fp")]
	public string OpenInterest { get; set; }

	[JsonProperty("notional_value_dollars")]
	public string NotionalValue { get; init; }

	[JsonProperty("result")]
	public string Result { get; init; }

	[JsonProperty("rules_primary")]
	public string RulesPrimary { get; init; }

	[JsonProperty("rules_secondary")]
	public string RulesSecondary { get; init; }

	[JsonProperty("price_level_structure")]
	public string PriceLevelStructure { get; init; }

	[JsonProperty("price_ranges")]
	public KalshiPriceRange[] PriceRanges { get; init; }
}

sealed class KalshiMarketsPage
{
	[JsonProperty("markets")]
	public KalshiMarket[] Markets { get; init; }

	[JsonProperty("cursor")]
	public string Cursor { get; init; }
}

sealed class KalshiMarketResponse
{
	[JsonProperty("market")]
	public KalshiMarket Market { get; init; }
}

[JsonConverter(typeof(KalshiPriceLevelConverter))]
sealed class KalshiPriceLevel
{
	public string Price { get; init; }
	public string Volume { get; init; }
}

sealed class KalshiPriceLevelConverter : JsonConverter
{
	public override bool CanConvert(Type objectType)
		=> objectType == typeof(KalshiPriceLevel);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = serializer;
		if (reader.TokenType == JsonToken.None && !reader.Read())
			throw new JsonSerializationException("Unexpected end of a Kalshi price level.");
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("A Kalshi price level must be an array.");
		if (!reader.Read() || reader.TokenType is not (JsonToken.String or
			JsonToken.Integer or JsonToken.Float))
			throw new JsonSerializationException("A Kalshi price level has no price.");
		var price = Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType is not (JsonToken.String or
			JsonToken.Integer or JsonToken.Float))
			throw new JsonSerializationException("A Kalshi price level has no volume.");
		var volume = Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("A Kalshi price level must contain exactly two values.");
		return new KalshiPriceLevel { Price = price, Volume = volume };
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		_ = serializer;
		var level = (KalshiPriceLevel)value;
		writer.WriteStartArray();
		writer.WriteValue(level.Price);
		writer.WriteValue(level.Volume);
		writer.WriteEndArray();
	}
}

sealed class KalshiOrderBook
{
	[JsonProperty("yes_dollars")]
	public KalshiPriceLevel[] Yes { get; init; }

	[JsonProperty("no_dollars")]
	public KalshiPriceLevel[] No { get; init; }
}

sealed class KalshiOrderBookResponse
{
	[JsonProperty("orderbook_fp")]
	public KalshiOrderBook OrderBook { get; init; }
}

sealed class KalshiBookState
{
	private static readonly IComparer<decimal> _descending =
		Comparer<decimal>.Create(static (left, right) => right.CompareTo(left));

	public SortedDictionary<decimal, decimal> Bids { get; } = new(_descending);
	public SortedDictionary<decimal, decimal> Asks { get; } = [];
	public DateTime Time { get; private set; }
	public long Sequence { get; private set; }

	public void ApplyRest(KalshiOrderBook book, DateTime time)
	{
		ArgumentNullException.ThrowIfNull(book);
		Bids.Clear();
		Asks.Clear();
		ApplyLevels(Bids, book.Yes, false);
		ApplyLevels(Asks, book.No, true);
		Time = time.EnsureUtc();
		Sequence = 0;
	}

	public void ApplySocket(KalshiPriceLevel[] yes, KalshiPriceLevel[] no,
		DateTime time, long sequence)
	{
		Bids.Clear();
		Asks.Clear();
		ApplyLevels(Bids, yes, false);
		ApplyLevels(Asks, no, false);
		Time = time.EnsureUtc();
		Sequence = sequence;
	}

	public bool ApplyDelta(KalshiMarketSides side, string priceValue,
		string deltaValue, DateTime time, long sequence)
	{
		if (Sequence > 0 && sequence > 0 && sequence != Sequence + 1)
			return false;
		var price = priceValue.ParseKalshiDecimal("order-book price");
		var delta = deltaValue.ParseKalshiDecimal("order-book delta");
		if (price is < 0 or > 1)
			throw new InvalidDataException("Kalshi returned an order-book price outside [0, 1].");
		var levels = side == KalshiMarketSides.Yes ? Bids : Asks;
		levels.TryGetValue(price, out var current);
		var updated = current + delta;
		if (updated > 0)
			levels[price] = updated;
		else
			levels.Remove(price);
		Time = time.EnsureUtc();
		Sequence = sequence;
		return true;
	}

	private static void ApplyLevels(SortedDictionary<decimal, decimal> target,
		KalshiPriceLevel[] source, bool isNoPrice)
	{
		foreach (var level in source ?? [])
		{
			if (level is null)
				continue;
			var price = level.Price.ParseKalshiDecimal("order-book price");
			var volume = level.Volume.ParseKalshiDecimal("order-book volume");
			if (isNoPrice)
				price = 1m - price;
			if (price is >= 0 and <= 1 && volume > 0)
				target[price] = volume;
		}
	}
}
