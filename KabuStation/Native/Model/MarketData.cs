namespace StockSharp.KabuStation.Native.Model;

internal sealed class KabuStationSymbol
{
	[JsonProperty("Symbol")]
	public string Symbol { get; set; }

	[JsonProperty("SymbolName")]
	public string SymbolName { get; set; }

	[JsonProperty("DisplayName")]
	public string DisplayName { get; set; }

	[JsonProperty("Exchange")]
	public int Exchange { get; set; }

	[JsonProperty("ExchangeName")]
	public string ExchangeName { get; set; }

	[JsonProperty("BisCategory")]
	public string BisCategory { get; set; }

	[JsonProperty("TotalMarketValue")]
	public decimal? TotalMarketValue { get; set; }

	[JsonProperty("TotalStocks")]
	public decimal? TotalStocks { get; set; }

	[JsonProperty("TradingUnit")]
	public decimal? TradingUnit { get; set; }

	[JsonProperty("FiscalYearEndBasic")]
	public int? FiscalYearEndBasic { get; set; }

	[JsonProperty("PriceRangeGroup")]
	public string PriceRangeGroup { get; set; }

	[JsonProperty("KCMarginBuy")]
	public bool? IsKabuMarginBuyAllowed { get; set; }

	[JsonProperty("KCMarginSell")]
	public bool? IsKabuMarginSellAllowed { get; set; }

	[JsonProperty("MarginBuy")]
	public bool? IsMarginBuyAllowed { get; set; }

	[JsonProperty("MarginSell")]
	public bool? IsMarginSellAllowed { get; set; }

	[JsonProperty("PerSymbolLimit")]
	public decimal? PerSymbolLimit { get; set; }

	[JsonProperty("UpperLimit")]
	public decimal? UpperLimit { get; set; }

	[JsonProperty("LowerLimit")]
	public decimal? LowerLimit { get; set; }

	[JsonProperty("Underlyer")]
	public string Underlying { get; set; }

	[JsonProperty("DerivMonth")]
	public string DerivativeMonth { get; set; }

	[JsonProperty("TradeStart")]
	public int? TradeStart { get; set; }

	[JsonProperty("TradeEnd")]
	public int? TradeEnd { get; set; }

	[JsonProperty("StrikePrice")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("PutOrCall")]
	public int? PutOrCall { get; set; }

	[JsonProperty("ClearingPrice")]
	public decimal? ClearingPrice { get; set; }
}

internal sealed class KabuStationBoardLevel
{
	[JsonProperty("Price")]
	public decimal? Price { get; set; }

	[JsonProperty("Qty")]
	public decimal? Quantity { get; set; }
}

internal sealed class KabuStationBoard
{
	[JsonProperty("Symbol")]
	public string Symbol { get; set; }
	[JsonProperty("SymbolName")]
	public string SymbolName { get; set; }
	[JsonProperty("Exchange")]
	public int Exchange { get; set; }
	[JsonProperty("ExchangeName")]
	public string ExchangeName { get; set; }
	[JsonProperty("CurrentPrice")]
	public decimal? CurrentPrice { get; set; }
	[JsonProperty("CurrentPriceTime")]
	public string CurrentPriceTime { get; set; }
	[JsonProperty("CurrentPriceChangeStatus")]
	public string CurrentPriceChangeStatus { get; set; }
	[JsonProperty("CurrentPriceStatus")]
	public int? CurrentPriceStatus { get; set; }
	[JsonProperty("CalcPrice")]
	public decimal? CalculatedPrice { get; set; }
	[JsonProperty("PreviousClose")]
	public decimal? PreviousClose { get; set; }
	[JsonProperty("PreviousCloseTime")]
	public string PreviousCloseTime { get; set; }
	[JsonProperty("ChangePreviousClose")]
	public decimal? ChangePreviousClose { get; set; }
	[JsonProperty("ChangePreviousClosePer")]
	public decimal? ChangePreviousClosePercent { get; set; }
	[JsonProperty("OpeningPrice")]
	public decimal? OpenPrice { get; set; }
	[JsonProperty("OpeningPriceTime")]
	public string OpenPriceTime { get; set; }
	[JsonProperty("HighPrice")]
	public decimal? HighPrice { get; set; }
	[JsonProperty("HighPriceTime")]
	public string HighPriceTime { get; set; }
	[JsonProperty("LowPrice")]
	public decimal? LowPrice { get; set; }
	[JsonProperty("LowPriceTime")]
	public string LowPriceTime { get; set; }
	[JsonProperty("TradingVolume")]
	public decimal? TradingVolume { get; set; }
	[JsonProperty("TradingVolumeTime")]
	public string TradingVolumeTime { get; set; }
	[JsonProperty("VWAP")]
	public decimal? Vwap { get; set; }
	[JsonProperty("TradingValue")]
	public decimal? TradingValue { get; set; }

	// The official API documents Bid as the sell quote and Ask as the buy quote.
	[JsonProperty("BidQty")]
	public decimal? SellQuoteQuantity { get; set; }
	[JsonProperty("BidPrice")]
	public decimal? SellQuotePrice { get; set; }
	[JsonProperty("BidTime")]
	public string SellQuoteTime { get; set; }
	[JsonProperty("BidSign")]
	public string SellQuoteSign { get; set; }
	[JsonProperty("AskQty")]
	public decimal? BuyQuoteQuantity { get; set; }
	[JsonProperty("AskPrice")]
	public decimal? BuyQuotePrice { get; set; }
	[JsonProperty("AskTime")]
	public string BuyQuoteTime { get; set; }
	[JsonProperty("AskSign")]
	public string BuyQuoteSign { get; set; }

	[JsonProperty("MarketOrderSellQty")]
	public decimal? MarketSellQuantity { get; set; }
	[JsonProperty("MarketOrderBuyQty")]
	public decimal? MarketBuyQuantity { get; set; }
	[JsonProperty("OverSellQty")]
	public decimal? OverSellQuantity { get; set; }
	[JsonProperty("UnderBuyQty")]
	public decimal? UnderBuyQuantity { get; set; }
	[JsonProperty("TotalMarketValue")]
	public decimal? TotalMarketValue { get; set; }
	[JsonProperty("ClearingPrice")]
	public decimal? ClearingPrice { get; set; }
	[JsonProperty("IV")]
	public decimal? ImpliedVolatility { get; set; }
	[JsonProperty("Gamma")]
	public decimal? Gamma { get; set; }
	[JsonProperty("Theta")]
	public decimal? Theta { get; set; }
	[JsonProperty("Vega")]
	public decimal? Vega { get; set; }
	[JsonProperty("Delta")]
	public decimal? Delta { get; set; }
	[JsonProperty("SecurityType")]
	public int NativeSecurityType { get; set; }

	[JsonProperty("Sell1")]
	public KabuStationBoardLevel Sell1 { get; set; }
	[JsonProperty("Sell2")]
	public KabuStationBoardLevel Sell2 { get; set; }
	[JsonProperty("Sell3")]
	public KabuStationBoardLevel Sell3 { get; set; }
	[JsonProperty("Sell4")]
	public KabuStationBoardLevel Sell4 { get; set; }
	[JsonProperty("Sell5")]
	public KabuStationBoardLevel Sell5 { get; set; }
	[JsonProperty("Sell6")]
	public KabuStationBoardLevel Sell6 { get; set; }
	[JsonProperty("Sell7")]
	public KabuStationBoardLevel Sell7 { get; set; }
	[JsonProperty("Sell8")]
	public KabuStationBoardLevel Sell8 { get; set; }
	[JsonProperty("Sell9")]
	public KabuStationBoardLevel Sell9 { get; set; }
	[JsonProperty("Sell10")]
	public KabuStationBoardLevel Sell10 { get; set; }
	[JsonProperty("Buy1")]
	public KabuStationBoardLevel Buy1 { get; set; }
	[JsonProperty("Buy2")]
	public KabuStationBoardLevel Buy2 { get; set; }
	[JsonProperty("Buy3")]
	public KabuStationBoardLevel Buy3 { get; set; }
	[JsonProperty("Buy4")]
	public KabuStationBoardLevel Buy4 { get; set; }
	[JsonProperty("Buy5")]
	public KabuStationBoardLevel Buy5 { get; set; }
	[JsonProperty("Buy6")]
	public KabuStationBoardLevel Buy6 { get; set; }
	[JsonProperty("Buy7")]
	public KabuStationBoardLevel Buy7 { get; set; }
	[JsonProperty("Buy8")]
	public KabuStationBoardLevel Buy8 { get; set; }
	[JsonProperty("Buy9")]
	public KabuStationBoardLevel Buy9 { get; set; }
	[JsonProperty("Buy10")]
	public KabuStationBoardLevel Buy10 { get; set; }

	public KabuStationBoardLevel[] GetSells()
		=> [Sell1, Sell2, Sell3, Sell4, Sell5, Sell6, Sell7, Sell8, Sell9, Sell10];

	public KabuStationBoardLevel[] GetBuys()
		=> [Buy1, Buy2, Buy3, Buy4, Buy5, Buy6, Buy7, Buy8, Buy9, Buy10];
}
