namespace StockSharp.Questrade.Native.Model;

sealed class QuestradeAccountsResponse
{
	[JsonProperty("accounts")]
	public QuestradeAccount[] Accounts { get; set; }
}

sealed class QuestradeAccount
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("number")]
	public string Number { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("isPrimary")]
	public bool IsPrimary { get; set; }

	[JsonProperty("isBilling")]
	public bool IsBilling { get; set; }

	[JsonProperty("clientAccountType")]
	public string ClientAccountType { get; set; }

	[JsonProperty("userId")]
	public long? UserId { get; set; }
}

sealed class QuestradeBalancesResponse
{
	[JsonProperty("perCurrencyBalances")]
	public QuestradeBalance[] PerCurrencyBalances { get; set; }

	[JsonProperty("combinedBalances")]
	public QuestradeBalance[] CombinedBalances { get; set; }
}

sealed class QuestradeBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("cash")]
	public decimal? Cash { get; set; }

	[JsonProperty("marketValue")]
	public decimal? MarketValue { get; set; }

	[JsonProperty("totalEquity")]
	public decimal? TotalEquity { get; set; }

	[JsonProperty("buyingPower")]
	public decimal? BuyingPower { get; set; }

	[JsonProperty("maintenanceExcess")]
	public decimal? MaintenanceExcess { get; set; }

	[JsonProperty("isRealTime")]
	public bool? IsRealTime { get; set; }
}

sealed class QuestradePositionsResponse
{
	[JsonProperty("positions")]
	public QuestradePosition[] Positions { get; set; }
}

sealed class QuestradePosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolId")]
	public long SymbolId { get; set; }

	[JsonProperty("openQuantity")]
	public decimal OpenQuantity { get; set; }

	[JsonProperty("closedQuantity")]
	public decimal? ClosedQuantity { get; set; }

	[JsonProperty("currentMarketValue")]
	public decimal? CurrentMarketValue { get; set; }

	[JsonProperty("currentPrice")]
	public decimal? CurrentPrice { get; set; }

	[JsonProperty("averageEntryPrice")]
	public decimal? AverageEntryPrice { get; set; }

	[JsonProperty("closedPnl")]
	public decimal? ClosedPnL { get; set; }

	[JsonProperty("openPnl")]
	public decimal? OpenPnL { get; set; }

	[JsonProperty("totalCost")]
	public decimal? TotalCost { get; set; }

	[JsonProperty("isRealTime")]
	public bool? IsRealTime { get; set; }

	[JsonProperty("isUnderReorg")]
	public bool? IsUnderReorganization { get; set; }
}
