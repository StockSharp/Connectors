namespace StockSharp.Alpaca.Native;

/// <summary>
/// Option market data.
/// </summary>
/// <remarks>
/// Bars and trades answer in the same envelope as the equity ones — an object keyed by symbol — so the
/// paging and deserialization the base class already does apply unchanged, and a candle of an option is
/// read by the same model as a candle of a share.
///
/// Two things differ from the equity endpoints and both are refusals rather than preferences. They take
/// no <c>feed</c> parameter: sending one is answered with <c>unexpected query parameter(s): feed</c>,
/// so the feed is passed only where it is accepted. And there is no historical quotes endpoint at all —
/// only the latest quote — so a caller wanting the spread of an option as it was last month cannot have
/// it from here, and is told so rather than handed an empty answer.
/// </remarks>
class RestOptionClient : RestMarketDataClient
{
	public RestOptionClient(string endpoint, SecureString key, SecureString secret)
		: base(endpoint, key, secret)
	{
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(RestOptionClient);

	public IAsyncEnumerable<Ohlc> GetOhlc(string symbol, string tf, DateTime start, DateTime end, long? limit, CancellationToken cancellationToken)
		=> MakePagingRequest<Ohlc>("v1beta1/options/bars", () =>
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

	public IAsyncEnumerable<Tick> GetTicks(string symbol, DateTime start, DateTime end, long? limit, CancellationToken cancellationToken)
		=> MakePagingRequest<Tick>("v1beta1/options/trades", () =>
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

	/// <summary>
	/// The quote each contract currently shows.
	/// </summary>
	/// <remarks>
	/// The only quotes an option has here. Unlike the bar and trade endpoints this one does take a feed,
	/// and asking for a tape the account is not entitled to is refused rather than downgraded.
	/// </remarks>
	public async Task<IDictionary<string, Quote>> GetLatestQuotes(IEnumerable<string> symbols, string feed, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddQueryParameter("symbols", symbols.JoinComma());

		if (!feed.IsEmpty())
			request.AddQueryParameter("feed", feed);

		dynamic result = await MakeRequest<object>("v1beta1/options/quotes/latest", request, cancellationToken);

		return ((JToken)result.quotes).ToObject<Dictionary<string, Quote>>();
	}
}
