namespace StockSharp.KoreaInvestment.Native;

enum KisOperations
{
	DomesticQuote,
	DomesticOrderBook,
	DomesticDailyCandles,
	DomesticMinuteCandles,
	DomesticBuy,
	DomesticSell,
	DomesticCancel,
	DomesticBalance,
	DomesticExecutions,
	DerivativeQuote,
	DerivativeOrderBook,
	DerivativeDailyCandles,
	DerivativeMinuteCandles,
	DerivativeOrder,
	DerivativeNightOrder,
	DerivativeCancel,
	DerivativeNightCancel,
	DerivativeBalance,
	DerivativeExecutions,
	OverseasQuote,
	OverseasOrderBook,
	OverseasDailyCandles,
	OverseasMinuteCandles,
	OverseasBuyUs,
	OverseasSellUs,
	OverseasBuyHongKong,
	OverseasSellHongKong,
	OverseasBuyShanghai,
	OverseasSellShanghai,
	OverseasBuyShenzhen,
	OverseasSellShenzhen,
	OverseasBuyTokyo,
	OverseasSellTokyo,
	OverseasBuyVietnam,
	OverseasSellVietnam,
	OverseasCancel,
	OverseasBalance,
	OverseasExecutions,
}

enum KisRealtimeChannels
{
	DomesticTrade,
	DomesticDepth,
	DomesticOrderNotice,
	DerivativeTrade,
	DerivativeDepth,
	DerivativeOrderNotice,
	OptionTrade,
	OptionDepth,
	OverseasTrade,
	OverseasDepth,
	OverseasOrderNotice,
}

readonly record struct KisRoute(HttpMethod Method, string Path, string ProductionTrId, string SimulationTrId = null)
{
	public string GetTrId(bool isDemo)
		=> isDemo ? SimulationTrId.IsEmpty(ProductionTrId) : ProductionTrId;
}

static class KisRoutes
{
	public static KisRoute Get(KisOperations operation)
		=> operation switch
		{
			KisOperations.DomesticQuote => Get("/uapi/domestic-stock/v1/quotations/inquire-price", "FHKST01010100"),
			KisOperations.DomesticOrderBook => Get("/uapi/domestic-stock/v1/quotations/inquire-asking-price-exp-ccn", "FHKST01010200"),
			KisOperations.DomesticDailyCandles => Get("/uapi/domestic-stock/v1/quotations/inquire-daily-itemchartprice", "FHKST03010100"),
			KisOperations.DomesticMinuteCandles => Get("/uapi/domestic-stock/v1/quotations/inquire-time-dailychartprice", "FHKST03010230"),
			KisOperations.DomesticBuy => Post("/uapi/domestic-stock/v1/trading/order-cash", "TTTC0012U", "VTTC0012U"),
			KisOperations.DomesticSell => Post("/uapi/domestic-stock/v1/trading/order-cash", "TTTC0011U", "VTTC0011U"),
			KisOperations.DomesticCancel => Post("/uapi/domestic-stock/v1/trading/order-rvsecncl", "TTTC0013U", "VTTC0013U"),
			KisOperations.DomesticBalance => Get("/uapi/domestic-stock/v1/trading/inquire-balance", "TTTC8434R", "VTTC8434R"),
			KisOperations.DomesticExecutions => Get("/uapi/domestic-stock/v1/trading/inquire-daily-ccld", "TTTC0081R", "VTTC0081R"),
			KisOperations.DerivativeQuote => Get("/uapi/domestic-futureoption/v1/quotations/inquire-price", "FHMIF10000000"),
			KisOperations.DerivativeOrderBook => Get("/uapi/domestic-futureoption/v1/quotations/inquire-asking-price", "FHMIF10010000"),
			KisOperations.DerivativeDailyCandles => Get("/uapi/domestic-futureoption/v1/quotations/inquire-daily-fuopchartprice", "FHKIF03020100"),
			KisOperations.DerivativeMinuteCandles => Get("/uapi/domestic-futureoption/v1/quotations/inquire-time-fuopchartprice", "FHKIF03020200"),
			KisOperations.DerivativeOrder => Post("/uapi/domestic-futureoption/v1/trading/order", "TTTO1101U", "VTTO1101U"),
			KisOperations.DerivativeNightOrder => Post("/uapi/domestic-futureoption/v1/trading/order", "STTN1101U"),
			KisOperations.DerivativeCancel => Post("/uapi/domestic-futureoption/v1/trading/order-rvsecncl", "TTTO1103U", "VTTO1103U"),
			KisOperations.DerivativeNightCancel => Post("/uapi/domestic-futureoption/v1/trading/order-rvsecncl", "TTTN1103U"),
			KisOperations.DerivativeBalance => Get("/uapi/domestic-futureoption/v1/trading/inquire-balance", "CTFO6118R", "VTFO6118R"),
			KisOperations.DerivativeExecutions => Get("/uapi/domestic-futureoption/v1/trading/inquire-ccnl", "TTTO5201R", "VTTO5201R"),
			KisOperations.OverseasQuote => Get("/uapi/overseas-price/v1/quotations/price", "HHDFS00000300"),
			KisOperations.OverseasOrderBook => Get("/uapi/overseas-price/v1/quotations/inquire-asking-price", "HHDFS76200100"),
			KisOperations.OverseasDailyCandles => Get("/uapi/overseas-price/v1/quotations/dailyprice", "HHDFS76240000"),
			KisOperations.OverseasMinuteCandles => Get("/uapi/overseas-price/v1/quotations/inquire-time-itemchartprice", "HHDFS76950200"),
			KisOperations.OverseasBuyUs => Post("/uapi/overseas-stock/v1/trading/order", "TTTT1002U", "VTTT1002U"),
			KisOperations.OverseasSellUs => Post("/uapi/overseas-stock/v1/trading/order", "TTTT1006U", "VTTT1006U"),
			KisOperations.OverseasBuyHongKong => Post("/uapi/overseas-stock/v1/trading/order", "TTTS1002U", "VTTS1002U"),
			KisOperations.OverseasSellHongKong => Post("/uapi/overseas-stock/v1/trading/order", "TTTS1001U", "VTTS1001U"),
			KisOperations.OverseasBuyShanghai => Post("/uapi/overseas-stock/v1/trading/order", "TTTS0202U", "VTTS0202U"),
			KisOperations.OverseasSellShanghai => Post("/uapi/overseas-stock/v1/trading/order", "TTTS1005U", "VTTS1005U"),
			KisOperations.OverseasBuyShenzhen => Post("/uapi/overseas-stock/v1/trading/order", "TTTS0305U", "VTTS0305U"),
			KisOperations.OverseasSellShenzhen => Post("/uapi/overseas-stock/v1/trading/order", "TTTS0304U", "VTTS0304U"),
			KisOperations.OverseasBuyTokyo => Post("/uapi/overseas-stock/v1/trading/order", "TTTS0308U", "VTTS0308U"),
			KisOperations.OverseasSellTokyo => Post("/uapi/overseas-stock/v1/trading/order", "TTTS0307U", "VTTS0307U"),
			KisOperations.OverseasBuyVietnam => Post("/uapi/overseas-stock/v1/trading/order", "TTTS0311U", "VTTS0311U"),
			KisOperations.OverseasSellVietnam => Post("/uapi/overseas-stock/v1/trading/order", "TTTS0310U", "VTTS0310U"),
			KisOperations.OverseasCancel => Post("/uapi/overseas-stock/v1/trading/order-rvsecncl", "TTTT1004U", "VTTT1004U"),
			KisOperations.OverseasBalance => Get("/uapi/overseas-stock/v1/trading/inquire-balance", "TTTS3012R", "VTTS3012R"),
			KisOperations.OverseasExecutions => Get("/uapi/overseas-stock/v1/trading/inquire-ccnl", "TTTS3035R", "VTTS3035R"),
			_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
		};

	public static string Get(KisRealtimeChannels channel, bool isDemo)
		=> channel switch
		{
			KisRealtimeChannels.DomesticTrade => "H0STCNT0",
			KisRealtimeChannels.DomesticDepth => "H0STASP0",
			KisRealtimeChannels.DomesticOrderNotice => isDemo ? "H0STCNI9" : "H0STCNI0",
			KisRealtimeChannels.DerivativeTrade => "H0IFCNT0",
			KisRealtimeChannels.DerivativeDepth => "H0IFASP0",
			KisRealtimeChannels.DerivativeOrderNotice => "H0IFCNI0",
			KisRealtimeChannels.OptionTrade => "H0IOCNT0",
			KisRealtimeChannels.OptionDepth => "H0IOASP0",
			KisRealtimeChannels.OverseasTrade => "HDFSCNT0",
			KisRealtimeChannels.OverseasDepth => "HDFSASP0",
			KisRealtimeChannels.OverseasOrderNotice => isDemo ? "H0GSCNI9" : "H0GSCNI0",
			_ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
		};

	public static bool TryGetRealtime(string transactionId, bool isDemo, out KisRealtimeChannels channel)
	{
		foreach (var value in Enum.GetValues<KisRealtimeChannels>())
		{
			if (Get(value, isDemo).EqualsIgnoreCase(transactionId))
			{
				channel = value;
				return true;
			}
		}
		channel = default;
		return false;
	}

	private static KisRoute Get(string path, string productionTrId, string simulationTrId = null)
		=> new(HttpMethod.Get, path, productionTrId, simulationTrId);

	private static KisRoute Post(string path, string productionTrId, string simulationTrId = null)
		=> new(HttpMethod.Post, path, productionTrId, simulationTrId);
}
