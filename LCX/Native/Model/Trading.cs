namespace StockSharp.LCX.Native.Model;

sealed class LcxBalance
{
	public string Currency { get; init; }

	public string Name { get; init; }

	public decimal Available { get; init; }

	public decimal Blocked { get; init; }

	public decimal Total { get; init; }
}

sealed class LcxOrder
{
	public string Id { get; init; }

	public string ClientOrderId { get; init; }

	public string Symbol { get; init; }

	public Sides Side { get; init; }

	public OrderTypes OrderType { get; init; }

	public OrderStates State { get; init; }

	public DateTime CreatedAt { get; init; }

	public DateTime UpdatedAt { get; init; }

	public decimal Price { get; init; }

	public decimal Amount { get; init; }

	public decimal Filled { get; init; }

	public decimal RemainingAmount
		=> Math.Max(0, Amount - Filled);

	public decimal Fee { get; init; }
}

sealed class LcxUserTrade
{
	public string Id { get; init; }

	public string OrderId { get; init; }

	public string Symbol { get; init; }

	public Sides Side { get; init; }

	public DateTime Time { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public decimal Fee { get; init; }

	public string FeeCurrency { get; init; }
}
