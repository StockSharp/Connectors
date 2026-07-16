namespace StockSharp.IG.Native;

internal sealed class IgLoginRequest
{
	[JsonProperty("identifier")]
	public string Identifier { get; set; }

	[JsonProperty("password")]
	public string Password { get; set; }

	[JsonProperty("encryptedPassword")]
	public bool EncryptedPassword { get; set; }
}

internal sealed class IgLoginResponse
{
	[JsonProperty("currentAccountId")]
	public string CurrentAccountId { get; set; }

	[JsonProperty("clientId")]
	public string ClientId { get; set; }

	[JsonProperty("currencyIsoCode")]
	public string CurrencyIsoCode { get; set; }

	[JsonProperty("lightstreamerEndpoint")]
	public string LightstreamerEndpoint { get; set; }

	[JsonProperty("timezoneOffset")]
	public int TimezoneOffset { get; set; }

	[JsonProperty("trailingStopsEnabled")]
	public bool TrailingStopsEnabled { get; set; }

	[JsonProperty("accounts")]
	public IgAccount[] Accounts { get; set; }
}

internal sealed class IgSession
{
	public string AccountId { get; init; }
	public string Currency { get; init; }
	public string LightstreamerEndpoint { get; init; }
	public string Cst { get; init; }
	public string SecurityToken { get; init; }
	public IgAccount[] Accounts { get; init; }
}

internal sealed class IgEncryptionKey
{
	[JsonProperty("encryptionKey")]
	public string Key { get; set; }

	[JsonProperty("timeStamp")]
	public long Timestamp { get; set; }
}

internal sealed class IgAccountList
{
	[JsonProperty("accounts")]
	public IgAccount[] Accounts { get; set; }
}

internal sealed class IgAccount
{
	[JsonProperty("accountId")]
	public string Id { get; set; }

	[JsonProperty("accountName")]
	public string Name { get; set; }

	[JsonProperty("accountAlias")]
	public string Alias { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("accountType")]
	public string Type { get; set; }

	[JsonProperty("preferred")]
	public bool Preferred { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public IgBalance Balance { get; set; }
}

internal sealed class IgBalance
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

internal sealed class IgAccountSwitchRequest
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("defaultAccount")]
	public bool DefaultAccount { get; set; }
}

internal sealed class IgAccountSwitchResponse
{
	[JsonProperty("dealingEnabled")]
	public bool DealingEnabled { get; set; }

	[JsonProperty("trailingStopsEnabled")]
	public bool TrailingStopsEnabled { get; set; }
}

internal sealed class IgSearchResponse
{
	[JsonProperty("markets")]
	public IgMarketSummary[] Markets { get; set; }
}

internal sealed class IgMarketSummary
{
	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("instrumentName")]
	public string Name { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("offer")]
	public decimal? Offer { get; set; }

	[JsonProperty("streamingPricesAvailable")]
	public bool StreamingPricesAvailable { get; set; }

	[JsonProperty("marketStatus")]
	public string MarketStatus { get; set; }

	[JsonProperty("scalingFactor")]
	public int ScalingFactor { get; set; }
}

internal sealed class IgMarketDetails
{
	[JsonProperty("instrument")]
	public IgInstrument Instrument { get; set; }

	[JsonProperty("dealingRules")]
	public IgDealingRules DealingRules { get; set; }

	[JsonProperty("snapshot")]
	public IgMarketSnapshot Snapshot { get; set; }
}

internal sealed class IgInstrument
{
	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("lotSize")]
	public decimal? LotSize { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("country")]
	public string Country { get; set; }

	[JsonProperty("contractSize")]
	public string ContractSize { get; set; }

	[JsonProperty("currencies")]
	public IgCurrency[] Currencies { get; set; }

	[JsonProperty("streamingPricesAvailable")]
	public bool StreamingPricesAvailable { get; set; }
}

internal sealed class IgCurrency
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("isDefault")]
	public bool IsDefault { get; set; }
}

internal sealed class IgDealingRules
{
	[JsonProperty("minDealSize")]
	public IgDealingRule MinDealSize { get; set; }

	[JsonProperty("minStepDistance")]
	public IgDealingRule MinStepDistance { get; set; }
}

internal sealed class IgDealingRule
{
	[JsonProperty("value")]
	public decimal? Value { get; set; }

	[JsonProperty("unit")]
	public string Unit { get; set; }
}

internal sealed class IgMarketSnapshot
{
	[JsonProperty("marketStatus")]
	public string MarketStatus { get; set; }

	[JsonProperty("updateTimeUTC")]
	public string UpdateTimeUtc { get; set; }

	[JsonProperty("updateTime")]
	public string UpdateTime { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("offer")]
	public decimal? Offer { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("netChange")]
	public decimal? NetChange { get; set; }
}

internal sealed class IgPriceList
{
	[JsonProperty("prices")]
	public IgPriceSnapshot[] Prices { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("metadata")]
	public IgPriceMetadata Metadata { get; set; }
}

internal sealed class IgPriceMetadata
{
	[JsonProperty("pageData")]
	public IgPageData PageData { get; set; }

	[JsonProperty("allowance")]
	public IgAllowance Allowance { get; set; }
}

internal sealed class IgPageData
{
	[JsonProperty("pageSize")]
	public int PageSize { get; set; }

	[JsonProperty("pageNumber")]
	public int PageNumber { get; set; }

	[JsonProperty("totalPages")]
	public int TotalPages { get; set; }
}

internal sealed class IgAllowance
{
	[JsonProperty("remainingAllowance")]
	public int Remaining { get; set; }

	[JsonProperty("totalAllowance")]
	public int Total { get; set; }

	[JsonProperty("allowanceExpiry")]
	public int ExpirySeconds { get; set; }
}

internal sealed class IgPriceSnapshot
{
	[JsonProperty("snapshotTime")]
	public string SnapshotTime { get; set; }

	[JsonProperty("snapshotTimeUTC")]
	public string SnapshotTimeUtc { get; set; }

	[JsonProperty("openPrice")]
	public IgPrice Open { get; set; }

	[JsonProperty("closePrice")]
	public IgPrice Close { get; set; }

	[JsonProperty("highPrice")]
	public IgPrice High { get; set; }

	[JsonProperty("lowPrice")]
	public IgPrice Low { get; set; }

	[JsonProperty("lastTradedVolume")]
	public decimal? Volume { get; set; }
}

internal sealed class IgPrice
{
	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("lastTraded")]
	public decimal? Last { get; set; }

	public decimal? Mid => Last ?? (Bid is { } bid && Ask is { } ask ? (bid + ask) / 2 : Bid ?? Ask);
}

internal sealed class IgPositionsResponse
{
	[JsonProperty("positions")]
	public IgOpenPosition[] Positions { get; set; }
}

internal sealed class IgOpenPosition
{
	[JsonProperty("position")]
	public IgPosition Position { get; set; }

	[JsonProperty("market")]
	public IgPositionMarket Market { get; set; }
}

internal sealed class IgPosition
{
	[JsonProperty("createdDateUTC")]
	public string CreatedDateUtc { get; set; }

	[JsonProperty("createdDate")]
	public string CreatedDate { get; set; }

	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("limitLevel")]
	public decimal? LimitLevel { get; set; }

	[JsonProperty("level")]
	public decimal? Level { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("stopLevel")]
	public decimal? StopLevel { get; set; }

	[JsonProperty("trailingStep")]
	public decimal? TrailingStep { get; set; }

	[JsonProperty("trailingStopDistance")]
	public decimal? TrailingStopDistance { get; set; }
}

internal sealed class IgPositionMarket
{
	[JsonProperty("instrumentName")]
	public string Name { get; set; }

	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("offer")]
	public decimal? Offer { get; set; }
}

internal sealed class IgWorkingOrdersResponse
{
	[JsonProperty("workingOrders")]
	public IgWorkingOrder[] WorkingOrders { get; set; }
}

internal sealed class IgWorkingOrder
{
	[JsonProperty("workingOrderData")]
	public IgWorkingOrderData Data { get; set; }

	[JsonProperty("marketData")]
	public IgPositionMarket Market { get; set; }
}

internal sealed class IgWorkingOrderData
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

	[JsonProperty("goodTillDateUTC")]
	public string GoodTillDateUtc { get; set; }

	[JsonProperty("goodTillDate")]
	public string GoodTillDate { get; set; }

	[JsonProperty("createdDateUTC")]
	public string CreatedDateUtc { get; set; }

	[JsonProperty("createdDate")]
	public string CreatedDate { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("stopDistance")]
	public decimal? StopDistance { get; set; }

	[JsonProperty("limitDistance")]
	public decimal? LimitDistance { get; set; }

	[JsonProperty("currencyCode")]
	public string CurrencyCode { get; set; }
}

internal sealed class IgCreatePositionRequest
{
	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Level { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("guaranteedStop")]
	public bool GuaranteedStop { get; set; }

	[JsonProperty("stopLevel", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopLevel { get; set; }

	[JsonProperty("stopDistance", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopDistance { get; set; }

	[JsonProperty("trailingStop")]
	public bool TrailingStop { get; set; }

	[JsonProperty("trailingStopIncrement", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TrailingStopIncrement { get; set; }

	[JsonProperty("forceOpen")]
	public bool ForceOpen { get; set; }

	[JsonProperty("limitLevel", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? LimitLevel { get; set; }

	[JsonProperty("limitDistance", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? LimitDistance { get; set; }

	[JsonProperty("currencyCode", NullValueHandling = NullValueHandling.Ignore)]
	public string CurrencyCode { get; set; }
}

internal sealed class IgClosePositionRequest
{
	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; } = "FILL_OR_KILL";

	[JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Level { get; set; }
}

internal sealed class IgEditPositionRequest
{
	[JsonProperty("stopLevel", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopLevel { get; set; }

	[JsonProperty("limitLevel", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? LimitLevel { get; set; }

	[JsonProperty("trailingStop")]
	public bool TrailingStop { get; set; }

	[JsonProperty("trailingStopDistance", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TrailingStopDistance { get; set; }

	[JsonProperty("trailingStopIncrement", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TrailingStopIncrement { get; set; }
}

internal sealed class IgCreateWorkingOrderRequest
{
	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("level")]
	public decimal Level { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("currencyCode", NullValueHandling = NullValueHandling.Ignore)]
	public string CurrencyCode { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("goodTillDate", NullValueHandling = NullValueHandling.Ignore)]
	public string GoodTillDate { get; set; }

	[JsonProperty("guaranteedStop")]
	public bool GuaranteedStop { get; set; }

	[JsonProperty("forceOpen")]
	public bool ForceOpen { get; set; }

	[JsonProperty("stopDistance", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopDistance { get; set; }

	[JsonProperty("limitDistance", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? LimitDistance { get; set; }
}

internal sealed class IgEditWorkingOrderRequest
{
	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("goodTillDate", NullValueHandling = NullValueHandling.Ignore)]
	public string GoodTillDate { get; set; }

	[JsonProperty("stopDistance", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopDistance { get; set; }

	[JsonProperty("limitDistance", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? LimitDistance { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("level")]
	public decimal Level { get; set; }
}

internal sealed class IgDealReference
{
	[JsonProperty("dealReference")]
	public string DealReference { get; set; }
}

internal sealed class IgActivitiesResponse
{
	[JsonProperty("activities")]
	public IgActivity[] Activities { get; set; }

	[JsonProperty("metadata")]
	public IgActivityMetadata Metadata { get; set; }
}

internal sealed class IgActivityMetadata
{
	[JsonProperty("paging")]
	public IgActivityPaging Paging { get; set; }
}

internal sealed class IgActivityPaging
{
	[JsonProperty("next")]
	public string Next { get; set; }

	[JsonProperty("size")]
	public int Size { get; set; }
}

internal sealed class IgActivity
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("details")]
	public IgActivityDetails Details { get; set; }
}

internal sealed class IgActivityDetails
{
	[JsonProperty("actions")]
	public IgActivityAction[] Actions { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("dealReference")]
	public string DealReference { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("goodTillDate")]
	public string GoodTillDate { get; set; }

	[JsonProperty("guaranteedStop")]
	public bool GuaranteedStop { get; set; }

	[JsonProperty("level")]
	public decimal? Level { get; set; }

	[JsonProperty("limitDistance")]
	public decimal? LimitDistance { get; set; }

	[JsonProperty("limitLevel")]
	public decimal? LimitLevel { get; set; }

	[JsonProperty("marketName")]
	public string MarketName { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("stopDistance")]
	public decimal? StopDistance { get; set; }

	[JsonProperty("stopLevel")]
	public decimal? StopLevel { get; set; }

	[JsonProperty("trailingStep")]
	public decimal? TrailingStep { get; set; }

	[JsonProperty("trailingStopDistance")]
	public decimal? TrailingStopDistance { get; set; }

	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("period")]
	public string Period { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

internal sealed class IgActivityAction
{
	[JsonProperty("affectedDealId")]
	public string AffectedDealId { get; set; }

	[JsonProperty("actionType")]
	public string ActionType { get; set; }
}

internal sealed class IgConfirmation
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("dealStatus")]
	public string DealStatus { get; set; }

	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("dealReference")]
	public string DealReference { get; set; }

	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("affectedDeals")]
	public IgAffectedDeal[] AffectedDeals { get; set; }

	[JsonProperty("level")]
	public decimal? Level { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("stopLevel")]
	public decimal? StopLevel { get; set; }

	[JsonProperty("limitLevel")]
	public decimal? LimitLevel { get; set; }

	[JsonProperty("guaranteedStop")]
	public bool GuaranteedStop { get; set; }
}

internal sealed class IgAffectedDeal
{
	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

internal sealed class IgTradeUpdate
{
	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("dealId")]
	public string DealId { get; set; }

	[JsonProperty("dealReference")]
	public string DealReference { get; set; }

	[JsonProperty("epic")]
	public string Epic { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("level")]
	public decimal? Level { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("stopLevel")]
	public decimal? StopLevel { get; set; }

	[JsonProperty("limitLevel")]
	public decimal? LimitLevel { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("goodTillDate")]
	public string GoodTillDate { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("guaranteedStop")]
	public bool GuaranteedStop { get; set; }

	[JsonProperty("dealStatus")]
	public string DealStatus { get; set; }

	[JsonProperty("trailingStopDistance")]
	public decimal? TrailingStopDistance { get; set; }

	[JsonProperty("trailingStep")]
	public decimal? TrailingStep { get; set; }
}

internal sealed class IgApiError
{
	[JsonProperty("errorCode")]
	public string Code { get; set; }
}
