namespace StockSharp.LMAX.Native.Model;

class MarketDataEntry
{
	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("quantity")]
	public double Quantity { get; set; }
}

class OrderBookSnapshot
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("bids")]
	public MarketDataEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public MarketDataEntry[] Asks { get; set; }
}

class HistoricClosingPrice
{
	[JsonProperty("closing_date")]
	public string ClosingDate { get; set; }

	[JsonProperty("closing_price")]
	public double? ClosingPrice { get; set; }

	[JsonProperty("settlement_price")]
	public double? SettlementPrice { get; set; }
}

class HistoricClosingPricesResponse
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("closing_prices")]
	public HistoricClosingPrice[] ClosingPrices { get; set; }
}
