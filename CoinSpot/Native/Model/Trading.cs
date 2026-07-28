namespace StockSharp.CoinSpot.Native.Model;

sealed class CoinSpotBalance
{
	public string Currency { get; init; }

	public decimal Balance { get; init; }

	public decimal? Available { get; init; }

	public decimal AudBalance { get; init; }

	public decimal Rate { get; init; }

	public decimal Blocked
		=> Available is decimal available
			? (Balance - available).Max(0)
			: 0;
}

sealed class CoinSpotOrder
{
	public string Id { get; init; }

	public string Coin { get; init; }

	public string Market { get; init; }

	public decimal Amount { get; init; }

	public decimal Rate { get; init; }

	public decimal? Total { get; init; }

	public DateTime? CreatedAt { get; init; }

	public DateTime? CompletedAt { get; init; }

	public Sides Side { get; init; }

	public OrderStates State { get; init; }

	public OrderTypes OrderType { get; init; } = OrderTypes.Limit;

	public decimal RemainingVolume
		=> State == OrderStates.Active ? Amount : 0;
}

sealed class CoinSpotPlaceOrderRequest
{
	public string Coin { get; init; }

	public string Market { get; init; }

	public Sides Side { get; init; }

	public OrderTypes OrderType { get; init; }

	public decimal Amount { get; init; }

	public decimal Price { get; init; }
}

sealed class CoinSpotPlaceOrderResult
{
	public string Id { get; init; }

	public string Coin { get; init; }

	public string Market { get; init; }

	public decimal Amount { get; init; }

	public decimal Rate { get; init; }
}
