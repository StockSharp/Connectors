namespace StockSharp.IndependentReserve.Native.Model;

abstract class IndependentReservePrivateRequest
{
	[JsonProperty("apiKey", Order = 0)]
	public string ApiKey { get; set; }

	[JsonProperty("expiry", Order = 1)]
	public string Expiry { get; set; }

	[JsonProperty("signature", Order = 100)]
	public string Signature { get; set; }
}

sealed class IndependentReserveAccountsRequest :
	IndependentReservePrivateRequest
{
}

sealed class IndependentReserveOpenOrdersRequest :
	IndependentReservePrivateRequest
{
	[JsonProperty("primaryCurrencyCode", Order = 10,
		NullValueHandling = NullValueHandling.Ignore)]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("secondaryCurrencyCode", Order = 11,
		NullValueHandling = NullValueHandling.Ignore)]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("pageIndex", Order = 12)]
	public int PageIndex { get; init; }

	[JsonProperty("pageSize", Order = 13)]
	public int PageSize { get; init; }
}

sealed class IndependentReserveClosedOrdersRequest :
	IndependentReservePrivateRequest
{
	[JsonProperty("primaryCurrencyCode", Order = 10,
		NullValueHandling = NullValueHandling.Ignore)]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("secondaryCurrencyCode", Order = 11,
		NullValueHandling = NullValueHandling.Ignore)]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("pageIndex", Order = 12)]
	public int PageIndex { get; init; }

	[JsonProperty("pageSize", Order = 13)]
	public int PageSize { get; init; }

	[JsonProperty("includeTotals", Order = 14)]
	public bool IsIncludeTotals { get; init; }

	[JsonProperty("fromTimestampUtc", Order = 15,
		NullValueHandling = NullValueHandling.Ignore)]
	public string FromTimestampUtc { get; init; }
}

sealed class IndependentReserveOrderLookupRequest :
	IndependentReservePrivateRequest
{
	[JsonProperty("orderGuid", Order = 10,
		NullValueHandling = NullValueHandling.Ignore)]
	public string OrderGuid { get; init; }

	[JsonProperty("clientId", Order = 11,
		NullValueHandling = NullValueHandling.Ignore)]
	public string ClientId { get; init; }
}

sealed class IndependentReserveTradesRequest :
	IndependentReservePrivateRequest
{
	[JsonProperty("pageIndex", Order = 10)]
	public int PageIndex { get; init; }

	[JsonProperty("pageSize", Order = 11)]
	public int PageSize { get; init; }

	[JsonProperty("fromTimestampUtc", Order = 12,
		NullValueHandling = NullValueHandling.Ignore)]
	public string FromTimestampUtc { get; init; }

	[JsonProperty("toTimestampUtc", Order = 13,
		NullValueHandling = NullValueHandling.Ignore)]
	public string ToTimestampUtc { get; init; }

	[JsonProperty("includeTotals", Order = 14)]
	public bool IsIncludeTotals { get; init; }
}

sealed class IndependentReserveTradesByOrderRequest :
	IndependentReservePrivateRequest
{
	[JsonProperty("orderGuid", Order = 10,
		NullValueHandling = NullValueHandling.Ignore)]
	public string OrderGuid { get; init; }

	[JsonProperty("pageIndex", Order = 11)]
	public int PageIndex { get; init; }

	[JsonProperty("pageSize", Order = 12)]
	public int PageSize { get; init; }

	[JsonProperty("clientId", Order = 13,
		NullValueHandling = NullValueHandling.Ignore)]
	public string ClientId { get; init; }
}

sealed class IndependentReserveLimitOrderRequest :
	IndependentReservePrivateRequest
{
	[JsonProperty("primaryCurrencyCode", Order = 10)]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("secondaryCurrencyCode", Order = 11)]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("orderType", Order = 12)]
	public IndependentReserveOrderTypes OrderType { get; init; }

	[JsonProperty("price", Order = 13)]
	public decimal Price { get; init; }

	[JsonProperty("volume", Order = 14)]
	public decimal Volume { get; init; }

	[JsonProperty("clientId", Order = 15)]
	public string ClientId { get; init; }

	[JsonProperty("timeInForce", Order = 16)]
	public IndependentReserveTimeInForce TimeInForce { get; init; }
}

sealed class IndependentReserveMarketOrderRequest :
	IndependentReservePrivateRequest
{
	[JsonProperty("primaryCurrencyCode", Order = 10)]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("secondaryCurrencyCode", Order = 11)]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("orderType", Order = 12)]
	public IndependentReserveOrderTypes OrderType { get; init; }

	[JsonProperty("volume", Order = 13)]
	public decimal Volume { get; init; }

	[JsonProperty("clientId", Order = 14)]
	public string ClientId { get; init; }

	[JsonProperty("allowedSlippagePercent", Order = 15,
		NullValueHandling = NullValueHandling.Ignore)]
	public decimal? AllowedSlippagePercent { get; init; }

	[JsonProperty("volumeCurrencyType", Order = 16)]
	public IndependentReserveVolumeCurrencyTypes VolumeCurrencyType { get; init; }
}

sealed class IndependentReserveCancelOrderRequest :
	IndependentReservePrivateRequest
{
	[JsonProperty("orderGuid", Order = 10)]
	public string OrderGuid { get; init; }
}

sealed class IndependentReserveAccount
{
	[JsonProperty("AccountGuid")]
	public Guid AccountGuid { get; init; }

	[JsonProperty("AccountStatus")]
	public IndependentReserveAccountStatuses Status { get; init; }

	[JsonProperty("AvailableBalance")]
	public decimal AvailableBalance { get; init; }

	[JsonProperty("CurrencyCode")]
	public string CurrencyCode { get; init; }

	[JsonProperty("TotalBalance")]
	public decimal TotalBalance { get; init; }
}

sealed class IndependentReservePage<T>
{
	[JsonProperty("PageSize")]
	public long PageSize { get; init; }

	[JsonProperty("TotalItems")]
	public long TotalItems { get; init; }

	[JsonProperty("TotalPages")]
	public long TotalPages { get; init; }

	[JsonProperty("Data")]
	public T[] Data { get; init; }
}

sealed class IndependentReserveOrder
{
	[JsonProperty("OrderGuid")]
	public Guid OrderGuid { get; init; }

	[JsonProperty("CreatedTimestampUtc")]
	public DateTime CreatedTimestampUtc { get; init; }

	[JsonProperty("Type")]
	public IndependentReserveOrderTypes Type { get; init; }

	[JsonProperty("VolumeOrdered")]
	public decimal VolumeOrdered { get; init; }

	[JsonProperty("VolumeFilled")]
	public decimal VolumeFilled { get; init; }

	[JsonProperty("Price")]
	public decimal? Price { get; init; }

	[JsonProperty("AvgPrice")]
	public decimal? AveragePrice { get; init; }

	[JsonProperty("ReservedAmount")]
	public decimal ReservedAmount { get; init; }

	[JsonProperty("Status")]
	public IndependentReserveOrderStatuses Status { get; init; }

	[JsonProperty("PrimaryCurrencyCode")]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("SecondaryCurrencyCode")]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("FeePercent")]
	public decimal FeePercent { get; init; }

	[JsonProperty("VolumeCurrencyType")]
	public IndependentReserveVolumeCurrencyTypes VolumeCurrencyType { get; init; }

	[JsonProperty("ClientId")]
	public string ClientId { get; init; }
}

sealed class IndependentReserveHistoryOrder
{
	[JsonProperty("CreatedTimestampUtc")]
	public DateTime CreatedTimestampUtc { get; init; }

	[JsonProperty("OrderType")]
	public IndependentReserveOrderTypes OrderType { get; init; }

	[JsonProperty("Volume")]
	public decimal Volume { get; init; }

	[JsonProperty("Outstanding")]
	public decimal? Outstanding { get; init; }

	[JsonProperty("Price")]
	public decimal? Price { get; init; }

	[JsonProperty("AvgPrice")]
	public decimal? AveragePrice { get; init; }

	[JsonProperty("Status")]
	public IndependentReserveOrderStatuses Status { get; init; }

	[JsonProperty("OrderGuid")]
	public Guid OrderGuid { get; init; }

	[JsonProperty("PrimaryCurrencyCode")]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("SecondaryCurrencyCode")]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("FeePercent")]
	public decimal FeePercent { get; init; }

	[JsonProperty("Original")]
	public IndependentReserveOrderVolume Original { get; init; }

	[JsonProperty("ClientId")]
	public string ClientId { get; init; }

	[JsonProperty("TimeInForce")]
	public IndependentReserveTimeInForce? TimeInForce { get; init; }
}

sealed class IndependentReserveOrderVolume
{
	[JsonProperty("Volume")]
	public decimal Volume { get; init; }

	[JsonProperty("Outstanding")]
	public decimal? Outstanding { get; init; }

	[JsonProperty("VolumeCurrencyType")]
	public IndependentReserveVolumeCurrencyTypes VolumeCurrencyType { get; init; }
}

sealed class IndependentReserveUserTrade
{
	[JsonProperty("TradeGuid")]
	public Guid TradeGuid { get; init; }

	[JsonProperty("TradeTimestampUtc")]
	public DateTime TradeTimestampUtc { get; init; }

	[JsonProperty("OrderGuid")]
	public Guid OrderGuid { get; init; }

	[JsonProperty("OrderType")]
	public IndependentReserveOrderTypes OrderType { get; init; }

	[JsonProperty("OrderTimestampUtc")]
	public DateTime OrderTimestampUtc { get; init; }

	[JsonProperty("VolumeTraded")]
	public decimal Volume { get; init; }

	[JsonProperty("Price")]
	public decimal Price { get; init; }

	[JsonProperty("PrimaryCurrencyCode")]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("SecondaryCurrencyCode")]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("TradeSide")]
	public IndependentReserveTradeSides TradeSide { get; init; }
}
