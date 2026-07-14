namespace StockSharp.Alpaca.Native;

class RestNewsClient : RestMarketDataClient
{
	public RestNewsClient(SecureString key, SecureString secret)
		: base(key, secret)
	{
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(RestNewsClient);

	public IAsyncEnumerable<News> GetNews(string symbol, DateTime start, DateTime end, long? limit, bool? includeContent, CancellationToken cancellationToken)
		=> MakePagingRequest("v1beta1/news", () =>
		{
			var request = CreateRequest(Method.Get);

			if (symbol.IsEmpty())
				request.AddQueryParameter("symbols", symbol);

			request
				.AddQueryParameter("start", start.ToString(DateTimeFormat))
				.AddQueryParameter("end", end.ToString(DateTimeFormat))
			;

			if (limit is not null)
				request.AddQueryParameter("limit", limit.Value);

			if (includeContent is not null)
				request.AddQueryParameter("include_content", includeContent.Value);

			return request;
		}, r => ((JToken)r.news).DeserializeObject<IEnumerable<News>>(), cancellationToken);
}