namespace StockSharp.Kiwoom.Native;

static class KiwoomRoutes
{
	public const string Token = "/oauth2/token";
	public const string DomesticStockInfo = "/api/dostk/stkinfo";
	public const string DomesticMarket = "/api/dostk/mrkcond";
	public const string DomesticChart = "/api/dostk/chart";
	public const string DomesticAccount = "/api/dostk/acnt";
	public const string DomesticOrder = "/api/dostk/ordr";
	public const string UsStockInfo = "/api/us/stkinfo";
	public const string UsMarket = "/api/us/mrkcond";
	public const string UsChart = "/api/us/chart";
	public const string UsAccount = "/api/us/acnt";
	public const string UsOrder = "/api/us/ordr";

	public const string DomesticStockList = "ka10099";
	public const string DomesticSecurityInfo = "ka10001";
	public const string DomesticDepth = "ka10004";
	public const string DomesticMinuteCandles = "ka10080";
	public const string DomesticDailyCandles = "ka10081";
	public const string DomesticPositions = "kt00018";
	public const string DomesticOpenOrders = "ka10075";
	public const string DomesticExecutions = "ka10076";
	public const string DomesticBuy = "kt10000";
	public const string DomesticSell = "kt10001";
	public const string DomesticReplace = "kt10002";
	public const string DomesticCancel = "kt10003";

	public const string UsStockList = "usa10099";
	public const string UsQuote = "usa20100";
	public const string UsDepth = "usa20101";
	public const string UsMinuteCandles = "usa06011";
	public const string UsDailyCandles = "usa06012";
	public const string UsPositions = "ust21070";
	public const string UsOpenOrders = "ust21050";
	public const string UsExecutions = "ust21150";
	public const string UsBuy = "ust20000";
	public const string UsSell = "ust20001";
	public const string UsReplace = "ust20002";
	public const string UsCancel = "ust20003";
}
