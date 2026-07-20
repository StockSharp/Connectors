namespace StockSharp.Pacifica.Native;

sealed class PacificaResponse<T>
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }

	[JsonProperty("data")]
	public T Data { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("code")]
	public int? Code { get; init; }

	[JsonProperty("last_order_id")]
	public long? LastOrderId { get; init; }

	[JsonProperty("next_cursor")]
	public string NextCursor { get; init; }

	[JsonProperty("has_more")]
	public bool IsMore { get; init; }
}

sealed class PacificaMarket
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("tick_size")]
	public string TickSize { get; init; }

	[JsonProperty("min_tick")]
	public string MinimumPrice { get; init; }

	[JsonProperty("max_tick")]
	public string MaximumPrice { get; init; }

	[JsonProperty("lot_size")]
	public string LotSize { get; init; }

	[JsonProperty("max_leverage")]
	public int MaximumLeverage { get; init; }

	[JsonProperty("isolated_only")]
	public bool IsIsolatedOnly { get; init; }

	[JsonProperty("min_order_size")]
	public string MinimumOrderNotional { get; init; }

	[JsonProperty("max_order_size")]
	public string MaximumOrderNotional { get; init; }

	[JsonProperty("funding_rate")]
	public string FundingRate { get; init; }

	[JsonProperty("next_funding_rate")]
	public string NextFundingRate { get; init; }

	[JsonProperty("created_at")]
	public long CreatedAt { get; init; }

	[JsonProperty("instrument_type")]
	public PacificaInstrumentTypes InstrumentType { get; init; }

	[JsonProperty("base_asset")]
	public string BaseAsset { get; init; }
}

sealed class PacificaPrice
{
	[JsonProperty("funding")]
	public string FundingRate { get; init; }

	[JsonProperty("mark")]
	public string MarkPrice { get; init; }

	[JsonProperty("mid")]
	public string MidPrice { get; init; }

	[JsonProperty("next_funding")]
	public string NextFundingRate { get; init; }

	[JsonProperty("open_interest")]
	public string OpenInterest { get; init; }

	[JsonProperty("oracle")]
	public string OraclePrice { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("volume_24h")]
	public string Volume24Hours { get; init; }

	[JsonProperty("yesterday_price")]
	public string YesterdayPrice { get; init; }
}

sealed class PacificaBook
{
	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("l")]
	public PacificaBookLevel[][] Levels { get; init; }

	[JsonProperty("t")]
	public long Timestamp { get; init; }

	[JsonProperty("li")]
	public long? LastOrderId { get; init; }
}

sealed class PacificaBookLevel
{
	[JsonProperty("p")]
	public string Price { get; init; }

	[JsonProperty("a")]
	public string Amount { get; init; }

	[JsonProperty("n")]
	public int OrdersCount { get; init; }
}

sealed class PacificaBestBidOffer
{
	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("i")]
	public long OrderId { get; init; }

	[JsonProperty("li")]
	public long LastOrderId { get; init; }

	[JsonProperty("t")]
	public long Timestamp { get; init; }

	[JsonProperty("b")]
	public string BidPrice { get; init; }

	[JsonProperty("B")]
	public string BidAmount { get; init; }

	[JsonProperty("a")]
	public string AskPrice { get; init; }

	[JsonProperty("A")]
	public string AskAmount { get; init; }
}

sealed class PacificaPublicTrade
{
	[JsonProperty("h")]
	public long HistoryId { get; init; }

	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("p")]
	private string AlternatePrice
	{
		init
		{
			if (Price.IsEmpty())
				Price = value;
		}
	}

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("a")]
	private string AlternateAmount
	{
		init
		{
			if (Amount.IsEmpty())
				Amount = value;
		}
	}

	[JsonProperty("side")]
	public PacificaTradeSides Side { get; init; }

	[JsonProperty("d")]
	private PacificaTradeSides AlternateSide
	{
		init => Side = value;
	}

	[JsonProperty("event_type")]
	public PacificaTradeEvents? EventType { get; init; }

	[JsonProperty("cause")]
	public PacificaTradeCauses Cause { get; init; }

	[JsonProperty("tc")]
	private PacificaTradeCauses AlternateCause
	{
		init => Cause = value;
	}

	[JsonProperty("created_at")]
	public long CreatedAt { get; init; }

	[JsonProperty("t")]
	private long AlternateCreatedAt
	{
		init
		{
			if (CreatedAt == 0)
				CreatedAt = value;
		}
	}

	[JsonProperty("li")]
	public long? LastOrderId { get; init; }

	[JsonProperty("it")]
	public int? InstrumentType { get; init; }
}

sealed class PacificaCandle
{
	[JsonProperty("t")]
	public long OpenTime { get; init; }

	[JsonProperty("T")]
	public long CloseTime { get; init; }

	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("i")]
	public PacificaCandleIntervals Interval { get; init; }

	[JsonProperty("o")]
	public string OpenPrice { get; init; }

	[JsonProperty("c")]
	public string ClosePrice { get; init; }

	[JsonProperty("h")]
	public string HighPrice { get; init; }

	[JsonProperty("l")]
	public string LowPrice { get; init; }

	[JsonProperty("v")]
	public string Volume { get; init; }

	[JsonProperty("n")]
	public int TradesCount { get; init; }
}

sealed class PacificaAccountInfo
{
	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("fee_level")]
	public int FeeLevel { get; init; }

	[JsonProperty("maker_fee")]
	public string MakerFee { get; init; }

	[JsonProperty("taker_fee")]
	public string TakerFee { get; init; }

	[JsonProperty("account_equity")]
	public string AccountEquity { get; init; }

	[JsonProperty("available_to_spend")]
	public string AvailableToSpend { get; init; }

	[JsonProperty("available_to_withdraw")]
	public string AvailableToWithdraw { get; init; }

	[JsonProperty("pending_balance")]
	public string PendingBalance { get; init; }

	[JsonProperty("pending_interest")]
	public string PendingInterest { get; init; }

	[JsonProperty("spot_collateral")]
	public string SpotCollateral { get; init; }

	[JsonProperty("cross_account_equity")]
	public string CrossAccountEquity { get; init; }

	[JsonProperty("spot_market_value")]
	public string SpotMarketValue { get; init; }

	[JsonProperty("total_margin_used")]
	public string TotalMarginUsed { get; init; }

	[JsonProperty("cross_mmr")]
	public string CrossMaintenanceMargin { get; init; }

	[JsonProperty("positions_count")]
	public int PositionsCount { get; init; }

	[JsonProperty("orders_count")]
	public int OrdersCount { get; init; }

	[JsonProperty("stop_orders_count")]
	public int StopOrdersCount { get; init; }

	[JsonProperty("updated_at")]
	public long UpdatedAt { get; init; }

	[JsonProperty("spot_balances")]
	public PacificaSpotBalance[] SpotBalances { get; init; }
}

sealed class PacificaSpotBalance
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("available_to_withdraw")]
	public string AvailableToWithdraw { get; init; }

	[JsonProperty("pending_balance")]
	public string PendingBalance { get; init; }
}

sealed class PacificaPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public PacificaSides Side { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("entry_price")]
	public string EntryPrice { get; init; }

	[JsonProperty("margin")]
	public string Margin { get; init; }

	[JsonProperty("funding")]
	public string Funding { get; init; }

	[JsonProperty("isolated")]
	public bool IsIsolated { get; init; }

	[JsonProperty("liquidation_price")]
	public string LiquidationPrice { get; init; }

	[JsonProperty("created_at")]
	public long CreatedAt { get; init; }

	[JsonProperty("updated_at")]
	public long UpdatedAt { get; init; }
}

sealed class PacificaOrder
{
	[JsonProperty("order_id")]
	public long OrderId { get; init; }

	[JsonProperty("i")]
	private long AlternateOrderId
	{
		init
		{
			if (OrderId == 0)
				OrderId = value;
		}
	}

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("I")]
	private string AlternateClientOrderId
	{
		init
		{
			if (ClientOrderId.IsEmpty())
				ClientOrderId = value;
		}
	}

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("s")]
	private string AlternateSymbol
	{
		init
		{
			if (Symbol.IsEmpty())
				Symbol = value;
		}
	}

	[JsonProperty("side")]
	public PacificaSides Side { get; init; }

	[JsonProperty("d")]
	private PacificaSides AlternateSide
	{
		init => Side = value;
	}

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("initial_price")]
	public string InitialPrice { get; init; }

	[JsonProperty("ip")]
	private string AlternateInitialPrice
	{
		init
		{
			if (InitialPrice.IsEmpty())
				InitialPrice = value;
		}
	}

	[JsonProperty("average_filled_price")]
	public string AverageFilledPrice { get; init; }

	[JsonProperty("p")]
	private string AlternateAverageFilledPrice
	{
		init
		{
			if (AverageFilledPrice.IsEmpty())
				AverageFilledPrice = value;
		}
	}

	[JsonProperty("lp")]
	public string LastFilledPrice { get; init; }

	[JsonProperty("initial_amount")]
	public string InitialAmount { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("a")]
	private string AlternateAmount
	{
		init
		{
			if (Amount.IsEmpty())
				Amount = value;
		}
	}

	[JsonProperty("filled_amount")]
	public string FilledAmount { get; init; }

	[JsonProperty("f")]
	private string AlternateFilledAmount
	{
		init
		{
			if (FilledAmount.IsEmpty())
				FilledAmount = value;
		}
	}

	[JsonProperty("cancelled_amount")]
	public string CancelledAmount { get; init; }

	[JsonProperty("order_status")]
	public PacificaOrderStatuses? Status { get; init; }

	[JsonProperty("os")]
	private PacificaOrderStatuses AlternateStatus
	{
		init => Status = value;
	}

	[JsonProperty("order_type")]
	public PacificaOrderTypes OrderType { get; init; }

	[JsonProperty("ot")]
	private PacificaOrderTypes AlternateOrderType
	{
		init => OrderType = value;
	}

	[JsonProperty("stop_price")]
	public string StopPrice { get; init; }

	[JsonProperty("sp")]
	private string AlternateStopPrice
	{
		init
		{
			if (StopPrice.IsEmpty())
				StopPrice = value;
		}
	}

	[JsonProperty("stop_parent_order_id")]
	public long? StopParentOrderId { get; init; }

	[JsonProperty("si")]
	private long? AlternateStopParentOrderId
	{
		init
		{
			if (StopParentOrderId is null)
				StopParentOrderId = value;
		}
	}

	[JsonProperty("trigger_price_type")]
	public global::StockSharp.Pacifica.PacificaTriggerPriceTypes?
		TriggerPriceType { get; init; }

	[JsonProperty("tp")]
	private global::StockSharp.Pacifica.PacificaTriggerPriceTypes?
		AlternateTriggerPriceType
	{
		init
		{
			if (TriggerPriceType is null)
				TriggerPriceType = value;
		}
	}

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("r")]
	private bool AlternateIsReduceOnly
	{
		init => IsReduceOnly = value;
	}

	[JsonProperty("reason")]
	public string Reason { get; init; }

	[JsonProperty("event_type")]
	public PacificaOrderEvents? EventType { get; init; }

	[JsonProperty("oe")]
	private PacificaOrderEvents AlternateEventType
	{
		init => EventType = value;
	}

	[JsonProperty("created_at")]
	public long CreatedAt { get; init; }

	[JsonProperty("ct")]
	private long AlternateCreatedAt
	{
		init
		{
			if (CreatedAt == 0)
				CreatedAt = value;
		}
	}

	[JsonProperty("updated_at")]
	public long UpdatedAt { get; init; }

	[JsonProperty("ut")]
	private long AlternateUpdatedAt
	{
		init
		{
			if (UpdatedAt == 0)
				UpdatedAt = value;
		}
	}

	[JsonProperty("li")]
	public long? LastOrderId { get; init; }
}

sealed class PacificaAccountTrade
{
	[JsonProperty("history_id")]
	public long HistoryId { get; init; }

	[JsonProperty("h")]
	private long AlternateHistoryId
	{
		init
		{
			if (HistoryId == 0)
				HistoryId = value;
		}
	}

	[JsonProperty("order_id")]
	public long OrderId { get; init; }

	[JsonProperty("i")]
	private long AlternateOrderId
	{
		init
		{
			if (OrderId == 0)
				OrderId = value;
		}
	}

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("I")]
	private string AlternateClientOrderId
	{
		init
		{
			if (ClientOrderId.IsEmpty())
				ClientOrderId = value;
		}
	}

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("s")]
	private string AlternateSymbol
	{
		init
		{
			if (Symbol.IsEmpty())
				Symbol = value;
		}
	}

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("a")]
	private string AlternateAmount
	{
		init
		{
			if (Amount.IsEmpty())
				Amount = value;
		}
	}

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("p")]
	private string AlternatePrice
	{
		init
		{
			if (Price.IsEmpty())
				Price = value;
		}
	}

	[JsonProperty("entry_price")]
	public string EntryPrice { get; init; }

	[JsonProperty("o")]
	private string AlternateEntryPrice
	{
		init
		{
			if (EntryPrice.IsEmpty())
				EntryPrice = value;
		}
	}

	[JsonProperty("fee")]
	public string Fee { get; init; }

	[JsonProperty("f")]
	private string AlternateFee
	{
		init
		{
			if (Fee.IsEmpty())
				Fee = value;
		}
	}

	[JsonProperty("pnl")]
	public string ProfitLoss { get; init; }

	[JsonProperty("n")]
	private string AlternateProfitLoss
	{
		init
		{
			if (ProfitLoss.IsEmpty())
				ProfitLoss = value;
		}
	}

	[JsonProperty("event_type")]
	public PacificaTradeEvents EventType { get; init; }

	[JsonProperty("te")]
	private PacificaTradeEvents AlternateEventType
	{
		init => EventType = value;
	}

	[JsonProperty("side")]
	public PacificaTradeSides Side { get; init; }

	[JsonProperty("ts")]
	private PacificaTradeSides AlternateSide
	{
		init => Side = value;
	}

	[JsonProperty("cause")]
	public PacificaTradeCauses Cause { get; init; }

	[JsonProperty("tc")]
	private PacificaTradeCauses AlternateCause
	{
		init => Cause = value;
	}

	[JsonProperty("created_at")]
	public long CreatedAt { get; init; }

	[JsonProperty("t")]
	private long AlternateCreatedAt
	{
		init
		{
			if (CreatedAt == 0)
				CreatedAt = value;
		}
	}

	[JsonProperty("li")]
	public long? LastOrderId { get; init; }

	[JsonProperty("it")]
	public int? InstrumentType { get; init; }
}

sealed class PacificaAccountInfoUpdate
{
	[JsonProperty("ae")]
	public string AccountEquity { get; init; }

	[JsonProperty("as")]
	public string AvailableToSpend { get; init; }

	[JsonProperty("aw")]
	public string AvailableToWithdraw { get; init; }

	[JsonProperty("b")]
	public string Balance { get; init; }

	[JsonProperty("f")]
	public int FeeLevel { get; init; }

	[JsonProperty("mu")]
	public string TotalMarginUsed { get; init; }

	[JsonProperty("cm")]
	public string CrossMaintenanceMargin { get; init; }

	[JsonProperty("oc")]
	public int OrdersCount { get; init; }

	[JsonProperty("pb")]
	public string PendingBalance { get; init; }

	[JsonProperty("pc")]
	public int PositionsCount { get; init; }

	[JsonProperty("sc")]
	public int StopOrdersCount { get; init; }

	[JsonProperty("sb")]
	public PacificaSpotBalanceUpdate[] SpotBalances { get; init; }

	[JsonProperty("t")]
	public long Timestamp { get; init; }
}

sealed class PacificaSpotBalanceUpdate
{
	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("a")]
	public string Amount { get; init; }

	[JsonProperty("aw")]
	public string AvailableToWithdraw { get; init; }

	[JsonProperty("pb")]
	public string PendingBalance { get; init; }
}

sealed class PacificaPositionUpdate
{
	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("d")]
	public PacificaSides Side { get; init; }

	[JsonProperty("a")]
	public string Amount { get; init; }

	[JsonProperty("p")]
	public string EntryPrice { get; init; }

	[JsonProperty("m")]
	public string Margin { get; init; }

	[JsonProperty("f")]
	public string Funding { get; init; }

	[JsonProperty("i")]
	public bool IsIsolated { get; init; }

	[JsonProperty("l")]
	public string LiquidationPrice { get; init; }

	[JsonProperty("t")]
	public long Timestamp { get; init; }
}

readonly record struct PacificaSubscriptionKey(PacificaSources Source,
	string Symbol, PacificaCandleIntervals? Interval, int? AggregationLevel,
	string Account);

sealed class PacificaSubscriptionRequest
{
	[JsonProperty("method")]
	public PacificaWebSocketMethods Method { get; init; }

	[JsonProperty("params")]
	public PacificaSubscriptionParameters Parameters { get; init; }
}

sealed class PacificaSubscriptionParameters
{
	[JsonProperty("source")]
	public PacificaSources Source { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("interval")]
	public PacificaCandleIntervals? Interval { get; init; }

	[JsonProperty("agg_level")]
	public int? AggregationLevel { get; init; }

	[JsonProperty("account")]
	public string Account { get; init; }
}

sealed class PacificaPingRequest
{
	[JsonProperty("method")]
	public PacificaWebSocketMethods Method { get; init; } =
		PacificaWebSocketMethods.Ping;
}

sealed class PacificaWebSocketHeader
{
	[JsonProperty("channel")]
	public PacificaChannels? Channel { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("type")]
	public PacificaOperationTypes? Type { get; init; }

	[JsonProperty("code")]
	public int? Code { get; init; }

	[JsonProperty("err")]
	public string Error { get; init; }

	[JsonProperty("t")]
	public long? Timestamp { get; init; }
}

sealed class PacificaWebSocketEnvelope<T>
{
	[JsonProperty("channel")]
	public PacificaChannels Channel { get; init; }

	[JsonProperty("data")]
	public T Data { get; init; }

	[JsonProperty("li")]
	public long? LastOrderId { get; init; }
}

sealed class PacificaSubscriptionAcknowledgement
{
	[JsonProperty("source")]
	public PacificaSources Source { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("interval")]
	public PacificaCandleIntervals? Interval { get; init; }

	[JsonProperty("agg_level")]
	public int? AggregationLevel { get; init; }

	[JsonProperty("account")]
	public string Account { get; init; }
}
