namespace StockSharp.Backpack.Native.Model;

[JsonConverter(typeof(BackpackBalancesConverter))]
sealed class BackpackBalances
{
	public BackpackBalance[] Entries { get; set; }
}

sealed class BackpackBalance
{
	public string Asset { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }

	[JsonProperty("staked")]
	public decimal Staked { get; set; }
}

sealed class BackpackPosition
{
	[JsonProperty("breakEvenPrice")]
	public decimal BreakEvenPrice { get; set; }

	[JsonProperty("entryPrice")]
	public decimal EntryPrice { get; set; }

	[JsonProperty("estLiquidationPrice")]
	public decimal? EstimatedLiquidationPrice { get; set; }

	[JsonProperty("imf")]
	public decimal InitialMarginFraction { get; set; }

	[JsonProperty("markPrice")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("mmf")]
	public decimal MaintenanceMarginFraction { get; set; }

	[JsonProperty("netCost")]
	public decimal NetCost { get; set; }

	[JsonProperty("netQuantity")]
	public decimal NetQuantity { get; set; }

	[JsonProperty("netExposureQuantity")]
	public decimal NetExposureQuantity { get; set; }

	[JsonProperty("netExposureNotional")]
	public decimal NetExposureNotional { get; set; }

	[JsonProperty("pnlRealized")]
	public decimal RealizedPnL { get; set; }

	[JsonProperty("pnlUnrealized")]
	public decimal UnrealizedPnL { get; set; }

	[JsonProperty("cumulativeFundingPayment")]
	public decimal CumulativeFundingPayment { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }
}

sealed class BackpackPositionsQuery : IBackpackParameters
{
	public string Symbol { get; init; }
	public BackpackMarketTypes? MarketType { get; init; }

	public BackpackParameter[] GetParameters()
	{
		var result = new List<BackpackParameter>();
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		if (MarketType is BackpackMarketTypes marketType)
			result.Add(new("marketType", marketType.ToWire()));
		return [.. result];
	}
}

sealed class BackpackOrderRequest : IBackpackParameters
{
	[JsonProperty("clientId")]
	public uint ClientId { get; init; }

	[JsonProperty("orderType")]
	public BackpackOrderTypes OrderType { get; init; }

	[JsonProperty("postOnly")]
	public bool? IsPostOnly { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("quoteQuantity")]
	public string QuoteQuantity { get; init; }

	[JsonProperty("reduceOnly")]
	public bool? IsReduceOnly { get; init; }

	[JsonProperty("selfTradePrevention")]
	public BackpackSelfTradePreventions SelfTradePrevention { get; init; } =
		BackpackSelfTradePreventions.RejectTaker;

	[JsonProperty("side")]
	public BackpackSides Side { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("timeInForce")]
	public BackpackTimeInForces? TimeInForce { get; init; }

	public BackpackParameter[] GetParameters()
	{
		var result = new List<BackpackParameter>
		{
			new("clientId", ClientId.ToString(CultureInfo.InvariantCulture)),
			new("orderType", OrderType == BackpackOrderTypes.Market ? "Market" : "Limit"),
			new("selfTradePrevention", SelfTradePrevention.ToWire()),
			new("side", Side == BackpackSides.Bid ? "Bid" : "Ask"),
			new("symbol", Symbol),
		};
		if (IsPostOnly is bool isPostOnly)
			result.Add(new("postOnly", isPostOnly.ToWire()));
		if (!Price.IsEmpty())
			result.Add(new("price", Price));
		if (!Quantity.IsEmpty())
			result.Add(new("quantity", Quantity));
		if (!QuoteQuantity.IsEmpty())
			result.Add(new("quoteQuantity", QuoteQuantity));
		if (IsReduceOnly is bool isReduceOnly)
			result.Add(new("reduceOnly", isReduceOnly.ToWire()));
		if (TimeInForce is BackpackTimeInForces timeInForce)
			result.Add(new("timeInForce", timeInForce.ToWire()));
		return [.. result];
	}
}

sealed class BackpackCancelOrderRequest : IBackpackParameters
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientId")]
	public uint? ClientId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	public BackpackParameter[] GetParameters()
	{
		var result = new List<BackpackParameter> { new("symbol", Symbol) };
		if (!OrderId.IsEmpty())
			result.Add(new("orderId", OrderId));
		else if (ClientId is uint clientId)
			result.Add(new("clientId", clientId.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class BackpackCancelOrdersRequest : IBackpackParameters
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	public BackpackParameter[] GetParameters() => [new("symbol", Symbol)];
}

sealed class BackpackOrder
{
	[JsonProperty("orderType")]
	public BackpackOrderTypes OrderType { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("clientId")]
	public uint? ClientId { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("executedQuantity")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("executedQuoteQuantity")]
	public decimal ExecutedQuoteQuantity { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("quoteQuantity")]
	public decimal? QuoteQuantity { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; set; }

	[JsonProperty("timeInForce")]
	public BackpackTimeInForces TimeInForce { get; set; }

	[JsonProperty("selfTradePrevention")]
	public BackpackSelfTradePreventions SelfTradePrevention { get; set; }

	[JsonProperty("side")]
	public BackpackSides Side { get; set; }

	[JsonProperty("status")]
	public BackpackOrderStatuses Status { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("triggeredAt")]
	public long? TriggeredAt { get; set; }

	[JsonProperty("relatedOrderId")]
	public string RelatedOrderId { get; set; }
}

sealed class BackpackOrdersQuery : IBackpackParameters
{
	public string Symbol { get; init; }
	public BackpackMarketTypes? MarketType { get; init; }

	public BackpackParameter[] GetParameters()
	{
		var result = new List<BackpackParameter>();
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		if (MarketType is BackpackMarketTypes marketType)
			result.Add(new("marketType", marketType.ToWire()));
		return [.. result];
	}
}

sealed class BackpackOrderHistoryQuery : IBackpackParameters
{
	public string OrderId { get; init; }
	public string Symbol { get; init; }
	public int Limit { get; init; }
	public int Offset { get; init; }

	public BackpackParameter[] GetParameters()
	{
		var result = new List<BackpackParameter>
		{
			new("limit", Limit.ToString(CultureInfo.InvariantCulture)),
			new("offset", Offset.ToString(CultureInfo.InvariantCulture)),
			new("sortDirection", "Desc"),
		};
		if (!OrderId.IsEmpty())
			result.Add(new("orderId", OrderId));
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		return [.. result];
	}
}

sealed class BackpackFillHistoryQuery : IBackpackParameters
{
	public string OrderId { get; init; }
	public string Symbol { get; init; }
	public long? From { get; init; }
	public long? To { get; init; }
	public int Limit { get; init; }
	public int Offset { get; init; }

	public BackpackParameter[] GetParameters()
	{
		var result = new List<BackpackParameter>
		{
			new("limit", Limit.ToString(CultureInfo.InvariantCulture)),
			new("offset", Offset.ToString(CultureInfo.InvariantCulture)),
			new("sortDirection", "Desc"),
		};
		if (!OrderId.IsEmpty())
			result.Add(new("orderId", OrderId));
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		if (From is long from)
			result.Add(new("from", from.ToString(CultureInfo.InvariantCulture)));
		if (To is long to)
			result.Add(new("to", to.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class BackpackFill
{
	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("feeSymbol")]
	public string FeeSymbol { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("side")]
	public BackpackSides Side { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("tradeId")]
	public long TradeId { get; set; }
}

sealed class BackpackBalancesConverter : JsonConverter<BackpackBalances>
{
	public override BackpackBalances ReadJson(JsonReader reader, Type objectType,
		BackpackBalances existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"Backpack Exchange balances must be an object.");
		var entries = new List<BackpackBalance>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"Backpack Exchange balance asset is missing.");
			var asset = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException(
					"Backpack Exchange balance value is missing.");
			var balance = serializer.Deserialize<BackpackBalance>(reader)
				?? throw new JsonSerializationException(
					"Backpack Exchange balance value is empty.");
			balance.Asset = asset;
			entries.Add(balance);
		}
		return new() { Entries = [.. entries] };
	}

	public override void WriteJson(JsonWriter writer, BackpackBalances value,
		JsonSerializer serializer)
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
