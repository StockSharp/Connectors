namespace StockSharp.FubonNeo.Native.Model;

enum FubonNeoAssetKinds
{
	Stock,
	FuturesOptions,
}

sealed class FubonNeoSecurityInfo
{
	public FubonNeoAssetKinds Kind { get; set; }
	public string TickerType { get; set; }
	public string Exchange { get; set; }
	public string Market { get; set; }
	public string Session { get; set; }
	public string Symbol { get; set; }
	public string Name { get; set; }
	public string ContractType { get; set; }
	public decimal? ReferencePrice { get; set; }
	public string StartDate { get; set; }
	public string EndDate { get; set; }
	public string SettlementDate { get; set; }
	public bool IsAfterHours => Session.EqualsIgnoreCase("AFTERHOURS");
}

sealed class FubonNeoAccountInfo
{
	public string Name { get; set; }
	public string BranchNo { get; set; }
	public string Account { get; set; }
	public string AccountType { get; set; }
	public string PortfolioName => $"{BranchNo}-{Account}";
	public bool IsFutures => AccountType?.Contains("F", StringComparison.OrdinalIgnoreCase) == true ||
		AccountType?.Contains("FUT", StringComparison.OrdinalIgnoreCase) == true;
}

sealed class FubonNeoSubscription
{
	public FubonNeoAssetKinds Kind { get; set; }
	public string Channel { get; set; }
	public string Symbol { get; set; }
	public bool IsAfterHours { get; set; }
	public long TransactionId { get; set; }
	public SecurityId SecurityId { get; set; }
	public string ServerId { get; set; }
	public string Key => $"{Kind}|{Channel?.ToLowerInvariant()}|{Symbol?.ToUpperInvariant()}|{IsAfterHours}";
}

sealed class FubonNeoOrderRequest
{
	public string PortfolioName { get; set; }
	public FubonNeoAssetKinds Kind { get; set; }
	public SecurityTypes SecurityType { get; set; }
	public string Symbol { get; set; }
	public Sides Side { get; set; }
	public OrderTypes OrderType { get; set; }
	public TimeInForce TimeInForce { get; set; }
	public decimal Price { get; set; }
	public long Volume { get; set; }
	public FubonNeoStockMarketTypes StockMarketType { get; set; }
	public FubonNeoStockOrderTypes StockOrderType { get; set; }
	public FubonNeoFuturesOrderTypes FuturesOrderType { get; set; }
	public FubonNeoPriceTypes PriceType { get; set; }
	public bool IsAfterHours { get; set; }
	public string UserTag { get; set; }
}

sealed class FubonNeoOrderUpdate
{
	public bool IsFutures { get; set; }
	public string OrderId { get; set; }
	public string Sequence { get; set; }
	public string Symbol { get; set; }
	public string PortfolioName { get; set; }
	public string Market { get; set; }
	public int? AssetType { get; set; }
	public string MarketType { get; set; }
	public string Side { get; set; }
	public string PriceType { get; set; }
	public string OrderType { get; set; }
	public string TimeInForce { get; set; }
	public decimal? Price { get; set; }
	public long Volume { get; set; }
	public long FilledVolume { get; set; }
	public decimal? FilledMoney { get; set; }
	public int? Status { get; set; }
	public int? FunctionType { get; set; }
	public bool IsPreOrder { get; set; }
	public string Error { get; set; }
	public string Date { get; set; }
	public string LastTime { get; set; }
	public string UserTag { get; set; }
}

sealed class FubonNeoFillUpdate
{
	public bool IsFutures { get; set; }
	public bool IsOption { get; set; }
	public string OrderId { get; set; }
	public string Sequence { get; set; }
	public string FillId { get; set; }
	public string Symbol { get; set; }
	public string PortfolioName { get; set; }
	public string Side { get; set; }
	public decimal Price { get; set; }
	public long Volume { get; set; }
	public decimal AveragePrice { get; set; }
	public string Date { get; set; }
	public string Time { get; set; }
	public string UserTag { get; set; }
}

sealed class FubonNeoPositionInfo
{
	public bool IsFutures { get; set; }
	public bool IsOption { get; set; }
	public string PortfolioName { get; set; }
	public string Symbol { get; set; }
	public string Side { get; set; }
	public decimal CurrentValue { get; set; }
	public decimal? AveragePrice { get; set; }
	public decimal? CurrentPrice { get; set; }
	public decimal? UnrealizedPnL { get; set; }
	public decimal? BlockedValue { get; set; }
	public string OrderType { get; set; }
}

sealed class FubonNeoCashInfo
{
	public string PortfolioName { get; set; }
	public string Currency { get; set; }
	public decimal CurrentValue { get; set; }
	public decimal? AvailableValue { get; set; }
	public decimal? BlockedValue { get; set; }
	public decimal? UnrealizedPnL { get; set; }
}
