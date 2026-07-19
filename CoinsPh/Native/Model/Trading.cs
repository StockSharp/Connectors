namespace StockSharp.CoinsPh.Native.Model;

sealed class CoinsPhOrderRequest
{
	public string Symbol { get; init; }
	public CoinsPhSides Side { get; init; }
	public CoinsPhOrderTypes Type { get; init; }
	public CoinsPhTimeInForces? TimeInForce { get; init; }
	public decimal? Quantity { get; init; }
	public decimal? QuoteOrderQuantity { get; init; }
	public decimal? Price { get; init; }
	public string ClientOrderId { get; init; }
	public decimal? StopPrice { get; init; }
}

sealed class CoinsPhOrderLookupRequest
{
	public long? OrderId { get; init; }
	public string ClientOrderId { get; init; }
}

sealed class CoinsPhCancelOrderRequest
{
	public long? OrderId { get; init; }
	public string ClientOrderId { get; init; }
}

sealed class CoinsPhCancelAllRequest
{
	public string Symbol { get; init; }
}

sealed class CoinsPhOpenOrdersRequest
{
	public string Symbol { get; init; }
}

sealed class CoinsPhOrderHistoryRequest
{
	public string Symbol { get; init; }
	public long? OrderId { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Limit { get; init; }
}

sealed class CoinsPhMyTradesRequest
{
	public string Symbol { get; init; }
	public long? OrderId { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public long? FromId { get; init; }
	public int Limit { get; init; }
}

sealed class CoinsPhCancelReplaceRequest
{
	public string Symbol { get; init; }
	public CoinsPhSides Side { get; init; }
	public CoinsPhOrderTypes Type { get; init; }
	public CoinsPhTimeInForces? TimeInForce { get; init; }
	public decimal? Quantity { get; init; }
	public decimal? QuoteOrderQuantity { get; init; }
	public decimal? Price { get; init; }
	public string ClientOrderId { get; init; }
	public long? CancelOrderId { get; init; }
	public string CancelClientOrderId { get; init; }
	public decimal? StopPrice { get; init; }
}

sealed class CoinsPhListenKeyRequest
{
	public string ListenKey { get; init; }
}

sealed class CoinsPhAccount
{
	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("canTrade")]
	public bool IsTradingEnabled { get; set; }

	[JsonProperty("balances")]
	public CoinsPhBalance[] Balances { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class CoinsPhBalance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("free")]
	public decimal Available { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }
}

sealed class CoinsPhOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("origQty")]
	public decimal OriginalQuantity { get; set; }

	[JsonProperty("executedQty")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("cummulativeQuoteQty")]
	public decimal CumulativeQuoteQuantity { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhOrderStatuses Status { get; set; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhTimeInForces TimeInForce { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhOrderTypes Type { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhSides Side { get; set; }

	[JsonProperty("stopPrice")]
	public decimal StopPrice { get; set; }

	[JsonProperty("origQuoteOrderQty")]
	public decimal OriginalQuoteOrderQuantity { get; set; }

	[JsonProperty("transactTime")]
	public long TransactionTime { get; set; }

	[JsonProperty("time")]
	public long CreatedTime { get; set; }

	[JsonProperty("updateTime")]
	public long UpdatedTime { get; set; }

	[JsonProperty("fills")]
	public CoinsPhFill[] Fills { get; set; }
}

sealed class CoinsPhFill
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("commission")]
	public decimal Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }
}

sealed class CoinsPhAccountTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public long TradeId { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("quoteQty")]
	public decimal QuoteQuantity { get; set; }

	[JsonProperty("commission")]
	public decimal Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("time")]
	public long Timestamp { get; set; }

	[JsonProperty("isBuyer")]
	public bool IsBuyer { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }
}

[JsonConverter(typeof(CoinsPhOrderLookupResultConverter))]
sealed class CoinsPhOrderLookupResult
{
	public CoinsPhOrder[] Orders { get; set; }
}

sealed class CoinsPhCancelReplaceResponse
{
	[JsonProperty("cancelResult")]
	public string CancelResult { get; set; }

	[JsonProperty("newOrderResult")]
	public string NewOrderResult { get; set; }

	[JsonProperty("cancelResponse")]
	public CoinsPhOrder CanceledOrder { get; set; }

	[JsonProperty("newOrderResponse")]
	public CoinsPhOrder NewOrder { get; set; }
}

sealed class CoinsPhOrderLookupResultConverter :
	JsonConverter<CoinsPhOrderLookupResult>
{
	public override bool CanWrite => false;

	public override CoinsPhOrderLookupResult ReadJson(JsonReader reader,
		Type objectType, CoinsPhOrderLookupResult existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.StartObject)
			return new()
			{
				Orders = [serializer.Deserialize<CoinsPhOrder>(reader)],
			};
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				"Coins.ph order lookup must return an object or array.");

		var orders = new List<CoinsPhOrder>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
		{
			if (reader.TokenType != JsonToken.StartObject)
				throw new JsonSerializationException(
					"Coins.ph order lookup returned an invalid array item.");
			orders.Add(serializer.Deserialize<CoinsPhOrder>(reader));
		}
		if (reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"Coins.ph order lookup returned an unterminated array.");
		return new() { Orders = [.. orders] };
	}

	public override void WriteJson(JsonWriter writer,
		CoinsPhOrderLookupResult value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}
