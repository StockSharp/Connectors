namespace StockSharp.Alpaca.Native;

class RestCryptoClient : RestMarketDataClient
{
	public RestCryptoClient(SecureString key, SecureString secret)
		: base(key, secret)
	{
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(RestCryptoClient);

	public IAsyncEnumerable<Ohlc> GetOhlc(string symbol, string tf, DateTime start, DateTime end, long? limit, string location, CancellationToken cancellationToken)
		=> MakePagingRequest<Ohlc>($"v1beta3/crypto/{location}/bars", () =>
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

			return request;
		}, r => Deserialize<Ohlc>(r.bars), cancellationToken);

	public IAsyncEnumerable<Tick> GetTicks(string symbol, DateTime start, DateTime end, long? limit, string location, CancellationToken cancellationToken)
		=> MakePagingRequest<Tick>($"v1beta3/crypto/{location}/trades", () =>
		{
			var request = CreateRequest(Method.Get);

			request
				.AddQueryParameter("symbols", symbol)
				.AddQueryParameter("start", start.ToString(DateTimeFormat))
				.AddQueryParameter("end", end.ToString(DateTimeFormat))
			;

			if (limit is not null)
				request.AddQueryParameter("limit", limit.Value);

			return request;
		}, r => Deserialize<Tick>(r.trades), cancellationToken);

	public IAsyncEnumerable<Quote> GetQuotes(string symbol, DateTime start, DateTime end, long? limit, string location, CancellationToken cancellationToken)
		=> MakePagingRequest<Quote>($"v1beta3/crypto/{location}/quotes", () =>
		{
			var request = CreateRequest(Method.Get);

			request
				.AddQueryParameter("symbols", symbol)
				.AddQueryParameter("start", start.ToString(DateTimeFormat))
				.AddQueryParameter("end", end.ToString(DateTimeFormat))
			;

			if (limit is not null)
				request.AddQueryParameter("limit", limit.Value);

			return request;
		}, r => Deserialize<Quote>(r.quotes), cancellationToken);
}
