namespace StockSharp.Buda.Native.Model;

sealed class BudaAccount
{
	public string Id { get; init; }

	public string PubSubKey { get; init; }
}

sealed class BudaBalance
{
	public string Currency { get; init; }

	public decimal Amount { get; init; }

	public decimal Available { get; init; }

	public decimal Frozen { get; init; }

	public decimal PendingWithdrawal { get; init; }

	public decimal Blocked
		=> Frozen + PendingWithdrawal;
}

sealed class BudaOrder
{
	public string Id { get; init; }

	public string ClientId { get; init; }

	public string MarketId { get; init; }

	public Sides Side { get; init; }

	public OrderTypes OrderType { get; init; }

	public TimeInForce TimeInForce { get; init; }

	public bool PostOnly { get; init; }

	public OrderStates State { get; init; }

	public DateTime? CreatedAt { get; init; }

	public decimal Price { get; init; }

	public decimal OriginalAmount { get; init; }

	public decimal RemainingAmount { get; init; }

	public decimal TradedAmount { get; init; }

	public decimal PaidFee { get; init; }

	public string FeeCurrency { get; init; }
}

sealed class BudaPlaceOrderRequest
{
	public string MarketId { get; init; }

	public Sides Side { get; init; }

	public OrderTypes OrderType { get; init; }

	public TimeInForce? TimeInForce { get; init; }

	public bool PostOnly { get; init; }

	public decimal Price { get; init; }

	public decimal Amount { get; init; }

	public string ClientId { get; init; }
}
