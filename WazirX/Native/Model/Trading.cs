namespace StockSharp.WazirX.Native.Model;

sealed class WazirXBalance
{
	public string Asset { get; init; }

	public decimal Available { get; init; }

	public decimal Locked { get; init; }

	public decimal ReservedFee { get; init; }
}

sealed class WazirXOrder
{
	public long Id { get; init; }

	public string ClientOrderId { get; init; }

	public string Symbol { get; init; }

	public decimal Price { get; init; }

	public decimal StopPrice { get; init; }

	public decimal OriginalVolume { get; init; }

	public decimal ExecutedVolume { get; init; }

	public decimal RemainingVolume
		=> Math.Max(0, OriginalVolume - ExecutedVolume);

	public OrderStates State { get; init; }

	public OrderTypes OrderType { get; init; }

	public Sides Side { get; init; }

	public DateTime CreatedAt { get; init; }

	public DateTime UpdatedAt { get; init; }
}

sealed class WazirXUserTrade
{
	public long Id { get; init; }

	public long OrderId { get; init; }

	public string ClientOrderId { get; init; }

	public string Symbol { get; init; }

	public decimal Fee { get; init; }

	public string FeeCurrency { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public Sides Side { get; init; }

	public DateTime Time { get; init; }
}

sealed class WazirXAuthToken
{
	public string Key { get; init; }

	public TimeSpan Lifetime { get; init; }
}
