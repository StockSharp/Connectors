namespace StockSharp.MoexISS.Native.Requests;

class SecurityDividendsRequest : BaseRequest<SecurityDividendsResponse>
{
	public SecurityDividendsRequest(HttpClient client, string secCode)
		: base(client, $"securities/{secCode}/dividends.json")
	{
	}
}
