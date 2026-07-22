namespace StockSharp.Deriv.Native.Model;

sealed class DerivActiveSymbol
{
	[JsonProperty("exchange_is_open")]
	public int IsExchangeOpen { get; set; }

	[JsonProperty("is_trading_suspended")]
	public int IsTradingSuspended { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("pip_size")]
	public decimal PipSize { get; set; }

	[JsonProperty("subgroup")]
	public string Subgroup { get; set; }

	[JsonProperty("submarket")]
	public string Submarket { get; set; }

	[JsonProperty("trade_count")]
	public long TradeCount { get; set; }

	[JsonProperty("underlying_symbol")]
	public string Symbol { get; set; }

	[JsonProperty("underlying_symbol_name")]
	public string Name { get; set; }

	[JsonProperty("underlying_symbol_type")]
	public string SymbolType { get; set; }
}

sealed class DerivTick
{
	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("epoch")]
	public long Epoch { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("pip_size")]
	public int PipSize { get; set; }

	[JsonProperty("quote")]
	public decimal Quote { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class DerivHistory
{
	[JsonProperty("prices")]
	public decimal[] Prices { get; set; }

	[JsonProperty("times")]
	public long[] Times { get; set; }
}

sealed class DerivCandle
{
	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("epoch")]
	public long Epoch { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }
}

sealed class DerivOhlc
{
	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("epoch")]
	public long Epoch { get; set; }

	[JsonProperty("granularity")]
	public int Granularity { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("open_time")]
	public long OpenTime { get; set; }

	[JsonProperty("pip_size")]
	public int PipSize { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	public DerivCandle ToCandle()
		=> new()
		{
			Open = Open,
			High = High,
			Low = Low,
			Close = Close,
			Epoch = OpenTime,
		};
}

sealed class DerivProposal
{
	[JsonProperty("ask_price")]
	public decimal AskPrice { get; set; }

	[JsonProperty("date_expiry")]
	public long DateExpiry { get; set; }

	[JsonProperty("date_start")]
	public long DateStart { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("longcode")]
	public string LongCode { get; set; }

	[JsonProperty("payout")]
	public decimal Payout { get; set; }

	[JsonProperty("spot")]
	public decimal Spot { get; set; }

	[JsonProperty("spot_time")]
	public long SpotTime { get; set; }
}

sealed class DerivBuy
{
	[JsonProperty("balance_after")]
	public decimal BalanceAfter { get; set; }

	[JsonProperty("buy_price")]
	public decimal BuyPrice { get; set; }

	[JsonProperty("contract_id")]
	public long ContractId { get; set; }

	[JsonProperty("longcode")]
	public string LongCode { get; set; }

	[JsonProperty("payout")]
	public decimal Payout { get; set; }

	[JsonProperty("purchase_time")]
	public long PurchaseTime { get; set; }

	[JsonProperty("shortcode")]
	public string ShortCode { get; set; }

	[JsonProperty("start_time")]
	public long StartTime { get; set; }

	[JsonProperty("transaction_id")]
	public long TransactionId { get; set; }
}

sealed class DerivClose
{
	[JsonProperty("balance_after")]
	public decimal BalanceAfter { get; set; }

	[JsonProperty("contract_id")]
	public long ContractId { get; set; }

	[JsonProperty("reference_id")]
	public long ReferenceId { get; set; }

	[JsonProperty("sold_for")]
	public decimal SoldFor { get; set; }

	[JsonProperty("transaction_id")]
	public long TransactionId { get; set; }
}

sealed class DerivPortfolio
{
	[JsonProperty("contracts")]
	public DerivPortfolioContract[] Contracts { get; set; }
}

sealed class DerivPortfolioContract
{
	[JsonProperty("buy_price")]
	public decimal BuyPrice { get; set; }

	[JsonProperty("contract_id")]
	public long ContractId { get; set; }

	[JsonProperty("contract_type")]
	public string ContractType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("date_start")]
	public long DateStart { get; set; }

	[JsonProperty("expiry_time")]
	public long ExpiryTime { get; set; }

	[JsonProperty("longcode")]
	public string LongCode { get; set; }

	[JsonProperty("payout")]
	public decimal Payout { get; set; }

	[JsonProperty("purchase_time")]
	public long PurchaseTime { get; set; }

	[JsonProperty("shortcode")]
	public string ShortCode { get; set; }

	[JsonProperty("transaction_id")]
	public long TransactionId { get; set; }

	[JsonProperty("underlying_symbol")]
	public string Symbol { get; set; }
}

sealed class DerivTransactionIds
{
	[JsonProperty("buy")]
	public long? Buy { get; set; }

	[JsonProperty("sell")]
	public long? Sell { get; set; }
}

sealed class DerivOpenContract
{
	[JsonProperty("barrier")]
	public string Barrier { get; set; }

	[JsonProperty("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("buy_price")]
	public decimal BuyPrice { get; set; }

	[JsonProperty("contract_id")]
	public long ContractId { get; set; }

	[JsonProperty("contract_type")]
	public string ContractType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("current_spot")]
	public decimal? CurrentSpot { get; set; }

	[JsonProperty("current_spot_time")]
	public long CurrentSpotTime { get; set; }

	[JsonProperty("date_expiry")]
	public long DateExpiry { get; set; }

	[JsonProperty("date_start")]
	public long DateStart { get; set; }

	[JsonProperty("entry_spot")]
	public decimal? EntrySpot { get; set; }

	[JsonProperty("entry_spot_time")]
	public long? EntrySpotTime { get; set; }

	[JsonProperty("exit_spot")]
	public decimal? ExitSpot { get; set; }

	[JsonProperty("exit_spot_time")]
	public long? ExitSpotTime { get; set; }

	[JsonProperty("expiry_time")]
	public long ExpiryTime { get; set; }

	[JsonProperty("is_expired")]
	public int IsExpired { get; set; }

	[JsonProperty("is_sold")]
	public int IsSold { get; set; }

	[JsonProperty("is_valid_to_cancel")]
	public int IsValidToCancel { get; set; }

	[JsonProperty("is_valid_to_sell")]
	public int IsValidToSell { get; set; }

	[JsonProperty("longcode")]
	public string LongCode { get; set; }

	[JsonProperty("payout")]
	public decimal? Payout { get; set; }

	[JsonProperty("profit")]
	public decimal? Profit { get; set; }

	[JsonProperty("purchase_time")]
	public long PurchaseTime { get; set; }

	[JsonProperty("sell_price")]
	public decimal? SellPrice { get; set; }

	[JsonProperty("sell_time")]
	public long? SellTime { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("transaction_ids")]
	public DerivTransactionIds TransactionIds { get; set; }

	[JsonProperty("underlying_symbol")]
	public string Symbol { get; set; }

	[JsonProperty("validation_error")]
	public string ValidationError { get; set; }

	[JsonProperty("validation_error_code")]
	public string ValidationErrorCode { get; set; }
}

sealed class DerivBalance
{
	[JsonProperty("balance")]
	public decimal Value { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("loginid")]
	public string LoginId { get; set; }
}

sealed class DerivTransaction
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("contract_id")]
	public long? ContractId { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("date_expiry")]
	public long DateExpiry { get; set; }

	[JsonProperty("longcode")]
	public string LongCode { get; set; }

	[JsonProperty("purchase_time")]
	public long PurchaseTime { get; set; }

	[JsonProperty("transaction_id")]
	public long TransactionId { get; set; }

	[JsonProperty("transaction_time")]
	public long TransactionTime { get; set; }

	[JsonProperty("underlying_symbol")]
	public string Symbol { get; set; }
}

sealed class DerivRestEnvelope<T>
{
	[JsonProperty("data")]
	public T Data { get; set; }
}

sealed class DerivRestAccount
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("group")]
	public string Group { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("account_type")]
	public string AccountType { get; set; }
}

sealed class DerivWebSocketData
{
	[JsonProperty("url")]
	public string Url { get; set; }
}

sealed class DerivRestErrorEnvelope
{
	[JsonProperty("errors")]
	public DerivRestError[] Errors { get; set; }
}

sealed class DerivRestError
{
	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("field")]
	public string Field { get; set; }
}
