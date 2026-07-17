namespace StockSharp.LemonMarkets.Native.Model;

sealed class LemonPage<T>
{
	[JsonProperty("data")]
	public T[] Data { get; set; }

	[JsonProperty("pagination")]
	public LemonPagination Pagination { get; set; }
}

sealed class LemonPagination
{
	[JsonProperty("next_cursor")]
	public string NextCursor { get; set; }
}

sealed class LemonAccount
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("cash_account")]
	public LemonBankAccount CashAccount { get; set; }

	[JsonProperty("securities_account")]
	public LemonSecuritiesAccount SecuritiesAccount { get; set; }
}

sealed class LemonBankAccount
{
	[JsonProperty("iban")]
	public string Iban { get; set; }

	[JsonProperty("bic")]
	public string Bic { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

sealed class LemonSecuritiesAccount
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("number")]
	public string Number { get; set; }

	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }
}

sealed class LemonFinancials
{
	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("buying_power")]
	public decimal BuyingPower { get; set; }

	[JsonProperty("funds_to_withdraw")]
	public decimal FundsToWithdraw { get; set; }

	[JsonProperty("blocked")]
	public decimal Blocked { get; set; }

	[JsonProperty("to_be_settled")]
	public decimal ToBeSettled { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}

sealed class LemonInstrument
{
	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("fund_profit_model")]
	public string FundProfitModel { get; set; }

	[JsonProperty("total_expense_ratio_pct")]
	public decimal? TotalExpenseRatioPct { get; set; }

	[JsonProperty("active_trading_halts")]
	public LemonTradingHalt[] ActiveTradingHalts { get; set; }
}

sealed class LemonTradingHalt
{
	[JsonProperty("valid_from")]
	public DateTime ValidFrom { get; set; }

	[JsonProperty("valid_to")]
	public DateTime? ValidTo { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

sealed class LemonPrice
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("updated_at")]
	public DateTime? UpdatedAt { get; set; }

	[JsonProperty("valuation_date")]
	public DateTime? ValuationDate { get; set; }
}

sealed class LemonPosition
{
	[JsonProperty("buy_in")]
	public decimal BuyIn { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("instrument")]
	public LemonPositionInstrument Instrument { get; set; }

	[JsonProperty("latest_bid")]
	public decimal? LatestBid { get; set; }

	[JsonProperty("latest_price")]
	public LemonLatestPrice LatestPrice { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }
}

sealed class LemonPositionInstrument
{
	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }
}

sealed class LemonLatestPrice
{
	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("updated_at")]
	public DateTime? UpdatedAt { get; set; }

	[JsonProperty("valuation_date")]
	public DateTime? ValuationDate { get; set; }
}

sealed class LemonOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("fees")]
	public LemonFees Fees { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("history")]
	public LemonOrderStatusChange[] History { get; set; }

	[JsonProperty("appropriateness_check")]
	public LemonAppropriatenessCheck AppropriatenessCheck { get; set; }

	[JsonProperty("sca")]
	public LemonScaRequirement Sca { get; set; }

	[JsonProperty("securities_account")]
	public string SecuritiesAccount { get; set; }
}

sealed class LemonFees
{
	[JsonProperty("pct")]
	public decimal? Percent { get; set; }

	[JsonProperty("base_amount")]
	public decimal? BaseAmount { get; set; }
}

sealed class LemonOrderStatusChange
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }
}

sealed class LemonAppropriatenessCheck
{
	[JsonProperty("required")]
	public bool IsRequired { get; set; }
}

sealed class LemonScaRequirement
{
	[JsonProperty("required")]
	public bool IsRequired { get; set; }

	[JsonProperty("challenge")]
	public string Challenge { get; set; }
}

sealed class LemonTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("taxes")]
	public LemonTax[] Taxes { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("order")]
	public string Order { get; set; }

	[JsonProperty("execution_venue")]
	public string ExecutionVenue { get; set; }

	[JsonProperty("securities_account")]
	public string SecuritiesAccount { get; set; }

	[JsonProperty("executed_at")]
	public DateTime ExecutedAt { get; set; }
}

sealed class LemonTax
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }
}

sealed class LemonEvent
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("created_at")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("context")]
	public LemonEventContext Context { get; set; }
}

sealed class LemonEventContext
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("securities_account")]
	public string SecuritiesAccount { get; set; }

	[JsonProperty("order")]
	public string Order { get; set; }

	[JsonProperty("trade")]
	public string Trade { get; set; }

	[JsonProperty("transaction")]
	public string Transaction { get; set; }
}

sealed class LemonActorRequest
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("person")]
	public string Person { get; set; }
}

sealed class LemonFeesRequest
{
	[JsonProperty("pct")]
	public string Percent { get; set; }

	[JsonProperty("base_amount")]
	public string BaseAmount { get; set; }
}

sealed class LemonCreateOrderRequest
{
	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("fees")]
	public LemonFeesRequest Fees { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("securities_account")]
	public string SecuritiesAccount { get; set; }

	[JsonProperty("actor")]
	public LemonActorRequest Actor { get; set; }
}

sealed class LemonConfirmOrderRequest
{
	[JsonProperty("actor")]
	public LemonActorRequest Actor { get; set; }

	[JsonProperty("appropriateness_consent")]
	public bool? IsAppropriatenessConsentAccepted { get; set; }
}

sealed class LemonCancelOrderRequest
{
	[JsonProperty("actor")]
	public LemonActorRequest Actor { get; set; }
}

sealed class LemonErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }
}
