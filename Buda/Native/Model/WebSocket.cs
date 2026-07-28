namespace StockSharp.Buda.Native.Model;

sealed class BudaBookChange
{
	public Sides Side { get; init; }

	public decimal Price { get; init; }

	public decimal Delta { get; init; }
}

sealed class BudaWsMessage
{
	public string Event { get; init; }

	public string MarketId { get; init; }

	public DateTime? Time { get; init; }

	public BudaTrade Trade { get; init; }

	public BudaOrderBook OrderBook { get; init; }

	public BudaBookChange Change { get; init; }

	public BudaBalance Balance { get; init; }

	public BudaOrder Order { get; init; }
}
