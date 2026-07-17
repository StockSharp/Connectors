namespace StockSharp.XOpenHub.Native.Model;

internal sealed class XApiCommand<TArguments>
{
	[JsonProperty("command")]
	public string Command { get; set; }

	[JsonProperty("arguments", NullValueHandling = NullValueHandling.Ignore)]
	public TArguments Arguments { get; set; }

	[JsonProperty("customTag")]
	public string CustomTag { get; set; }
}

internal sealed class XApiEmptyArguments
{
}

internal sealed class XApiEmptyResult
{
}

internal sealed class XApiResponse<T>
{
	[JsonProperty("status")]
	public bool Status { get; set; }

	[JsonProperty("returnData")]
	public T Data { get; set; }

	[JsonProperty("streamSessionId")]
	public string StreamSessionId { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("errorDescr")]
	public string ErrorDescription { get; set; }

	[JsonProperty("customTag")]
	public string CustomTag { get; set; }
}

internal sealed class XApiLoginArguments
{
	[JsonProperty("userId")]
	public string UserId { get; set; }

	[JsonProperty("password")]
	public string Password { get; set; }

	[JsonProperty("appName")]
	public string ApplicationName { get; set; }
}

internal sealed class XApiSymbolArguments
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

internal sealed class XApiChartArguments
{
	[JsonProperty("info")]
	public XApiChartRangeInfo Info { get; set; }
}

internal sealed class XApiChartRangeInfo
{
	[JsonProperty("period")]
	public int Period { get; set; }

	[JsonProperty("start")]
	public long Start { get; set; }

	[JsonProperty("end")]
	public long End { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ticks")]
	public long Ticks { get; set; }
}

internal sealed class XApiTradesArguments
{
	[JsonProperty("openedOnly")]
	public bool OpenedOnly { get; set; }
}

internal sealed class XApiTradesHistoryArguments
{
	[JsonProperty("start")]
	public long Start { get; set; }

	[JsonProperty("end")]
	public long End { get; set; }
}

internal sealed class XApiTradeTransactionArguments
{
	[JsonProperty("tradeTransInfo")]
	public XApiTradeTransactionInfo Trade { get; set; }
}

internal sealed class XApiTradeStatusArguments
{
	[JsonProperty("order")]
	public long Order { get; set; }
}

internal sealed class XApiSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("categoryName")]
	public string Category { get; set; }

	[JsonProperty("groupName")]
	public string Group { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("currencyPair")]
	public bool IsCurrencyPair { get; set; }

	[JsonProperty("currencyProfit")]
	public string ProfitCurrency { get; set; }

	[JsonProperty("contractSize")]
	public decimal ContractSize { get; set; }

	[JsonProperty("precision")]
	public int Digits { get; set; }

	[JsonProperty("lotMin")]
	public decimal MinVolume { get; set; }

	[JsonProperty("lotMax")]
	public decimal MaxVolume { get; set; }

	[JsonProperty("lotStep")]
	public decimal VolumeStep { get; set; }

	[JsonProperty("tickSize")]
	public decimal? TickSize { get; set; }

	[JsonProperty("tickValue")]
	public decimal? TickValue { get; set; }

	[JsonProperty("ask")]
	public decimal Ask { get; set; }

	[JsonProperty("bid")]
	public decimal Bid { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("expiration")]
	public long? Expiration { get; set; }

	[JsonProperty("quoteId")]
	public int QuoteId { get; set; }
}

internal sealed class XApiChartData
{
	[JsonProperty("digits")]
	public int Digits { get; set; }

	[JsonProperty("rateInfos")]
	public XApiRate[] Rates { get; set; }
}

internal sealed class XApiRate
{
	[JsonProperty("ctm")]
	public long Time { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal HighDelta { get; set; }

	[JsonProperty("low")]
	public decimal LowDelta { get; set; }

	[JsonProperty("close")]
	public decimal CloseDelta { get; set; }

	[JsonProperty("vol")]
	public decimal Volume { get; set; }
}

internal sealed class XApiMarginLevel
{
	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("credit")]
	public decimal Credit { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("equity")]
	public decimal Equity { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("margin_free")]
	public decimal FreeMargin { get; set; }

	[JsonProperty("margin_level")]
	public decimal MarginLevel { get; set; }
}

internal sealed class XApiTrade
{
	[JsonProperty("order")]
	public long Order { get; set; }

	[JsonProperty("order2")]
	public long Transaction { get; set; }

	[JsonProperty("position")]
	public long Position { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("cmd")]
	public int Command { get; set; }

	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("open_price")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("open_time")]
	public long OpenTime { get; set; }

	[JsonProperty("close_price")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("close_time")]
	public long? CloseTime { get; set; }

	[JsonProperty("closed")]
	public bool Closed { get; set; }

	[JsonProperty("profit")]
	public decimal? Profit { get; set; }

	[JsonProperty("commission")]
	public decimal? Commission { get; set; }

	[JsonProperty("storage")]
	public decimal? Swap { get; set; }

	[JsonProperty("sl")]
	public decimal StopLoss { get; set; }

	[JsonProperty("tp")]
	public decimal TakeProfit { get; set; }

	[JsonProperty("expiration")]
	public long? Expiration { get; set; }

	[JsonProperty("comment")]
	public string Comment { get; set; }

	[JsonProperty("customComment")]
	public string CustomComment { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }
}

internal sealed class XApiTradeTransactionInfo
{
	[JsonProperty("cmd")]
	public int Command { get; set; }

	[JsonProperty("type")]
	public int Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("sl")]
	public decimal StopLoss { get; set; }

	[JsonProperty("tp")]
	public decimal TakeProfit { get; set; }

	[JsonProperty("order")]
	public long Order { get; set; }

	[JsonProperty("expiration")]
	public long Expiration { get; set; }

	[JsonProperty("offset")]
	public int Offset { get; set; }

	[JsonProperty("customComment")]
	public string CustomComment { get; set; }
}

internal sealed class XApiTradeTransactionResult
{
	[JsonProperty("order")]
	public long Order { get; set; }
}

internal sealed class XApiTradeStatus
{
	[JsonProperty("ask")]
	public decimal Ask { get; set; }

	[JsonProperty("bid")]
	public decimal Bid { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("customComment")]
	public string CustomComment { get; set; }

	[JsonProperty("order")]
	public long Order { get; set; }

	[JsonProperty("requestStatus")]
	public int RequestStatus { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }
}

internal sealed class XApiStreamRequest
{
	[JsonProperty("command")]
	public string Command { get; set; }

	[JsonProperty("streamSessionId", NullValueHandling = NullValueHandling.Ignore)]
	public string StreamSessionId { get; set; }

	[JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)]
	public string Symbol { get; set; }

	[JsonProperty("minArrivalTime", NullValueHandling = NullValueHandling.Ignore)]
	public int? MinArrivalTime { get; set; }

	[JsonProperty("maxLevel", NullValueHandling = NullValueHandling.Ignore)]
	public int? MaxLevel { get; set; }
}

internal sealed class XApiStreamHeader
{
	[JsonProperty("command")]
	public string Command { get; set; }
}

internal sealed class XApiStreamMessage<T>
{
	[JsonProperty("command")]
	public string Command { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }
}

internal sealed class XApiTick
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ask")]
	public decimal Ask { get; set; }

	[JsonProperty("askVolume")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("bid")]
	public decimal Bid { get; set; }

	[JsonProperty("bidVolume")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("level")]
	public int Level { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("spreadRaw")]
	public decimal Spread { get; set; }
}

internal sealed class XApiStreamCandle
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ctm")]
	public long Time { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("vol")]
	public decimal Volume { get; set; }
}

internal sealed class XApiStreamBalance
{
	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("credit")]
	public decimal Credit { get; set; }

	[JsonProperty("equity")]
	public decimal Equity { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("marginFree")]
	public decimal FreeMargin { get; set; }

	[JsonProperty("marginLevel")]
	public decimal MarginLevel { get; set; }
}
