namespace StockSharp.MoexISS.Native.Requests;

class EngineMarketSecurityCandlesRequest : BaseRequest<EngineMarketSecurityCandlesResponse>
{
	public EngineMarketSecurityCandlesRequest(HttpClient client, string engine, string market, string secCode, int interval, DateTime from, int? start)
		: base(client, $"engines/{engine}/markets/{market}/securities/{secCode}/candles.json", new()
		{
			{ "interval", interval },
			{ "from", from },
			{ "start", start },
		})
	{
	}
}