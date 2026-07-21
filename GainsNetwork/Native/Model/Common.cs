namespace StockSharp.GainsNetwork.Native.Model;

enum GainsTradeTypes
{
	Market,
	Limit,
	Stop,
}

enum GainsTradingStates
{
	Activated,
	CloseOnly,
	Paused,
}

sealed class GainsMarket
{
	public int PairIndex { get; init; }
	public string Symbol { get; init; }
	public string BaseAsset { get; init; }
	public string QuoteAsset { get; init; }
	public string Group { get; init; }
	public decimal MinimumLeverage { get; init; }
	public decimal MaximumLeverage { get; init; }
	public decimal SpreadPercentage { get; init; }
	public decimal MinimumPositionSizeUsd { get; init; }
	public decimal VolumeStep { get; init; }
	public bool IsEnabled { get; init; }
	public bool IsMarketOpen { get; init; }
}

sealed class GainsMarketPrice
{
	public int PairIndex { get; init; }
	public decimal MarkPrice { get; set; }
	public decimal IndexPrice { get; set; }
	public decimal OpenPrice { get; set; }
	public decimal HighPrice { get; set; }
	public decimal LowPrice { get; set; }
	public decimal ClosePrice { get; set; }
	public DateTime Time { get; set; }
}
