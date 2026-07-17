namespace StockSharp.Trading212.Native.Model;

sealed class Trading212AccountSummary
{
	[JsonProperty("cash")]
	public Trading212Cash Cash { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("investments")]
	public Trading212Investments Investments { get; set; }

	[JsonProperty("totalValue")]
	public decimal TotalValue { get; set; }
}

sealed class Trading212Cash
{
	[JsonProperty("availableToTrade")]
	public decimal AvailableToTrade { get; set; }

	[JsonProperty("inPies")]
	public decimal InPies { get; set; }

	[JsonProperty("reservedForOrders")]
	public decimal ReservedForOrders { get; set; }
}

sealed class Trading212Investments
{
	[JsonProperty("currentValue")]
	public decimal CurrentValue { get; set; }

	[JsonProperty("realizedProfitLoss")]
	public decimal RealizedProfitLoss { get; set; }

	[JsonProperty("totalCost")]
	public decimal TotalCost { get; set; }

	[JsonProperty("unrealizedProfitLoss")]
	public decimal UnrealizedProfitLoss { get; set; }
}

sealed class Trading212Exchange
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("workingSchedules")]
	public Trading212WorkingSchedule[] WorkingSchedules { get; set; }
}

sealed class Trading212WorkingSchedule
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("timeEvents")]
	public Trading212TimeEvent[] TimeEvents { get; set; }
}

sealed class Trading212TimeEvent
{
	[JsonProperty("date")]
	public DateTime Date { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

sealed class Trading212TradableInstrument
{
	[JsonProperty("addedOn")]
	public DateTime AddedOn { get; set; }

	[JsonProperty("currencyCode")]
	public string CurrencyCode { get; set; }

	[JsonProperty("extendedHours")]
	public bool IsExtendedHours { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("maxOpenQuantity")]
	public decimal MaxOpenQuantity { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("shortName")]
	public string ShortName { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("workingScheduleId")]
	public long WorkingScheduleId { get; set; }
}

sealed class Trading212Instrument
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }
}

sealed class Trading212Order
{
	[JsonProperty("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("extendedHours")]
	public bool IsExtendedHours { get; set; }

	[JsonProperty("filledQuantity")]
	public decimal? FilledQuantity { get; set; }

	[JsonProperty("filledValue")]
	public decimal? FilledValue { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("initiatedFrom")]
	public string InitiatedFrom { get; set; }

	[JsonProperty("instrument")]
	public Trading212Instrument Instrument { get; set; }

	[JsonProperty("limitPrice")]
	public decimal? LimitPrice { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("strategy")]
	public string Strategy { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("value")]
	public decimal? Value { get; set; }
}

sealed class Trading212Position
{
	[JsonProperty("averagePricePaid")]
	public decimal AveragePricePaid { get; set; }

	[JsonProperty("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("currentPrice")]
	public decimal CurrentPrice { get; set; }

	[JsonProperty("instrument")]
	public Trading212Instrument Instrument { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("quantityAvailableForTrading")]
	public decimal QuantityAvailableForTrading { get; set; }

	[JsonProperty("quantityInPies")]
	public decimal QuantityInPies { get; set; }

	[JsonProperty("walletImpact")]
	public Trading212PositionWalletImpact WalletImpact { get; set; }
}

sealed class Trading212PositionWalletImpact
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("currentValue")]
	public decimal CurrentValue { get; set; }

	[JsonProperty("fxImpact")]
	public decimal FxImpact { get; set; }

	[JsonProperty("totalCost")]
	public decimal TotalCost { get; set; }

	[JsonProperty("unrealizedProfitLoss")]
	public decimal UnrealizedProfitLoss { get; set; }
}

sealed class Trading212HistoricalOrderPage
{
	[JsonProperty("items")]
	public Trading212HistoricalOrder[] Items { get; set; }

	[JsonProperty("nextPagePath")]
	public string NextPagePath { get; set; }
}

sealed class Trading212HistoricalOrder
{
	[JsonProperty("fill")]
	public Trading212Fill Fill { get; set; }

	[JsonProperty("order")]
	public Trading212Order Order { get; set; }
}

sealed class Trading212Fill
{
	[JsonProperty("filledAt")]
	public DateTime FilledAt { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("tradingMethod")]
	public string TradingMethod { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("walletImpact")]
	public Trading212FillWalletImpact WalletImpact { get; set; }
}

sealed class Trading212FillWalletImpact
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("fxRate")]
	public decimal FxRate { get; set; }

	[JsonProperty("netValue")]
	public decimal NetValue { get; set; }

	[JsonProperty("realisedProfitLoss")]
	public decimal RealisedProfitLoss { get; set; }

	[JsonProperty("taxes")]
	public Trading212Tax[] Taxes { get; set; }
}

sealed class Trading212Tax
{
	[JsonProperty("chargedAt")]
	public DateTime ChargedAt { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }
}

sealed class Trading212LimitOrderRequest
{
	[JsonProperty("limitPrice")]
	public decimal LimitPrice { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("timeValidity")]
	public string TimeValidity { get; set; }
}

sealed class Trading212MarketOrderRequest
{
	[JsonProperty("extendedHours")]
	public bool IsExtendedHours { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }
}

sealed class Trading212StopOrderRequest
{
	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("stopPrice")]
	public decimal StopPrice { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("timeValidity")]
	public string TimeValidity { get; set; }
}

sealed class Trading212StopLimitOrderRequest
{
	[JsonProperty("limitPrice")]
	public decimal LimitPrice { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("stopPrice")]
	public decimal StopPrice { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("timeValidity")]
	public string TimeValidity { get; set; }
}

sealed class Trading212ErrorResponse
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }
}
