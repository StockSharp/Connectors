namespace StockSharp.BigOne.Native.Model;

enum BigOneMarketKind
{
	Spot,
	Contract,
}

sealed class BigOneResponse<TData>
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("msg")]
	private string AlternateMessage
	{
		set
		{
			if (!value.IsEmpty())
				Message = value;
		}
	}

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("value")]
	public TData Value { get; set; }
}

abstract class BigOneSymbol
{
	public abstract BigOneMarketKind Kind { get; }
	public abstract string Pair { get; }
	public abstract string SecurityCode { get; }
	public abstract string Base { get; }
	public abstract string Quote { get; }
	public abstract int AmountPrecision { get; }
	public abstract int QuotePrecision { get; }
	public abstract decimal? PriceStep { get; }
	public abstract decimal? VolumeStep { get; }
	public virtual decimal? MinimumAmount => null;
	public virtual decimal? MaximumAmount => null;
	public virtual bool IsMaintenance => false;
	public bool IsContract => Kind == BigOneMarketKind.Contract;
}

sealed class BigOneAsset
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

sealed class BigOnePriceLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("quantity")]
	private decimal Quantity
	{
		set => Amount = value;
	}

	[JsonProperty("orderCount")]
	public long OrderCount { get; set; }

	[JsonProperty("order_count")]
	private long RestOrderCount
	{
		set => OrderCount = value;
	}
}

sealed class BigOneBestPrices
{
	[JsonProperty("ask")]
	public decimal Ask { get; set; }

	[JsonProperty("bid")]
	public decimal Bid { get; set; }
}
