namespace StockSharp.Webull.Native.Model;

sealed class InstrumentListQuery
{
	[JsonProperty("symbols")]
	public string Symbols { get; set; }

	[JsonProperty("category")]
	public WebullInstrumentCategories Category { get; set; }
}

sealed class AccountQuery
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }
}

sealed class InstrumentInfo
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

sealed class AccountInfo
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }
}

sealed class AccountBalance
{
	[JsonProperty("total_cash_balance")]
	public decimal? TotalCashBalance { get; set; }

	[JsonProperty("account_currency_assets")]
	public AccountCurrencyAsset[] CurrencyAssets { get; set; }

	[JsonProperty("total_unrealized_profit_loss")]
	public decimal? UnrealizedProfitLoss { get; set; }
}

sealed class AccountCurrencyAsset
{
	[JsonProperty("buying_power")]
	public decimal? BuyingPower { get; set; }
}

sealed class AccountPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("cost_price")]
	public decimal? CostPrice { get; set; }

	[JsonProperty("last_price")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("unrealized_profit_loss")]
	public decimal? UnrealizedProfitLoss { get; set; }
}

sealed class StockOrderRequest
{
	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("combo_type")]
	public WebullComboTypes ComboType { get; set; }

	[JsonProperty("instrument_type")]
	public WebullInstrumentTypes InstrumentType { get; set; }

	[JsonProperty("entrust_type")]
	public WebullEntrustTypes EntrustType { get; set; }

	[JsonProperty("support_trading_session")]
	public WebullTradingSessions TradingSession { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("market")]
	public WebullMarkets Market { get; set; }

	[JsonProperty("side")]
	public WebullSides Side { get; set; }

	[JsonProperty("order_type")]
	public WebullOrderTypes OrderType { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("time_in_force")]
	public WebullTimeInForces TimeInForce { get; set; }

	[JsonProperty("limit_price", NullValueHandling = NullValueHandling.Ignore)]
	public string LimitPrice { get; set; }
}

sealed class PlaceOrderRequest
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("new_orders")]
	public StockOrderRequest[] NewOrders { get; set; }
}

sealed class CancelOrderRequest
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }
}

sealed class TradeEventPayload
{
	[JsonProperty("request_id")]
	public string RequestId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("order_status")]
	public WebullOrderStatuses OrderStatus { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}
