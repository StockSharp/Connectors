namespace StockSharp.Kucoin.Native.Futures.Model;

class Position
{
	[JsonProperty("realisedGrossPnl")]
	public double? RealisedGrossPnl { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("crossMode")]
	public bool? CrossMode { get; set; }

	[JsonProperty("liquidationPrice")]
	public double? LiquidationPrice { get; set; }

	[JsonProperty("posLoss")]
	public double? PosLoss { get; set; }

	[JsonProperty("avgEntryPrice")]
	public double? AvgEntryPrice { get; set; }

	[JsonProperty("unrealisedPnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("posMargin")]
	public double? PosMargin { get; set; }

	[JsonProperty("autoDeposit")]
	public bool AutoDeposit { get; set; }

	[JsonProperty("riskLimit")]
	public int RiskLimit { get; set; }

	[JsonProperty("unrealisedCost")]
	public double? UnrealisedCost { get; set; }

	[JsonProperty("posComm")]
	public double? PosComm { get; set; }

	[JsonProperty("posMaint")]
	public double? PosMaint { get; set; }

	[JsonProperty("posCost")]
	public double? PosCost { get; set; }

	[JsonProperty("maintMarginReq")]
	public double? MaintMarginReq { get; set; }

	[JsonProperty("bankruptPrice")]
	public double? BankruptPrice { get; set; }

	[JsonProperty("realisedCost")]
	public double? RealisedCost { get; set; }

	[JsonProperty("markValue")]
	public double? MarkValue { get; set; }

	[JsonProperty("posInit")]
	public double? PosInit { get; set; }

	[JsonProperty("realisedPnl")]
	public double? RealisedPnl { get; set; }

	[JsonProperty("maintMargin")]
	public double? MaintMargin { get; set; }

	[JsonProperty("realLeverage")]
	public double? RealLeverage { get; set; }

	[JsonProperty("changeReason")]
	public string ChangeReason { get; set; }

	[JsonProperty("currentCost")]
	public double? CurrentCost { get; set; }

	[JsonProperty("openingTimestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime OpeningTimestamp { get; set; }

	[JsonProperty("currentQty")]
	public double? CurrentQty { get; set; }

	[JsonProperty("delevPercentage")]
	public double? DelevPercentage { get; set; }

	[JsonProperty("currentComm")]
	public double? CurrentComm { get; set; }

	[JsonProperty("realisedGrossCost")]
	public double? RealisedGrossCost { get; set; }

	[JsonProperty("isOpen")]
	public bool? IsOpen { get; set; }

	[JsonProperty("posCross")]
	public double? PosCross { get; set; }

	[JsonProperty("currentTimestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CurrentTimestamp { get; set; }

	[JsonProperty("unrealisedRoePcnt")]
	public double? UnrealisedRoePcnt { get; set; }

	[JsonProperty("unrealisedPnlPcnt")]
	public double? UnrealisedPnlPcnt { get; set; }

	[JsonProperty("settleCurrency")]
	public string SettleCurrency { get; set; }
}
