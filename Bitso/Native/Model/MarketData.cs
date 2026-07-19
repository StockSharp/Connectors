namespace StockSharp.Bitso.Native.Model;

sealed class BitsoBookQuery
{
	public string Book { get; init; }
}

sealed class BitsoOrderBookQuery
{
	public string Book { get; init; }
	public bool IsAggregate { get; init; } = true;
}

sealed class BitsoTradesQuery
{
	public string Book { get; init; }
	public int Limit { get; init; }
	public string Marker { get; init; }
	public bool IsAscending { get; init; }
}

class BitsoFeeRate
{
	[JsonProperty("maker")]
	public decimal Maker { get; set; }

	[JsonProperty("taker")]
	public decimal Taker { get; set; }
}

sealed class BitsoFeeTier : BitsoFeeRate
{
	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

sealed class BitsoFees
{
	[JsonProperty("flat_rate")]
	public BitsoFeeRate FlatRate { get; set; }

	[JsonProperty("structure")]
	public BitsoFeeTier[] Structure { get; set; }
}

sealed class BitsoBook
{
	[JsonProperty("book")]
	public string Name { get; set; }

	[JsonProperty("minimum_amount")]
	public decimal MinimumAmount { get; set; }

	[JsonProperty("maximum_amount")]
	public decimal MaximumAmount { get; set; }

	[JsonProperty("minimum_price")]
	public decimal MinimumPrice { get; set; }

	[JsonProperty("maximum_price")]
	public decimal MaximumPrice { get; set; }

	[JsonProperty("minimum_value")]
	public decimal MinimumValue { get; set; }

	[JsonProperty("maximum_value")]
	public decimal MaximumValue { get; set; }

	[JsonProperty("tick_size")]
	public decimal TickSize { get; set; }

	[JsonProperty("margin_enabled")]
	public bool IsMarginEnabled { get; set; }

	[JsonProperty("default_chart")]
	public string DefaultChart { get; set; }

	[JsonProperty("fees")]
	public BitsoFees Fees { get; set; }
}

sealed class BitsoTicker
{
	[JsonProperty("book")]
	public string Book { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("last")]
	public decimal Last { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("ask")]
	public decimal Ask { get; set; }

	[JsonProperty("bid")]
	public decimal Bid { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("vwap")]
	public decimal VolumeWeightedPrice { get; set; }

	[JsonProperty("change_24")]
	public decimal Change24Hours { get; set; }
}

sealed class BitsoOrderBookLevel
{
	[JsonProperty("book")]
	public string Book { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("oid")]
	public string OrderId { get; set; }
}

sealed class BitsoOrderBook
{
	[JsonProperty("asks")]
	public BitsoOrderBookLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public BitsoOrderBookLevel[] Bids { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }

	[JsonProperty("sequence")]
	public string Sequence { get; set; }
}

sealed class BitsoPublicTrade
{
	[JsonProperty("book")]
	public string Book { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("maker_side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitsoSides MakerSide { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("tid")]
	public string TradeId { get; set; }
}
