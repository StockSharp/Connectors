namespace StockSharp.MoexISS.Native.Requests;

class MarketsRequest : BaseRequest<MarketsResponse>
{
	public MarketsRequest(HttpClient client, string engine)
		: base(client, $"engines/{engine}/markets.json")
	{
	}
}