namespace StockSharp.CoinW.Native.Model;

sealed class CoinWSpotOrderRequest
{
	public string Symbol { get; init; }
	public string Side { get; init; }
	public string Amount { get; init; }
	public string Price { get; init; }
	public string Funds { get; init; }
	public bool IsMarket { get; init; }
	public string ClientOrderId { get; init; }
}

sealed class CoinWSpotCancelRequest
{
	public string OrderId { get; init; }
}

sealed class CoinWSpotOpenOrdersRequest
{
	public string Symbol { get; init; }
	public long? From { get; init; }
	public long? To { get; init; }
}

sealed class CoinWSpotTradeHistoryRequest
{
	public string Symbol { get; init; }
	public long? From { get; init; }
	public long? To { get; init; }
}

sealed class CoinWSpotUserTradesRequest
{
	public string Symbol { get; init; }
	public long? From { get; init; }
	public long? To { get; init; }
	public int Limit { get; init; }
}

sealed class CoinWSpotOrderResult
{
	[JsonProperty("orderNumber")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	private string CancelledOrderId
	{
		set => OrderId = OrderId.IsEmpty(value);
	}
}

sealed class CoinWSpotOrder
{
	[JsonProperty("orderNumber")]
	public string OrderId { get; set; }

	[JsonProperty("date")]
	public long Time { get; set; }

	[JsonProperty("currencyPair")]
	public string Symbol { get; set; }

	[JsonProperty("pair")]
	private string AlternateSymbol
	{
		set => Symbol = Symbol.IsEmpty(value);
	}

	[JsonProperty("startingAmount")]
	public string QuoteVolume { get; set; }

	[JsonProperty("total")]
	public string Volume { get; set; }

	[JsonProperty("type")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("prize")]
	private string LegacyPrice
	{
		set => Price = Price.IsEmpty(value);
	}

	[JsonProperty("success_count")]
	public string ExecutedVolume { get; set; }

	[JsonProperty("success_amount")]
	public string ExecutedValue { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("out_trade_no")]
	public string ClientOrderId { get; set; }
}

sealed class CoinWSpotTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("size")]
	public string Volume { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }
}

sealed class CoinWSpotTradesPage
{
	[JsonProperty("list")]
	public CoinWSpotTrade[] Items { get; set; }

	[JsonProperty("before")]
	public string Before { get; set; }

	[JsonProperty("after")]
	public string After { get; set; }
}

[JsonConverter(typeof(CoinWSpotBalancesConverter))]
sealed class CoinWSpotBalances
{
	public CoinWSpotBalance[] Items { get; set; }
}

sealed class CoinWSpotBalance
{
	public string Asset { get; set; }
	public string Available { get; set; }
	public string Held { get; set; }
}

sealed class CoinWSpotBalancesConverter : JsonConverter<CoinWSpotBalances>
{
	public override CoinWSpotBalances ReadJson(JsonReader reader, Type objectType,
		CoinWSpotBalances existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return new() { Items = [] };
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("CoinW spot balances must be an object.");

		var balances = new List<CoinWSpotBalance>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("CoinW spot balance asset is invalid.");
			var asset = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException("CoinW spot balance ended unexpectedly.");

			if (reader.TokenType is JsonToken.String or JsonToken.Integer or JsonToken.Float)
			{
				balances.Add(new()
				{
					Asset = asset,
					Available = Convert.ToString(reader.Value, CultureInfo.InvariantCulture),
				});
				continue;
			}

			if (reader.TokenType != JsonToken.StartObject)
				throw new JsonSerializationException("CoinW spot balance value is invalid.");
			var balance = new CoinWSpotBalance { Asset = asset };
			while (reader.Read() && reader.TokenType != JsonToken.EndObject)
			{
				if (reader.TokenType != JsonToken.PropertyName)
					throw new JsonSerializationException("CoinW spot balance property is invalid.");
				var property = (string)reader.Value;
				var value = CoinWJson.ReadWireString(reader, "spot balance");
				if (property.EqualsIgnoreCase("available"))
					balance.Available = value;
				else if (property.EqualsIgnoreCase("onOrders"))
					balance.Held = value;
			}
			balances.Add(balance);
		}
		return new() { Items = [.. balances] };
	}

	public override void WriteJson(JsonWriter writer, CoinWSpotBalances value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class CoinWFuturesOrderRequest
{
	[JsonProperty("instrument")]
	public string Instrument { get; init; }

	[JsonProperty("direction")]
	public string Direction { get; init; }

	[JsonProperty("leverage")]
	public int Leverage { get; init; }

	[JsonProperty("quantityUnit")]
	public int QuantityUnit { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("positionModel")]
	public int PositionModel { get; init; }

	[JsonProperty("positionType")]
	public string PositionType { get; init; }

	[JsonProperty("openPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; init; }

	[JsonProperty("stopLossPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string StopLossPrice { get; init; }

	[JsonProperty("stopProfitPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string TakeProfitPrice { get; init; }

	[JsonProperty("triggerPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string TriggerPrice { get; init; }

	[JsonProperty("triggerType", NullValueHandling = NullValueHandling.Ignore)]
	public int? TriggerType { get; init; }

	[JsonProperty("thirdOrderId")]
	public string ClientOrderId { get; init; }
}

sealed class CoinWFuturesCancelOrderRequest
{
	[JsonProperty("id")]
	public string OrderId { get; init; }
}

sealed class CoinWFuturesClosePositionRequest
{
	[JsonProperty("id")]
	public string PositionId { get; init; }

	[JsonProperty("positionType")]
	public string PositionType { get; init; }

	[JsonProperty("closeNum", NullValueHandling = NullValueHandling.Ignore)]
	public string Contracts { get; init; }

	[JsonProperty("closeRate", NullValueHandling = NullValueHandling.Ignore)]
	public string CloseRate { get; init; }

	[JsonProperty("orderPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; init; }
}

sealed class CoinWFuturesCloseAllRequest
{
	[JsonProperty("instrument")]
	public string Instrument { get; init; }
}

sealed class CoinWFuturesOpenOrdersRequest
{
	public string Instrument { get; init; }
	public string PositionType { get; init; }
	public int Page { get; init; } = 1;
	public int PageSize { get; init; } = 100;
}

sealed class CoinWFuturesHistoryRequest
{
	public string Instrument { get; init; }
	public int Page { get; init; } = 1;
	public int PageSize { get; init; } = 100;
	public string OriginType { get; init; }
}

sealed class CoinWFuturesOrdersPage
{
	[JsonProperty("rows")]
	public CoinWFuturesOrder[] Items { get; set; }

	[JsonProperty("total")]
	public int Total { get; set; }

	[JsonProperty("nextId")]
	public string NextId { get; set; }

	[JsonProperty("prevId")]
	public string PreviousId { get; set; }
}

sealed class CoinWFuturesOrder
{
	[JsonProperty("id")]
	public string OrderId { get; set; }

	[JsonProperty("instrument")]
	public string NativeSymbol { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("quantityUnit")]
	public int QuantityUnit { get; set; }

	[JsonProperty("baseSize")]
	public string ContractSize { get; set; }

	[JsonProperty("totalPiece")]
	public string TotalContracts { get; set; }

	[JsonProperty("currentPiece")]
	public string CurrentContracts { get; set; }

	[JsonProperty("tradePiece")]
	public string ExecutedContracts { get; set; }

	[JsonProperty("cancelPiece")]
	public string CancelledContracts { get; set; }

	[JsonProperty("orderPrice")]
	public string Price { get; set; }

	[JsonProperty("avgPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("originalType")]
	public string OriginalType { get; set; }

	[JsonProperty("posType")]
	public string PositionType { get; set; }

	[JsonProperty("positionModel")]
	public int PositionModel { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("margin")]
	public string Margin { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("createdDate")]
	public long CreatedTime { get; set; }

	[JsonProperty("updatedDate")]
	public long UpdatedTime { get; set; }

	[JsonProperty("thirdOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("triggerType")]
	public int? TriggerType { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("completeUsdt")]
	public string ExecutedValue { get; set; }
}

sealed class CoinWFuturesAssets
{
	[JsonProperty("availableMargin")]
	public string AvailableMargin { get; set; }

	[JsonProperty("availableUsdt")]
	public string Available { get; set; }

	[JsonProperty("alMargin")]
	public string Margin { get; set; }

	[JsonProperty("alFreeze")]
	public string Frozen { get; set; }

	[JsonProperty("almightyGold")]
	public string Coupon { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }
}

sealed class CoinWFuturesPosition
{
	[JsonProperty("id")]
	public string PositionId { get; set; }

	[JsonProperty("instrument")]
	public string NativeSymbol { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("baseSize")]
	public string ContractSize { get; set; }

	[JsonProperty("currentPiece")]
	public string CurrentContracts { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("quantityUnit")]
	public int QuantityUnit { get; set; }

	[JsonProperty("openPrice")]
	public string OpenPrice { get; set; }

	[JsonProperty("indexPrice")]
	public string IndexPrice { get; set; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("profitUnreal")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("profitReal")]
	public string RealizedPnl { get; set; }

	[JsonProperty("positionMargin")]
	public string Margin { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("positionModel")]
	public int PositionModel { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("createdDate")]
	public long CreatedTime { get; set; }

	[JsonProperty("updatedDate")]
	public long UpdatedTime { get; set; }
}
