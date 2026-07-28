namespace StockSharp.BigOne.Native.Model;

sealed class BigOneSpotAccount
{
	[JsonProperty("asset_symbol")]
	public string AssetSymbol { get; set; }

	[JsonProperty("asset")]
	private string StreamAsset
	{
		set
		{
			if (!value.IsEmpty())
				AssetSymbol = value;
		}
	}

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("locked_balance")]
	public decimal LockedBalance { get; set; }

	[JsonProperty("lockedBalance")]
	private decimal StreamLockedBalance
	{
		set => LockedBalance = value;
	}

	public BigOneBalance ToBalance()
		=> new()
		{
			Currency = AssetSymbol,
			Available = Balance,
			Locked = LockedBalance,
		};
}

sealed class BigOneContractAccount
{
	[JsonProperty("cash")]
	public BigOneContractCash Cash { get; set; }

	[JsonProperty("positions")]
	public BigOneContractPosition[] Positions { get; set; }
}

sealed class BigOneContractCash
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balances")]
	public decimal Balances { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("positionMargin")]
	public decimal PositionMargin { get; set; }

	[JsonProperty("orderMargin")]
	public decimal OrderMargin { get; set; }

	[JsonProperty("unrealizedPnl")]
	public decimal UnrealizedPnl { get; set; }

	public BigOneBalance ToBalance()
		=> new()
		{
			Currency = Currency,
			Available = Available,
			Locked = (Balances - Available).Max(0),
			UnrealizedPnl = UnrealizedPnl,
		};
}

sealed class BigOneContractPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("entryPrice")]
	public decimal? EntryPrice { get; set; }

	[JsonProperty("markPrice")]
	public decimal? MarkPrice { get; set; }

	[JsonProperty("liquidatePrice")]
	public decimal? LiquidationPrice { get; set; }

	[JsonProperty("unrealizedPnl")]
	public decimal? UnrealizedPnl { get; set; }

	[JsonProperty("leverage")]
	public decimal? Leverage { get; set; }

	[JsonProperty("margin")]
	public decimal? Margin { get; set; }
}

sealed class BigOneBalance
{
	public string Currency { get; set; }
	public decimal Available { get; set; }
	public decimal Locked { get; set; }
	public decimal? UnrealizedPnl { get; set; }
	public decimal Amount => Available + Locked;
}

sealed class BigOneSpotOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("asset_pair_name")]
	public string Market { get; set; }

	[JsonProperty("market")]
	private string StreamMarket
	{
		set
		{
			if (!value.IsEmpty())
				Market = value;
		}
	}

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("filled_amount")]
	public decimal FilledAmount { get; set; }

	[JsonProperty("filledAmount")]
	private decimal StreamFilledAmount
	{
		set => FilledAmount = value;
	}

	[JsonProperty("avg_deal_price")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("avgDealPrice")]
	private decimal? StreamAveragePrice
	{
		set => AveragePrice = value;
	}

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("created_at")]
	public DateTime? CreatedAt { get; set; }

	[JsonProperty("createdAt")]
	private DateTime? StreamCreatedAt
	{
		set => CreatedAt = value;
	}

	[JsonProperty("updated_at")]
	public DateTime? UpdatedAt { get; set; }

	[JsonProperty("updatedAt")]
	private DateTime? StreamUpdatedAt
	{
		set => UpdatedAt = value;
	}

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("stopPrice")]
	private decimal? StreamStopPrice
	{
		set => StopPrice = value;
	}

	[JsonProperty("operator")]
	public string Operator { get; set; }

	[JsonProperty("immediate_or_cancel")]
	public bool ImmediateOrCancel { get; set; }

	[JsonProperty("post_only")]
	public bool PostOnly { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("clientOrderId")]
	private string StreamClientOrderId
	{
		set
		{
			if (!value.IsEmpty())
				ClientOrderId = value;
		}
	}

	public BigOneOrder ToOrder()
		=> new()
		{
			Id = Id,
			Pair = Market,
			Action = Side,
			Type = Type,
			Status = State,
			Price = Price,
			OriginalAmount = Amount,
			ExecutedAmount = FilledAmount,
			StopPrice = StopPrice,
			TimeInForce = PostOnly
				? "POST_ONLY"
				: ImmediateOrCancel ? "IOC" : "GTC",
			ClientOrderId = ClientOrderId,
			CreatedTimestamp = CreatedAt?.ToUtc()
				.ToBigOneMilliseconds() ?? 0,
			UpdatedTimestamp = UpdatedAt?.ToUtc()
				.ToBigOneMilliseconds() ?? 0,
			Kind = BigOneMarketKind.Spot,
		};
}

sealed class BigOneContractOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("filled")]
	public decimal Filled { get; set; }

	[JsonProperty("avgPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("reduceOnly")]
	public bool ReduceOnly { get; set; }

	[JsonProperty("conditional")]
	public BigOneContractConditional Conditional { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	public BigOneOrder ToOrder()
		=> new()
		{
			Id = Id,
			Pair = Symbol,
			Action = Side,
			Type = Type,
			Status = Status,
			Price = Price,
			OriginalAmount = Size,
			ExecutedAmount = Filled,
			StopPrice = Conditional?.Price,
			TimeInForce = Type,
			CreatedTimestamp = BigOneExtensions.NormalizeTimestamp(
				Timestamp),
			UpdatedTimestamp = BigOneExtensions.NormalizeTimestamp(
				Timestamp),
			Kind = BigOneMarketKind.Contract,
			ReduceOnly = ReduceOnly,
		};
}

sealed class BigOneContractConditional
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("priceType")]
	public string PriceType { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

sealed class BigOneOrder
{
	public string Id { get; set; }
	public string ClientOrderId { get; set; }
	public string Pair { get; set; }
	public string Action { get; set; }
	public string Type { get; set; }
	public string Status { get; set; }
	public decimal Price { get; set; }
	public decimal OriginalAmount { get; set; }
	public decimal ExecutedAmount { get; set; }
	public decimal? StopPrice { get; set; }
	public string TimeInForce { get; set; }
	public long CreatedTimestamp { get; set; }
	public long UpdatedTimestamp { get; set; }
	public BigOneMarketKind Kind { get; set; }
	public bool ReduceOnly { get; set; }
	public long Timestamp => UpdatedTimestamp > 0
		? UpdatedTimestamp
		: CreatedTimestamp;
	public decimal RemainingAmount
		=> (OriginalAmount - ExecutedAmount).Max(0);
	public long? ClientId
		=> long.TryParse(
			ClientOrderId?.StartsWithIgnoreCase("ss-") == true
				? ClientOrderId[3..]
				: ClientOrderId,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var value)
				? value
				: null;
}

sealed class BigOneSpotUserTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("asset_pair_name")]
	public string Market { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("maker_order_id")]
	public string MakerOrderId { get; set; }

	[JsonProperty("taker_order_id")]
	public string TakerOrderId { get; set; }

	[JsonProperty("maker_fee")]
	public decimal? MakerFee { get; set; }

	[JsonProperty("taker_fee")]
	public decimal? TakerFee { get; set; }

	[JsonProperty("inserted_at")]
	public DateTime? InsertedAt { get; set; }

	public BigOnePrivateTrade ToTrade()
		=> new()
		{
			TradeId = Id,
			OrderId = Side.EqualsIgnoreCase("ASK")
				? MakerOrderId
				: TakerOrderId,
			Pair = Market,
			Price = Price,
			BaseAmount = Amount,
			Fee = MakerFee ?? TakerFee,
			Action = Side,
			CreatedTimestamp = InsertedAt?.ToUtc()
				.ToBigOneMilliseconds() ?? 0,
		};
}

sealed class BigOneContractTradeExecution
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	public BigOnePrivateTrade ToTrade()
		=> new()
		{
			TradeId = Id,
			OrderId = OrderId,
			Pair = Symbol,
			Price = Price,
			BaseAmount = Size,
			Fee = Fee,
			FeeSymbol = Currency,
			Action = Side,
			CreatedTimestamp = BigOneExtensions.NormalizeTimestamp(
				Timestamp),
		};
}

sealed class BigOnePrivateTrade
{
	public string TradeId { get; set; }
	public string OrderId { get; set; }
	public string Pair { get; set; }
	public decimal Price { get; set; }
	public decimal BaseAmount { get; set; }
	public decimal? Fee { get; set; }
	public string FeeSymbol { get; set; }
	public string Action { get; set; }
	public long CreatedTimestamp { get; set; }
	public long Timestamp => CreatedTimestamp;
}

sealed class BigOnePlaceOrderRequest
{
	public string Market { get; init; }
	public string Side { get; init; }
	public string Volume { get; init; }
	public string QuoteVolume { get; init; }
	public string Price { get; init; }
	public string ClientOid { get; init; }
	public string StopPrice { get; init; }
	public string OrderType { get; init; }
	public bool ReduceOnly { get; init; }
	public bool TriggerAbove { get; init; }
	public bool PostOnly { get; init; }
}

sealed class BigOnePlaceOrderResult
{
	public BigOneOrder Order { get; init; }
	public string OrderId => Order?.Id;
}

sealed class BigOneContractStream
{
	[JsonProperty("cash")]
	public BigOneContractCash Cash { get; set; }

	[JsonProperty("positions")]
	public BigOneContractPosition[] Positions { get; set; }

	[JsonProperty("orders")]
	public BigOneContractOrder[] Orders { get; set; }

	[JsonProperty("trades")]
	public BigOneContractTradeExecution[] Trades { get; set; }
}
