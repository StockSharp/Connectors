namespace StockSharp.Alpaca.Native;

class RestStockClient : RestMarketDataClient
{
	public RestStockClient(SecureString key, SecureString secret)
		: base(key, secret)
	{
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(RestStockClient);

	public IAsyncEnumerable<Ohlc> GetOhlc(string symbol, string tf, DateTime start, DateTime end, long? limit, string feed, CancellationToken cancellationToken)
		=> MakePagingRequest<Ohlc>("v2/stocks/bars", () =>
		{
			var request = CreateRequest(Method.Get);

			request
				.AddQueryParameter("symbols", symbol)
				.AddQueryParameter("timeframe", tf)
				.AddQueryParameter("start", start.ToString(DateTimeFormat))
				.AddQueryParameter("end", end.ToString(DateTimeFormat))
			;

			if (limit is not null)
				request.AddQueryParameter("limit", limit.Value);

			if (!feed.IsEmpty())
				request.AddQueryParameter("feed", feed);

			return request;
		}, r => Deserialize<Ohlc>(r.bars), cancellationToken);

	public IAsyncEnumerable<Tick> GetTicks(string symbol, DateTime start, DateTime end, long? limit, string feed, CancellationToken cancellationToken)
		=> MakePagingRequest<Tick>($"v2/stocks/trades", () =>
		{
			var request = CreateRequest(Method.Get);

			request
				.AddQueryParameter("symbols", symbol)
				.AddQueryParameter("start", start.ToString(DateTimeFormat))
				.AddQueryParameter("end", end.ToString(DateTimeFormat))
			;

			if (limit is not null)
				request.AddQueryParameter("limit", limit.Value);

			if (!feed.IsEmpty())
				request.AddQueryParameter("feed", feed);

			return request;
		}, r => Deserialize<Tick>(r.trades), cancellationToken);

	public IAsyncEnumerable<Quote> GetQuotes(string symbol, DateTime start, DateTime end, long? limit, string feed, CancellationToken cancellationToken)
		=> MakePagingRequest<Quote>($"v2/stocks/quotes", () =>
		{
			var request = CreateRequest(Method.Get);

			request
				.AddQueryParameter("symbols", symbol)
				.AddQueryParameter("start", start.ToString(DateTimeFormat))
				.AddQueryParameter("end", end.ToString(DateTimeFormat))
			;

			if (limit is not null)
				request.AddQueryParameter("limit", limit.Value);

			if (!feed.IsEmpty())
				request.AddQueryParameter("feed", feed);

			return request;
		}, r => Deserialize<Quote>(r.quotes), cancellationToken);
}