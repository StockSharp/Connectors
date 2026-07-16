namespace StockSharp.Kiwoom.Native.Model;

sealed class KiwoomStreamLoginRequest
{
	[JsonProperty("trnm")]
	public string TransactionName { get; set; } = "LOGIN";

	[JsonProperty("token")]
	public string Token { get; set; }
}

sealed class KiwoomStreamRequest<TItem>
{
	[JsonProperty("trnm")]
	public string TransactionName { get; set; }

	[JsonProperty("grp_no")]
	public string GroupNumber { get; set; } = "1";

	[JsonProperty("refresh", NullValueHandling = NullValueHandling.Ignore)]
	public string Refresh { get; set; }

	[JsonProperty("data")]
	public KiwoomStreamRegistration<TItem>[] Data { get; set; }
}

sealed class KiwoomStreamRegistration<TItem>
{
	[JsonProperty("item")]
	public TItem[] Items { get; set; }

	[JsonProperty("type")]
	public string[] Types { get; set; }
}

sealed class KiwoomUsStreamItem
{
	[JsonProperty("jmcode")]
	public string SecurityCode { get; set; }

	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; }
}

sealed class KiwoomStreamEnvelope
{
	[JsonProperty("trnm")]
	public string TransactionName { get; set; }

	[JsonProperty("return_code")]
	public int? ReturnCode { get; set; }

	[JsonProperty("return_msg")]
	public string ReturnMessage { get; set; }

	[JsonProperty("data")]
	public KiwoomStreamData[] Data { get; set; }
}

sealed class KiwoomStreamData
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("item")]
	public string Item { get; set; }

	[JsonProperty("values")]
	public KiwoomStreamValues Values { get; set; }
}

sealed class KiwoomStreamValues
{
	[JsonProperty("10")]
	public string LastPrice { get; set; }
	[JsonProperty("13")]
	public string TotalVolume { get; set; }
	[JsonProperty("14")]
	public string Turnover { get; set; }
	[JsonProperty("15")]
	public string TradeVolume { get; set; }
	[JsonProperty("16")]
	public string OpenPrice { get; set; }
	[JsonProperty("17")]
	public string HighPrice { get; set; }
	[JsonProperty("18")]
	public string LowPrice { get; set; }
	[JsonProperty("20")]
	public string TradeTime { get; set; }
	[JsonProperty("21")]
	public string DepthTime { get; set; }
	[JsonProperty("22")]
	public string TradeDate { get; set; }
	[JsonProperty("27")]
	public string BestAskPrice { get; set; }
	[JsonProperty("28")]
	public string BestBidPrice { get; set; }

	[JsonProperty("41")]
	public string AskPrice1 { get; set; }
	[JsonProperty("42")]
	public string AskPrice2 { get; set; }
	[JsonProperty("43")]
	public string AskPrice3 { get; set; }
	[JsonProperty("44")]
	public string AskPrice4 { get; set; }
	[JsonProperty("45")]
	public string AskPrice5 { get; set; }
	[JsonProperty("46")]
	public string AskPrice6 { get; set; }
	[JsonProperty("47")]
	public string AskPrice7 { get; set; }
	[JsonProperty("48")]
	public string AskPrice8 { get; set; }
	[JsonProperty("49")]
	public string AskPrice9 { get; set; }
	[JsonProperty("50")]
	public string AskPrice10 { get; set; }

	[JsonProperty("51")]
	public string BidPrice1 { get; set; }
	[JsonProperty("52")]
	public string BidPrice2 { get; set; }
	[JsonProperty("53")]
	public string BidPrice3 { get; set; }
	[JsonProperty("54")]
	public string BidPrice4 { get; set; }
	[JsonProperty("55")]
	public string BidPrice5 { get; set; }
	[JsonProperty("56")]
	public string BidPrice6 { get; set; }
	[JsonProperty("57")]
	public string BidPrice7 { get; set; }
	[JsonProperty("58")]
	public string BidPrice8 { get; set; }
	[JsonProperty("59")]
	public string BidPrice9 { get; set; }
	[JsonProperty("60")]
	public string BidPrice10 { get; set; }

	[JsonProperty("61")]
	public string AskVolume1 { get; set; }
	[JsonProperty("62")]
	public string AskVolume2 { get; set; }
	[JsonProperty("63")]
	public string AskVolume3 { get; set; }
	[JsonProperty("64")]
	public string AskVolume4 { get; set; }
	[JsonProperty("65")]
	public string AskVolume5 { get; set; }
	[JsonProperty("66")]
	public string AskVolume6 { get; set; }
	[JsonProperty("67")]
	public string AskVolume7 { get; set; }
	[JsonProperty("68")]
	public string AskVolume8 { get; set; }
	[JsonProperty("69")]
	public string AskVolume9 { get; set; }
	[JsonProperty("70")]
	public string AskVolume10 { get; set; }

	[JsonProperty("71")]
	public string BidVolume1 { get; set; }
	[JsonProperty("72")]
	public string BidVolume2 { get; set; }
	[JsonProperty("73")]
	public string BidVolume3 { get; set; }
	[JsonProperty("74")]
	public string BidVolume4 { get; set; }
	[JsonProperty("75")]
	public string BidVolume5 { get; set; }
	[JsonProperty("76")]
	public string BidVolume6 { get; set; }
	[JsonProperty("77")]
	public string BidVolume7 { get; set; }
	[JsonProperty("78")]
	public string BidVolume8 { get; set; }
	[JsonProperty("79")]
	public string BidVolume9 { get; set; }
	[JsonProperty("80")]
	public string BidVolume10 { get; set; }

	[JsonProperty("302")]
	public string SecurityName { get; set; }
	[JsonProperty("900")]
	public string OrderQuantity { get; set; }
	[JsonProperty("901")]
	public string OrderPrice { get; set; }
	[JsonProperty("902")]
	public string OrderBalance { get; set; }
	[JsonProperty("904")]
	public string OriginalOrderNumber { get; set; }
	[JsonProperty("905")]
	public string OrderDivision { get; set; }
	[JsonProperty("906")]
	public string TradeDivision { get; set; }
	[JsonProperty("907")]
	public string SideCode { get; set; }
	[JsonProperty("908")]
	public string OrderTime { get; set; }
	[JsonProperty("909")]
	public string TradeNumber { get; set; }
	[JsonProperty("910")]
	public string FillPrice { get; set; }
	[JsonProperty("911")]
	public string FillQuantity { get; set; }
	[JsonProperty("913")]
	public string OrderStatus { get; set; }
	[JsonProperty("930")]
	public string PositionQuantity { get; set; }
	[JsonProperty("931")]
	public string AveragePrice { get; set; }
	[JsonProperty("932")]
	public string PurchaseAmount { get; set; }
	[JsonProperty("933")]
	public string AvailableQuantity { get; set; }
	[JsonProperty("8018")]
	public string ProfitLoss { get; set; }
	[JsonProperty("8019")]
	public string ProfitLossRate { get; set; }
	[JsonProperty("8043")]
	public string CurrencyCode { get; set; }
	[JsonProperty("8046")]
	public string ExchangeCode { get; set; }
	[JsonProperty("9001")]
	public string SecurityCode { get; set; }
	[JsonProperty("9201")]
	public string AccountNumber { get; set; }
	[JsonProperty("9203")]
	public string OrderNumber { get; set; }
	[JsonProperty("50810")]
	public string StopPrice { get; set; }

	public string[] AskPrices => [AskPrice1, AskPrice2, AskPrice3, AskPrice4, AskPrice5, AskPrice6, AskPrice7, AskPrice8, AskPrice9, AskPrice10];
	public string[] AskVolumes => [AskVolume1, AskVolume2, AskVolume3, AskVolume4, AskVolume5, AskVolume6, AskVolume7, AskVolume8, AskVolume9, AskVolume10];
	public string[] BidPrices => [BidPrice1, BidPrice2, BidPrice3, BidPrice4, BidPrice5, BidPrice6, BidPrice7, BidPrice8, BidPrice9, BidPrice10];
	public string[] BidVolumes => [BidVolume1, BidVolume2, BidVolume3, BidVolume4, BidVolume5, BidVolume6, BidVolume7, BidVolume8, BidVolume9, BidVolume10];
}

readonly record struct KiwoomStreamSubscription(KiwoomAssetClasses AssetClass, string Code, string ExchangeCode, string Type);

sealed record KiwoomRealtimeMessage(KiwoomAssetClasses AssetClass, KiwoomStreamData Data);
