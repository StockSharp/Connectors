namespace StockSharp.NinjaTrader.Native.Model;

sealed class NinjaTraderSocketEnvelope
{
	[JsonProperty("s")]
	public int? Status { get; set; }
	[JsonProperty("i")]
	public long? RequestId { get; set; }
	[JsonProperty("e")]
	public string Event { get; set; }
	[JsonProperty("d")]
	public NinjaTraderSocketData Data { get; set; }
}

sealed class NinjaTraderSocketData
{
	public string ErrorText { get; set; }
	public long? HistoricalId { get; set; }
	public long? RealtimeId { get; set; }
	public string EntityType { get; set; }
	public string EventType { get; set; }
	public NinjaTraderEntity Entity { get; set; }
	public NinjaTraderQuote[] Quotes { get; set; }
	public NinjaTraderDom[] Doms { get; set; }
	public NinjaTraderChart[] Charts { get; set; }
}

sealed class NinjaTraderEntity
{
	public long Id { get; set; }
	public long AccountId { get; set; }
	public long ContractId { get; set; }
	public long OrderId { get; set; }
	public DateTime Timestamp { get; set; }
	public NinjaTraderActions Action { get; set; }
	public NinjaTraderOrderStates OrdStatus { get; set; }
	public int OrderQty { get; set; }
	public NinjaTraderOrderTypes OrderType { get; set; }
	public decimal? Price { get; set; }
	public decimal? StopPrice { get; set; }
	public NinjaTraderTimeInForces TimeInForce { get; set; }
	public DateTime? ExpireTime { get; set; }
	public string Text { get; set; }
	public int Qty { get; set; }
	public int NetPos { get; set; }
	public decimal? NetPrice { get; set; }
	public decimal Amount { get; set; }
	public decimal? RealizedPnL { get; set; }

	public NinjaTraderOrder ToOrder()
		=> new()
		{
			Id = Id,
			AccountId = AccountId,
			ContractId = ContractId.DefaultAsNull(),
			Timestamp = Timestamp,
			Action = Action,
			OrdStatus = OrdStatus,
		};

	public NinjaTraderOrderVersion ToOrderVersion()
		=> new()
		{
			Id = Id,
			OrderId = OrderId,
			OrderQty = OrderQty,
			OrderType = OrderType,
			Price = Price,
			StopPrice = StopPrice,
			TimeInForce = TimeInForce,
			ExpireTime = ExpireTime,
			Text = Text,
		};

	public NinjaTraderFill ToFill()
		=> new()
		{
			Id = Id,
			OrderId = OrderId,
			ContractId = ContractId,
			Timestamp = Timestamp,
			Action = Action,
			Qty = Qty,
			Price = Price ?? 0,
			IsActive = true,
		};

	public NinjaTraderPosition ToPosition()
		=> new()
		{
			Id = Id,
			AccountId = AccountId,
			ContractId = ContractId,
			Timestamp = Timestamp,
			NetPos = NetPos,
			NetPrice = NetPrice,
		};

	public NinjaTraderCashBalance ToCashBalance()
		=> new()
		{
			Id = Id,
			AccountId = AccountId,
			Timestamp = Timestamp,
			Amount = Amount,
			RealizedPnL = RealizedPnL,
		};
}

sealed class NinjaTraderQuote
{
	public DateTime Timestamp { get; set; }
	public long ContractId { get; set; }
	public NinjaTraderQuoteEntries Entries { get; set; }
}

sealed class NinjaTraderQuoteEntries
{
	public NinjaTraderPriceLevel Bid { get; set; }
	public NinjaTraderPriceLevel Offer { get; set; }
	public NinjaTraderPriceLevel Trade { get; set; }
	public NinjaTraderPriceLevel TotalTradeVolume { get; set; }
	public NinjaTraderPriceLevel LowPrice { get; set; }
	public NinjaTraderPriceLevel OpenInterest { get; set; }
	public NinjaTraderPriceLevel OpeningPrice { get; set; }
	public NinjaTraderPriceLevel HighPrice { get; set; }
	public NinjaTraderPriceLevel SettlementPrice { get; set; }
}

sealed class NinjaTraderPriceLevel
{
	public decimal? Price { get; set; }
	public decimal? Size { get; set; }
}

sealed class NinjaTraderDom
{
	public long ContractId { get; set; }
	public DateTime Timestamp { get; set; }
	public NinjaTraderPriceLevel[] Bids { get; set; }
	public NinjaTraderPriceLevel[] Offers { get; set; }
}

sealed class NinjaTraderChart
{
	public long Id { get; set; }
	public NinjaTraderBar[] Bars { get; set; }
}

sealed class NinjaTraderBar
{
	public DateTime Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal UpVolume { get; set; }
	public decimal DownVolume { get; set; }
}

sealed class NinjaTraderSymbolRequest
{
	public string Symbol { get; set; }
}

sealed class NinjaTraderSyncRequest
{
	public long[] Users { get; set; }
	public long[] Accounts { get; set; }
	[JsonProperty("splitResponses")]
	public bool IsSplitResponses { get; set; }
}

sealed class NinjaTraderChartRequest
{
	public string Symbol { get; set; }
	public NinjaTraderChartDescription ChartDescription { get; set; }
	public NinjaTraderChartTimeRange TimeRange { get; set; }
}

sealed class NinjaTraderChartDescription
{
	public NinjaTraderChartTypes UnderlyingType { get; set; }
	public int ElementSize { get; set; }
	public NinjaTraderChartUnits ElementSizeUnit { get; set; }
	[JsonProperty("withHistogram")]
	public bool IsWithHistogram { get; set; }
}

sealed class NinjaTraderChartTimeRange
{
	public DateTime? ClosestTimestamp { get; set; }
	public DateTime? AsFarAsTimestamp { get; set; }
	public int? AsMuchAsElements { get; set; }
}

sealed class NinjaTraderCancelChartRequest
{
	public long SubscriptionId { get; set; }
}
