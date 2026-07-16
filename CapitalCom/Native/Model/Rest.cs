namespace StockSharp.CapitalCom.Native.Model;

internal sealed class CapitalComSession
{
	public string AccountId { get; init; }
	public string Currency { get; init; }
	public string StreamingUrl { get; init; }
	public string Cst { get; init; }
	public string SecurityToken { get; init; }
}

internal sealed class CapitalComLoginRequest
{
	[JsonProperty("identifier")]
	public string Identifier { get; set; }

	[JsonProperty("password")]
	public string Password { get; set; }

	[JsonProperty("encryptedPassword")]
	public bool IsEncryptedPassword { get; set; }
}

internal sealed class CapitalComAccountSwitchRequest
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }
}

internal sealed class CapitalComEncryptionKey
{
	[JsonProperty("encryptionKey")]
	public string EncryptionKey { get; set; }

	[JsonProperty("timeStamp")]
	public long TimeStamp { get; set; }
}

internal sealed class CapitalComLoginResponse
{
	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("accountInfo")]
	public CapitalComBalance AccountInfo { get; set; }

	[JsonProperty("currencyIsoCode")]
	public string CurrencyIsoCode { get; set; }

	[JsonProperty("currentAccountId")]
	public string CurrentAccountId { get; set; }

	[JsonProperty("streamingHost")]
	public string StreamingHost { get; set; }

	[JsonProperty("streamEndpoint")]
	public string StreamEndpoint { get; set; }

	[JsonProperty("accounts")]
	public CapitalComLoginAccount[] Accounts { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }
}

internal sealed class CapitalComLoginAccount
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("accountName")]
	public string AccountName { get; set; }

	[JsonProperty("preferred")]
	public bool IsPreferred { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }
}

internal sealed class CapitalComAccountsResponse
{
	[JsonProperty("accounts")]
	public CapitalComAccount[] Accounts { get; set; }
}

internal sealed class CapitalComAccount
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("accountName")]
	public string AccountName { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("preferred")]
	public bool IsPreferred { get; set; }

	[JsonProperty("balance")]
	public CapitalComBalance Balance { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}

internal sealed class CapitalComBalance
{
	[JsonProperty("balance")]
	public decimal? Value { get; set; }

	[JsonProperty("deposit")]
	public decimal? Deposit { get; set; }

	[JsonProperty("profitLoss")]
	public decimal? ProfitLoss { get; set; }

	[JsonProperty("available")]
	public decimal? Available { get; set; }
}

internal sealed class CapitalComMarketsResponse
{
	[JsonProperty("markets")]
	public CapitalComMarketSummary[] Markets { get; set; }

	[JsonProperty("marketDetails")]
	public CapitalComMarketDetails[] MarketDetails { get; set; }
}

internal sealed class CapitalComMarketSummary
{
	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instrumentName")]
	public string InstrumentName { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("marketStatus")]
	public string MarketStatus { get; set; }

	[JsonProperty("lotSize")]
	public decimal? LotSize { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("percentageChange")]
	public decimal? PercentageChange { get; set; }

	[JsonProperty("netChange")]
	public decimal? NetChange { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("offer")]
	public decimal? Offer { get; set; }

	[JsonProperty("updateTimeUTC")]
	public string UpdateTimeUtc { get; set; }

	[JsonProperty("updateTime")]
	public string UpdateTime { get; set; }

	[JsonProperty("streamingPricesAvailable")]
	public bool IsStreamingPricesAvailable { get; set; }

	[JsonProperty("pipPosition")]
	public int? PipPosition { get; set; }

	[JsonProperty("tickSize")]
	public decimal? TickSize { get; set; }
}

internal sealed class CapitalComMarketDetails
{
	[JsonProperty("instrument")]
	public CapitalComInstrument Instrument { get; set; }

	[JsonProperty("dealingRules")]
	public CapitalComDealingRules DealingRules { get; set; }

	[JsonProperty("snapshot")]
	public CapitalComMarketSnapshot Snapshot { get; set; }
}

internal sealed class CapitalComInstrument
{
	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("lotSize")]
	public decimal? LotSize { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("guaranteedStopAllowed")]
	public bool IsGuaranteedStopAllowed { get; set; }

	[JsonProperty("streamingPricesAvailable")]
	public bool IsStreamingPricesAvailable { get; set; }

	[JsonProperty("marginFactor")]
	public decimal? MarginFactor { get; set; }
}

internal sealed class CapitalComDealingRules
{
	[JsonProperty("minStepDistance")]
	public CapitalComRuleValue MinStepDistance { get; set; }

	[JsonProperty("minDealSize")]
	public CapitalComRuleValue MinDealSize { get; set; }

	[JsonProperty("maxDealSize")]
	public CapitalComRuleValue MaxDealSize { get; set; }

	[JsonProperty("minSizeIncrement")]
	public CapitalComRuleValue MinSizeIncrement { get; set; }
}

internal sealed class CapitalComRuleValue
{
	[JsonProperty("unit")]
	public string Unit { get; set; }

	[JsonProperty("value")]
	public decimal? Value { get; set; }
}

internal sealed class CapitalComMarketSnapshot
{
	[JsonProperty("marketStatus")]
	public string MarketStatus { get; set; }

	[JsonProperty("netChange")]
	public decimal? NetChange { get; set; }

	[JsonProperty("percentageChange")]
	public decimal? PercentageChange { get; set; }

	[JsonProperty("updateTime")]
	public string UpdateTime { get; set; }

	[JsonProperty("updateTimeUTC")]
	public string UpdateTimeUtc { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("offer")]
	public decimal? Offer { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("decimalPlacesFactor")]
	public int? DecimalPlacesFactor { get; set; }
}

internal sealed class CapitalComPricesResponse
{
	[JsonProperty("prices")]
	public CapitalComPriceSnapshot[] Prices { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }
}

internal sealed class CapitalComPriceSnapshot
{
	[JsonProperty("snapshotTime")]
	public string SnapshotTime { get; set; }

	[JsonProperty("snapshotTimeUTC")]
	public string SnapshotTimeUtc { get; set; }

	[JsonProperty("openPrice")]
	public CapitalComPrice Open { get; set; }

	[JsonProperty("closePrice")]
	public CapitalComPrice Close { get; set; }

	[JsonProperty("highPrice")]
	public CapitalComPrice High { get; set; }

	[JsonProperty("lowPrice")]
	public CapitalComPrice Low { get; set; }

	[JsonProperty("lastTradedVolume")]
	public decimal? Volume { get; set; }
}

internal sealed class CapitalComPrice
{
	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	public decimal? Mid => Bid is { } bid && Ask is { } ask ? (bid + ask) / 2 : Bid ?? Ask;
}

internal sealed class CapitalComPositionsResponse
{
	[JsonProperty("positions")]
	public CapitalComOpenPosition[] Positions { get; set; }
}

internal sealed class CapitalComOpenPosition
{
	[JsonProperty("position")]
	public CapitalComPosition Position { get; set; }

	[JsonProperty("market")]
	public CapitalComMarketSummary Market { get; set; }
}

internal sealed class CapitalComPosition
{
	[JsonProperty("contractSize")]
	public decimal? ContractSize { get; set; }

	[JsonProperty("createdDate")]
	public string CreatedDate { get; set; }

	[JsonProperty("createdDateUTC")]
	public string CreatedDateUtc { get; set; }

	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("dealReference")]
	public string DealReference { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("leverage")]
	public decimal? Leverage { get; set; }

	[JsonProperty("upl")]
	public decimal? UnrealizedProfitLoss { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("level")]
	public decimal? Level { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("guaranteedStop")]
	public bool IsGuaranteedStop { get; set; }

	[JsonProperty("trailingStop")]
	public bool IsTrailingStop { get; set; }

	[JsonProperty("stopLevel")]
	public decimal? StopLevel { get; set; }

	[JsonProperty("stopDistance")]
	public decimal? StopDistance { get; set; }

	[JsonProperty("stopAmount")]
	public decimal? StopAmount { get; set; }

	[JsonProperty("profitLevel")]
	public decimal? ProfitLevel { get; set; }

	[JsonProperty("profitDistance")]
	public decimal? ProfitDistance { get; set; }

	[JsonProperty("profitAmount")]
	public decimal? ProfitAmount { get; set; }
}

internal sealed class CapitalComWorkingOrdersResponse
{
	[JsonProperty("workingOrders")]
	public CapitalComWorkingOrder[] WorkingOrders { get; set; }
}

internal sealed class CapitalComWorkingOrder
{
	[JsonProperty("workingOrderData")]
	public CapitalComWorkingOrderData Data { get; set; }

	[JsonProperty("marketData")]
	public CapitalComMarketSummary Market { get; set; }
}

internal sealed class CapitalComWorkingOrderData
{
	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("orderSize")]
	public decimal? Size { get; set; }

	[JsonProperty("orderLevel")]
	public decimal? Level { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("goodTillDate")]
	public string GoodTillDate { get; set; }

	[JsonProperty("goodTillDateUTC")]
	public string GoodTillDateUtc { get; set; }

	[JsonProperty("createdDate")]
	public string CreatedDate { get; set; }

	[JsonProperty("createdDateUTC")]
	public string CreatedDateUtc { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("guaranteedStop")]
	public bool IsGuaranteedStop { get; set; }

	[JsonProperty("trailingStop")]
	public bool IsTrailingStop { get; set; }

	[JsonProperty("stopLevel")]
	public decimal? StopLevel { get; set; }

	[JsonProperty("stopDistance")]
	public decimal? StopDistance { get; set; }

	[JsonProperty("stopAmount")]
	public decimal? StopAmount { get; set; }

	[JsonProperty("profitLevel")]
	public decimal? ProfitLevel { get; set; }

	[JsonProperty("profitDistance")]
	public decimal? ProfitDistance { get; set; }

	[JsonProperty("profitAmount")]
	public decimal? ProfitAmount { get; set; }

	[JsonProperty("currencyCode")]
	public string CurrencyCode { get; set; }
}

internal abstract class CapitalComProtectionRequest
{
	[JsonProperty("guaranteedStop")]
	public bool IsGuaranteedStop { get; set; }

	[JsonProperty("trailingStop")]
	public bool IsTrailingStop { get; set; }

	[JsonProperty("stopLevel", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopLevel { get; set; }

	[JsonProperty("stopDistance", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopDistance { get; set; }

	[JsonProperty("stopAmount", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopAmount { get; set; }

	[JsonProperty("profitLevel", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? ProfitLevel { get; set; }

	[JsonProperty("profitDistance", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? ProfitDistance { get; set; }

	[JsonProperty("profitAmount", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? ProfitAmount { get; set; }
}

internal sealed class CapitalComCreatePositionRequest : CapitalComProtectionRequest
{
	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }
}

internal sealed class CapitalComEditPositionRequest : CapitalComProtectionRequest
{
}

internal sealed class CapitalComCreateWorkingOrderRequest : CapitalComProtectionRequest
{
	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("level")]
	public decimal Level { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("goodTillDate", NullValueHandling = NullValueHandling.Ignore)]
	public string GoodTillDate { get; set; }
}

internal sealed class CapitalComEditWorkingOrderRequest : CapitalComProtectionRequest
{
	[JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Level { get; set; }

	[JsonProperty("goodTillDate", NullValueHandling = NullValueHandling.Ignore)]
	public string GoodTillDate { get; set; }
}

internal sealed class CapitalComDealReference
{
	[JsonProperty("dealReference")]
	public string DealReference { get; set; }
}

internal sealed class CapitalComConfirmation
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("dealStatus")]
	public string DealStatus { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("dealReference")]
	public string DealReference { get; set; }

	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("affectedDeals")]
	public CapitalComAffectedDeal[] AffectedDeals { get; set; }

	[JsonProperty("level")]
	public decimal? Level { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("guaranteedStop")]
	public bool IsGuaranteedStop { get; set; }

	[JsonProperty("trailingStop")]
	public bool IsTrailingStop { get; set; }

	[JsonProperty("stopLevel")]
	public decimal? StopLevel { get; set; }

	[JsonProperty("profitLevel")]
	public decimal? ProfitLevel { get; set; }
}

internal sealed class CapitalComAffectedDeal
{
	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

internal sealed class CapitalComActivitiesResponse
{
	[JsonProperty("activities")]
	public CapitalComActivity[] Activities { get; set; }
}

internal sealed class CapitalComActivity
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("dateUTC")]
	public string DateUtc { get; set; }

	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("details")]
	public CapitalComActivityDetails Details { get; set; }
}

internal sealed class CapitalComActivityDetails
{
	[JsonProperty("dealReference")]
	public string DealReference { get; set; }

	[JsonProperty("marketName")]
	public string MarketName { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("level")]
	public decimal? Level { get; set; }

	[JsonProperty("goodTillDate")]
	public string GoodTillDate { get; set; }

	[JsonProperty("guaranteedStop")]
	public bool IsGuaranteedStop { get; set; }

	[JsonProperty("trailingStop")]
	public bool IsTrailingStop { get; set; }

	[JsonProperty("stopLevel")]
	public decimal? StopLevel { get; set; }

	[JsonProperty("stopDistance")]
	public decimal? StopDistance { get; set; }

	[JsonProperty("stopAmount")]
	public decimal? StopAmount { get; set; }

	[JsonProperty("profitLevel")]
	public decimal? ProfitLevel { get; set; }

	[JsonProperty("profitDistance")]
	public decimal? ProfitDistance { get; set; }

	[JsonProperty("profitAmount")]
	public decimal? ProfitAmount { get; set; }
}

internal sealed class CapitalComApiError
{
	[JsonProperty("errorCode")]
	public string Code { get; set; }
}
