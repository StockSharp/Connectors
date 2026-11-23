namespace StockSharp.MoexISS.Native.Requests;

class MarketSecuritiesListRequest : BaseRequest<MarketSecuritiesListResponse>
{
	public MarketSecuritiesListRequest(HttpClient client, string engine, string market, int? first, string assets)
		: base(client, $"engines/{engine}/markets/{market}/securities.json", new()
		{
			{ "iss.only", "securities" },
			{ "first", first },
			{ "assets", assets },
		})
	{
	}
}
