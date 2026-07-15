namespace StockSharp.Tradovate.Native.Model;

sealed class TradovateSocketEnvelope
{
	[JsonProperty("s")]
	public int? Status { get; set; }
	[JsonProperty("i")]
	public long? RequestId { get; set; }
	[JsonProperty("e")]
	public string Event { get; set; }
	[JsonProperty("d")]
	public TradovateSocketData Data { get; set; }
}

sealed class TradovateSocketData
{
	public string ErrorText { get; set; }
	public long? HistoricalId { get; set; }
	public long? RealtimeId { get; set; }
	public string EntityType { get; set; }
	public string EventType { get; set; }
	public TradovateEntity Entity { get; set; }
	public TradovateQuote[] Quotes { get; set; }
	public TradovateDom[] Doms { get; set; }
	public TradovateChart[] Charts { get; set; }
}

sealed class TradovateEntity
{
	public long Id { get; set; }
	public long AccountId { get; set; }
	public long ContractId { get; set; }
	public long OrderId { get; set; }
	public DateTime Timestamp { get; set; }
	public TradovateActions Action { get; set; }
	public TradovateOrderStates OrdStatus { get; set; }
	public int OrderQty { get; set; }
	public TradovateOrderTypes OrderType { get; set; }
	public decimal? Price { get; set; }
	public decimal? StopPrice { get; set; }
	public TradovateTimeInForces TimeInForce { get; set; }
	public DateTime? ExpireTime { get; set; }
	public string Text { get; set; }
	public int Qty { get; set; }
	public int NetPos { get; set; }
	public decimal? NetPrice { get; set; }
	public decimal Amount { get; set; }
	public decimal? RealizedPnL { get; set; }

	public TradovateOrder ToOrder()
		=> new()
		{
			Id = Id,
			AccountId = AccountId,
			ContractId = ContractId.DefaultAsNull(),
			Timestamp = Timestamp,
			Action = Action,
			OrdStatus = OrdStatus,
		};

	public TradovateOrderVersion ToOrderVersion()
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

	public TradovateFill ToFill()
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

	public TradovatePosition ToPosition()
		=> new()
		{
			Id = Id,
			AccountId = AccountId,
			ContractId = ContractId,
			Timestamp = Timestamp,
			NetPos = NetPos,
			NetPrice = NetPrice,
		};

	public TradovateCashBalance ToCashBalance()
		=> new()
		{
			Id = Id,
			AccountId = AccountId,
			Timestamp = Timestamp,
			Amount = Amount,
			RealizedPnL = RealizedPnL,
		};
}

sealed class TradovateQuote
{
	public DateTime Timestamp { get; set; }
	public long ContractId { get; set; }
	public TradovateQuoteEntries Entries { get; set; }
}

sealed class TradovateQuoteEntries
{
	public TradovatePriceLevel Bid { get; set; }
	public TradovatePriceLevel Offer { get; set; }
	public TradovatePriceLevel Trade { get; set; }
	public TradovatePriceLevel TotalTradeVolume { get; set; }
	public TradovatePriceLevel LowPrice { get; set; }
	public TradovatePriceLevel OpenInterest { get; set; }
	public TradovatePriceLevel OpeningPrice { get; set; }
	public TradovatePriceLevel HighPrice { get; set; }
	public TradovatePriceLevel SettlementPrice { get; set; }
}

sealed class TradovatePriceLevel
{
	public decimal? Price { get; set; }
	public decimal? Size { get; set; }
}

sealed class TradovateDom
{
	public long ContractId { get; set; }
	public DateTime Timestamp { get; set; }
	public TradovatePriceLevel[] Bids { get; set; }
	public TradovatePriceLevel[] Offers { get; set; }
}

sealed class TradovateChart
{
	public long Id { get; set; }
	public TradovateBar[] Bars { get; set; }
}

sealed class TradovateBar
{
	public DateTime Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal UpVolume { get; set; }
	public decimal DownVolume { get; set; }
}

sealed class TradovateSymbolRequest
{
	public string Symbol { get; set; }
}

sealed class TradovateSyncRequest
{
	public long[] Users { get; set; }
	public long[] Accounts { get; set; }
	[JsonProperty("splitResponses")]
	public bool IsSplitResponses { get; set; }
}

sealed class TradovateChartRequest
{
	public string Symbol { get; set; }
	public TradovateChartDescription ChartDescription { get; set; }
	public TradovateChartTimeRange TimeRange { get; set; }
}

sealed class TradovateChartDescription
{
	public TradovateChartTypes UnderlyingType { get; set; }
	public int ElementSize { get; set; }
	public TradovateChartUnits ElementSizeUnit { get; set; }
	[JsonProperty("withHistogram")]
	public bool IsWithHistogram { get; set; }
}

sealed class TradovateChartTimeRange
{
	public DateTime? ClosestTimestamp { get; set; }
	public DateTime? AsFarAsTimestamp { get; set; }
	public int? AsMuchAsElements { get; set; }
}

sealed class TradovateCancelChartRequest
{
	public long SubscriptionId { get; set; }
}
