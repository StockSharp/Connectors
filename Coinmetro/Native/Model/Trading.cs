namespace StockSharp.Coinmetro.Native.Model;

sealed class CoinmetroWallet
{
	public string Id { get; init; }

	public string Currency { get; init; }

	public string Label { get; init; }

	public decimal Total { get; init; }

	public decimal Reserved { get; init; }

	public decimal Available => Total - Reserved;
}

sealed class CoinmetroFill
{
	public string Id { get; init; }

	public string OrderId { get; init; }

	public string Pair { get; init; }

	public DateTime Time { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public Sides Side { get; init; }
}

sealed class CoinmetroOrder
{
	public string Id { get; init; }

	public string Pair { get; init; }

	public string BuyingCurrency { get; init; }

	public string SellingCurrency { get; init; }

	public Sides Side { get; init; }

	public OrderTypes OrderType { get; init; }

	public TimeInForce TimeInForce { get; init; }

	public OrderStates State { get; init; }

	public DateTime CreatedAt { get; init; }

	public DateTime? CompletedAt { get; init; }

	public decimal Price { get; init; }

	public decimal OriginalAmount { get; init; }

	public decimal RemainingAmount { get; init; }

	public decimal Fees { get; init; }

	public CoinmetroFill[] Fills { get; init; } = [];
}
