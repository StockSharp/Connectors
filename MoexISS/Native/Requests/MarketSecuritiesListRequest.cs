namespace StockSharp.MoexISS.Native.Requests;

class MarketSecuritiesListRequest : BaseRequest<MarketSecuritiesListResponse>
{
	public MarketSecuritiesListRequest(HttpClient client, string engine, string market, int? first, string assets)
		: base(client, $"engines/{engine}/markets/{market}/securities.json", new()
		{
			{ "first", first },
			{ "assets", assets },
		})
	{
	}
}