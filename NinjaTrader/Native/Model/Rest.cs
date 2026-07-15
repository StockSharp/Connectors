namespace StockSharp.NinjaTrader.Native.Model;

sealed class AccessTokenRequest
{
	public string Name { get; set; }
	public string Password { get; set; }
	public string AppId { get; set; }
	public string AppVersion { get; set; }
	public string DeviceId { get; set; }
	public string Cid { get; set; }
	public string Sec { get; set; }
}

sealed class AccessTokenResponse
{
	public string ErrorText { get; set; }
	public string AccessToken { get; set; }
	[JsonProperty("mdAccessToken")]
	public string MarketDataAccessToken { get; set; }
	public DateTime? ExpirationTime { get; set; }
	public long UserId { get; set; }
	public string Name { get; set; }
	[JsonProperty("hasLive")]
	public bool IsLiveAvailable { get; set; }
}

sealed class NinjaTraderAccount
{
	public long Id { get; set; }
	public string Name { get; set; }
	public long UserId { get; set; }
	[JsonProperty("active")]
	public bool IsActive { get; set; }
}

sealed class NinjaTraderContract
{
	public long Id { get; set; }
	public string Name { get; set; }
	public long ContractMaturityId { get; set; }
}

sealed class NinjaTraderContractMaturity
{
	public long Id { get; set; }
	public long ProductId { get; set; }
	public int ExpirationMonth { get; set; }
	public DateTime ExpirationDate { get; set; }
	public long? UnderlyingId { get; set; }
	public bool IsFront { get; set; }
}

sealed class NinjaTraderProduct
{
	public long Id { get; set; }
	public string Name { get; set; }
	public long CurrencyId { get; set; }
	public NinjaTraderProductTypes ProductType { get; set; }
	public string Description { get; set; }
	public long ExchangeId { get; set; }
	public decimal ValuePerPoint { get; set; }
	public int PriceFormat { get; set; }
	public decimal TickSize { get; set; }
}

sealed class NinjaTraderExchange
{
	public long Id { get; set; }
	public string Name { get; set; }
}

sealed class NinjaTraderOrder
{
	public long Id { get; set; }
	public long AccountId { get; set; }
	public long? ContractId { get; set; }
	public DateTime Timestamp { get; set; }
	public NinjaTraderActions Action { get; set; }
	public NinjaTraderOrderStates OrdStatus { get; set; }
	public long? ParentId { get; set; }
}

sealed class NinjaTraderOrderVersion
{
	public long Id { get; set; }
	public long OrderId { get; set; }
	public int OrderQty { get; set; }
	public NinjaTraderOrderTypes OrderType { get; set; }
	public decimal? Price { get; set; }
	public decimal? StopPrice { get; set; }
	public int? MaxShow { get; set; }
	public NinjaTraderTimeInForces TimeInForce { get; set; }
	public DateTime? ExpireTime { get; set; }
	public string Text { get; set; }
}

sealed class NinjaTraderFill
{
	public long Id { get; set; }
	public long OrderId { get; set; }
	public long ContractId { get; set; }
	public DateTime Timestamp { get; set; }
	public NinjaTraderActions Action { get; set; }
	public int Qty { get; set; }
	public decimal Price { get; set; }
	[JsonProperty("active")]
	public bool IsActive { get; set; }
}

sealed class NinjaTraderPosition
{
	public long Id { get; set; }
	public long AccountId { get; set; }
	public long ContractId { get; set; }
	public DateTime Timestamp { get; set; }
	public int NetPos { get; set; }
	public decimal? NetPrice { get; set; }
	public int Bought { get; set; }
	public decimal BoughtValue { get; set; }
	public int Sold { get; set; }
	public decimal SoldValue { get; set; }
}

sealed class NinjaTraderCashBalance
{
	public long Id { get; set; }
	public long AccountId { get; set; }
	public DateTime Timestamp { get; set; }
	public long CurrencyId { get; set; }
	public decimal Amount { get; set; }
	public decimal? RealizedPnL { get; set; }
	public decimal? WeekRealizedPnL { get; set; }
	public decimal? AmountSOD { get; set; }
}

sealed class PlaceOrderRequest
{
	public string AccountSpec { get; set; }
	public long AccountId { get; set; }
	public string ClOrdId { get; set; }
	public NinjaTraderActions Action { get; set; }
	public string Symbol { get; set; }
	public int OrderQty { get; set; }
	public NinjaTraderOrderTypes OrderType { get; set; }
	public decimal? Price { get; set; }
	public decimal? StopPrice { get; set; }
	public NinjaTraderTimeInForces TimeInForce { get; set; }
	public DateTime? ExpireTime { get; set; }
	public bool IsAutomated { get; set; }
}

sealed class ModifyOrderRequest
{
	public long OrderId { get; set; }
	public string ClOrdId { get; set; }
	public int OrderQty { get; set; }
	public NinjaTraderOrderTypes OrderType { get; set; }
	public decimal? Price { get; set; }
	public decimal? StopPrice { get; set; }
	public NinjaTraderTimeInForces TimeInForce { get; set; }
	public DateTime? ExpireTime { get; set; }
	public bool IsAutomated { get; set; }
}

sealed class CancelOrderRequest
{
	public long OrderId { get; set; }
	public string ClOrdId { get; set; }
	public bool IsAutomated { get; set; }
}

class CommandResult
{
	public string FailureReason { get; set; }
	public string FailureText { get; set; }
	public long? CommandId { get; set; }

	public void ThrowIfError()
	{
		if (!FailureReason.IsEmpty() && !FailureReason.EqualsIgnoreCase("Success"))
			throw new InvalidOperationException(FailureText.IsEmpty(FailureReason));
	}
}

sealed class PlaceOrderResult : CommandResult
{
	public long? OrderId { get; set; }
}
