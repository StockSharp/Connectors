namespace StockSharp.ZondaCrypto.Native.Model;

sealed class ZondaCryptoWallet
{
	public string Id { get; init; }

	public string Currency { get; init; }

	public string Name { get; init; }

	public decimal Available { get; init; }

	public decimal Locked { get; init; }

	public decimal Total { get; init; }
}

sealed class ZondaCryptoOffer
{
	public string Id { get; init; }

	public string MarketCode { get; init; }

	public Sides Side { get; init; }

	public OrderTypes OrderType { get; init; }

	public TimeInForce TimeInForce { get; init; }

	public bool PostOnly { get; init; }

	public OrderStates State { get; init; }

	public DateTime? CreatedAt { get; init; }

	public decimal Price { get; init; }

	public decimal OriginalAmount { get; init; }

	public decimal RemainingAmount { get; init; }
}

sealed class ZondaCryptoPlaceOrderRequest
{
	public string MarketCode { get; init; }

	public Sides Side { get; init; }

	public OrderTypes OrderType { get; init; }

	public TimeInForce? TimeInForce { get; init; }

	public bool PostOnly { get; init; }

	public decimal Price { get; init; }

	public decimal Amount { get; init; }
}

sealed class ZondaCryptoPrivateTrade
{
	public string Id { get; init; }

	public string MarketCode { get; init; }

	public DateTime Time { get; init; }

	public decimal Volume { get; init; }

	public decimal Price { get; init; }

	public Sides Side { get; init; }

	public bool IsTaker { get; init; }
}
