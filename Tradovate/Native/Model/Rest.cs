namespace StockSharp.Tradovate.Native.Model;

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
	public DateTime? ExpirationTime { get; set; }
	public long UserId { get; set; }
	public string Name { get; set; }
	[JsonProperty("hasLive")]
	public bool IsLiveAvailable { get; set; }
}

sealed class TradovateAccount
{
	public long Id { get; set; }
	public string Name { get; set; }
	public long UserId { get; set; }
	[JsonProperty("active")]
	public bool IsActive { get; set; }
}

sealed class TradovateContract
{
	public long Id { get; set; }
	public string Name { get; set; }
	public long ContractMaturityId { get; set; }
}

sealed class TradovateContractMaturity
{
	public long Id { get; set; }
	public long ProductId { get; set; }
	public int ExpirationMonth { get; set; }
	public DateTime ExpirationDate { get; set; }
	public long? UnderlyingId { get; set; }
	public bool IsFront { get; set; }
}

sealed class TradovateProduct
{
	public long Id { get; set; }
	public string Name { get; set; }
	public long CurrencyId { get; set; }
	public TradovateProductTypes ProductType { get; set; }
	public string Description { get; set; }
	public long ExchangeId { get; set; }
	public decimal ValuePerPoint { get; set; }
	public int PriceFormat { get; set; }
	public decimal TickSize { get; set; }
}

sealed class TradovateExchange
{
	public long Id { get; set; }
	public string Name { get; set; }
}

sealed class TradovateOrder
{
	public long Id { get; set; }
	public long AccountId { get; set; }
	public long? ContractId { get; set; }
	public DateTime Timestamp { get; set; }
	public TradovateActions Action { get; set; }
	public TradovateOrderStates OrdStatus { get; set; }
	public long? ParentId { get; set; }
}

sealed class TradovateOrderVersion
{
	public long Id { get; set; }
	public long OrderId { get; set; }
	public int OrderQty { get; set; }
	public TradovateOrderTypes OrderType { get; set; }
	public decimal? Price { get; set; }
	public decimal? StopPrice { get; set; }
	public int? MaxShow { get; set; }
	public TradovateTimeInForces TimeInForce { get; set; }
	public DateTime? ExpireTime { get; set; }
	public string Text { get; set; }
}

sealed class TradovateFill
{
	public long Id { get; set; }
	public long OrderId { get; set; }
	public long ContractId { get; set; }
	public DateTime Timestamp { get; set; }
	public TradovateActions Action { get; set; }
	public int Qty { get; set; }
	public decimal Price { get; set; }
	[JsonProperty("active")]
	public bool IsActive { get; set; }
}

sealed class TradovatePosition
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

sealed class TradovateCashBalance
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
	public TradovateActions Action { get; set; }
	public string Symbol { get; set; }
	public int OrderQty { get; set; }
	public TradovateOrderTypes OrderType { get; set; }
	public decimal? Price { get; set; }
	public decimal? StopPrice { get; set; }
	public TradovateTimeInForces TimeInForce { get; set; }
	public DateTime? ExpireTime { get; set; }
	public bool IsAutomated { get; set; }
}

sealed class ModifyOrderRequest
{
	public long OrderId { get; set; }
	public string ClOrdId { get; set; }
	public int OrderQty { get; set; }
	public TradovateOrderTypes OrderType { get; set; }
	public decimal? Price { get; set; }
	public decimal? StopPrice { get; set; }
	public TradovateTimeInForces TimeInForce { get; set; }
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
