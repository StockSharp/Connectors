namespace StockSharp.DeltaExchangeIndia.Native.Model;

sealed class DeltaBalance
{
	public string Asset { get; init; }

	public decimal Current { get; init; }

	public decimal Available { get; init; }

	public decimal Blocked { get; init; }
}

sealed class DeltaPosition
{
	public int ProductId { get; init; }

	public string Symbol { get; init; }

	public decimal Size { get; init; }

	public decimal EntryPrice { get; init; }

	public decimal LiquidationPrice { get; init; }

	public decimal Margin { get; init; }

	public decimal RealizedPnl { get; init; }

	public decimal UnrealizedPnl { get; init; }
}

sealed class DeltaOrder
{
	public long Id { get; init; }

	public string ClientOrderId { get; init; }

	public int ProductId { get; init; }

	public string Symbol { get; init; }

	public Sides Side { get; init; }

	public OrderTypes OrderType { get; init; }

	public OrderStates State { get; init; }

	public decimal Price { get; init; }

	public decimal StopPrice { get; init; }

	public decimal Volume { get; init; }

	public decimal Balance { get; init; }

	public decimal AveragePrice { get; init; }

	public bool ReduceOnly { get; init; }

	public TimeInForce? TimeInForce { get; init; }

	public DateTime CreatedAt { get; init; }

	public DateTime UpdatedAt { get; init; }
}

sealed class DeltaFill
{
	public string Id { get; init; }

	public long OrderId { get; init; }

	public string ClientOrderId { get; init; }

	public int ProductId { get; init; }

	public string Symbol { get; init; }

	public Sides Side { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public decimal Commission { get; init; }

	public string CommissionCurrency { get; init; }

	public DateTime Time { get; init; }
}

sealed class DeltaWsMessage
{
	public string Type { get; init; }

	public DeltaTicker[] Tickers { get; init; } = [];

	public DeltaBook Book { get; init; }

	public DeltaTrade Trade { get; init; }

	public DeltaCandle Candle { get; init; }

	public DeltaOrder[] Orders { get; init; } = [];

	public DeltaFill Fill { get; init; }

	public DeltaPosition[] Positions { get; init; } = [];
}
