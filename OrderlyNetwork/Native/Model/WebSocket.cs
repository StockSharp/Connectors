namespace StockSharp.OrderlyNetwork.Native.Model;

sealed class OrderlyNetworkSocketCommand
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("topic")]
	public string Topic { get; init; }

	[JsonProperty("event")]
	public string Event { get; init; }
}

sealed class OrderlyNetworkSocketEvent
{
	[JsonProperty("event")]
	public string Event { get; init; }
}

sealed class OrderlyNetworkSocketAuthCommand
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("event")]
	public string Event { get; } = "auth";

	[JsonProperty("params")]
	public OrderlyNetworkSocketAuthParameters Parameters { get; init; }
}

sealed class OrderlyNetworkSocketAuthParameters
{
	[JsonProperty("orderly_key")]
	public string PublicKey { get; init; }

	[JsonProperty("sign")]
	public string Signature { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }
}

sealed class OrderlyNetworkSocketHeader
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class OrderlyNetworkSocketEnvelope<TData>
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class OrderlyNetworkSocketBbo
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ask")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskQuantity { get; set; }

	[JsonProperty("bid")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidQuantity { get; set; }
}

sealed class OrderlyNetworkSocketTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("amount")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("count")]
	public long? Trades { get; set; }
}

sealed class OrderlyNetworkSocketTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Quantity { get; set; }

	[JsonProperty("side")]
	public OrderlyNetworkSides Side { get; set; }
}

sealed class OrderlyNetworkSocketCandle
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Interval { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("amount")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("startTime")]
	public long StartTime { get; set; }

	[JsonProperty("endTime")]
	public long EndTime { get; set; }
}

sealed class OrderlyNetworkSocketDepth
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("prevTs")]
	public long PreviousTimestamp { get; set; }

	[JsonProperty("asks")]
	public OrderlyNetworkSocketLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public OrderlyNetworkSocketLevel[] Bids { get; set; }
}

[JsonConverter(typeof(OrderlyNetworkSocketLevelConverter))]
sealed class OrderlyNetworkSocketLevel
{
	public decimal Price { get; set; }
	public decimal Quantity { get; set; }
}

sealed class OrderlyNetworkSocketLevelConverter :
	JsonConverter<OrderlyNetworkSocketLevel>
{
	public override OrderlyNetworkSocketLevel ReadJson(JsonReader reader,
		Type objectType, OrderlyNetworkSocketLevel existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		var price = reader.ReadAsDecimal() ?? throw new JsonSerializationException(
			"Orderly Network price level has no price.");
		var quantity = reader.ReadAsDecimal() ?? throw new JsonSerializationException(
			"Orderly Network price level has no quantity.");
		if (!reader.Read())
			throw new JsonSerializationException(
				"Orderly Network price level is incomplete.");
		return new() { Price = price, Quantity = quantity };
	}

	public override void WriteJson(JsonWriter writer,
		OrderlyNetworkSocketLevel value, JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Price);
		writer.WriteValue(value.Quantity);
		writer.WriteEndArray();
	}
}

sealed class OrderlyNetworkSocketBalancesData
{
	[JsonProperty("balances")]
	public OrderlyNetworkSocketBalances Balances { get; set; }
}

[JsonConverter(typeof(OrderlyNetworkSocketBalancesConverter))]
sealed class OrderlyNetworkSocketBalances
{
	public OrderlyNetworkSocketBalance[] Entries { get; set; }
}

sealed class OrderlyNetworkSocketBalance
{
	public string Asset { get; set; }

	[JsonProperty("holding")]
	public decimal Holding { get; set; }

	[JsonProperty("frozen")]
	public decimal Frozen { get; set; }

	[JsonProperty("interest")]
	public decimal Interest { get; set; }

	[JsonProperty("pendingShortQty")]
	public decimal PendingShortQuantity { get; set; }

	[JsonProperty("pendingLongQty")]
	public decimal PendingLongQuantity { get; set; }

	[JsonProperty("isolatedMargin")]
	public decimal IsolatedMargin { get; set; }

	[JsonProperty("isolatedOrderFrozen")]
	public decimal IsolatedOrderFrozen { get; set; }

	[JsonProperty("markPrice")]
	public decimal? MarkPrice { get; set; }
}

sealed class OrderlyNetworkSocketBalancesConverter :
	JsonConverter<OrderlyNetworkSocketBalances>
{
	public override OrderlyNetworkSocketBalances ReadJson(JsonReader reader,
		Type objectType, OrderlyNetworkSocketBalances existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		var entries = new List<OrderlyNetworkSocketBalance>();
		while (reader.Read())
		{
			var token = reader.TokenType.ToString();
			if (token == "EndObject")
				break;
			if (token != "PropertyName")
				throw new JsonSerializationException(
					"Orderly Network balance asset is missing.");
			var asset = reader.Value?.ToString();
			if (!reader.Read())
				throw new JsonSerializationException(
					"Orderly Network balance value is missing.");
			var balance = serializer.Deserialize<OrderlyNetworkSocketBalance>(reader)
				?? throw new JsonSerializationException(
					"Orderly Network balance value is empty.");
			balance.Asset = asset;
			entries.Add(balance);
		}
		return new() { Entries = [.. entries] };
	}

	public override void WriteJson(JsonWriter writer,
		OrderlyNetworkSocketBalances value, JsonSerializer serializer)
	{
		writer.WriteStartObject();
		foreach (var balance in value?.Entries ?? [])
		{
			writer.WritePropertyName(balance.Asset);
			serializer.Serialize(writer, balance);
		}
		writer.WriteEndObject();
	}
}

sealed class OrderlyNetworkSocketPositionsData
{
	[JsonProperty("positions")]
	public OrderlyNetworkSocketPosition[] Positions { get; set; }
}

sealed class OrderlyNetworkSocketPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("positionQty")]
	public decimal Quantity { get; set; }

	[JsonProperty("averageOpenPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("unsettledPnl")]
	public decimal? UnrealizedPnL { get; set; }

	[JsonProperty("pnl24H")]
	public decimal? DailyPnL { get; set; }

	[JsonProperty("markPrice")]
	public decimal? MarkPrice { get; set; }

	[JsonProperty("estLiqPrice")]
	public decimal? LiquidationPrice { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("updated_time")]
	public long UpdatedTime { get; set; }

	[JsonProperty("leverage")]
	public int? Leverage { get; set; }

	[JsonProperty("marginMode")]
	public string MarginMode { get; set; }
}

sealed class OrderlyNetworkExecutionReport
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("type")]
	public OrderlyNetworkOrderTypes OrderType { get; set; }

	[JsonProperty("side")]
	public OrderlyNetworkSides Side { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	[JsonProperty("executedPrice")]
	public decimal ExecutedPrice { get; set; }

	[JsonProperty("executedQuantity")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("feeAsset")]
	public string FeeAsset { get; set; }

	[JsonProperty("totalExecutedQuantity")]
	public decimal TotalExecutedQuantity { get; set; }

	[JsonProperty("avgExecutedPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("status")]
	public OrderlyNetworkOrderStatuses Status { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("totalFee")]
	public decimal? TotalFee { get; set; }

	[JsonProperty("visibleQuantity")]
	public decimal? VisibleQuantity { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("maker")]
	public bool IsMaker { get; set; }

	[JsonProperty("match_id")]
	public string MatchId { get; set; }

	[JsonProperty("seq")]
	public long Sequence { get; set; }
}
