namespace StockSharp.PolygonIO.Native;

using System.Runtime.CompilerServices;

class RestClient : RestBaseApiClient
{
	private const string _dateFormat = "yyyy-MM-dd";
	private static readonly SemaphoreSlim _requestGate = new(1, 1);

	public RestClient(HttpClient http, string domain)
		: base(http, new NewtonsoftMediaTypeFormatter(), new NewtonsoftMediaTypeFormatter())
	{
		BaseAddress = $"https://api.{domain}".To<Uri>();
		ExtractBadResponse = true;
	}

	private async Task<Response<T>> Do<T>(TimeSpan delay, Uri url, CancellationToken cancellationToken)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			if (delay > TimeSpan.Zero)
				await delay.Delay(cancellationToken);
			return await DoAsync<Response<T>>(HttpMethod.Get, url, default, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			throw new InvalidOperationException($"Error downloading '{url.GetLeftPart(UriPartial.Path)}'.", ex);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	public async IAsyncEnumerable<Ticker> GetTickers(TimeSpan delay, string type, string search, bool active, long limit, string apiKey, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var url = new Url(BaseAddress, "/v3/reference/tickers");

		var qs = url.QueryString;

		if (!type.IsEmpty())
			qs.Append(nameof(type), type);

		if (!search.IsEmpty())
			qs.Append(nameof(search), search);

		qs
			.Append(nameof(active), active)
			.Append(nameof(limit), limit)
			.Append(nameof(apiKey), apiKey)
		;

		var response = await Do<Ticker>(delay, url, cancellationToken);

		await foreach (var item in ProcessResponse(delay, response, apiKey, cancellationToken).WithEnforcedCancellation(cancellationToken))
			yield return item;
	}

	public async IAsyncEnumerable<News> GetNews(TimeSpan delay, string ticker, DateTime? published, string order, long limit, string apiKey, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var url = new Url(BaseAddress, "/v2/reference/news");

		var qs = url.QueryString;

		if (!ticker.IsEmpty())
			qs.Append(nameof(ticker), ticker);

		qs
			.Append(nameof(order), order)
			.Append("sort", "published_utc")
			.Append(nameof(limit), limit)
			.Append(nameof(apiKey), apiKey)
		;

		if (published is not null)
			qs.Append("published_utc.gte", published.Value.ToString(_dateFormat));

		var response = await Do<News>(delay, url, cancellationToken);

		await foreach (var item in ProcessResponse(delay, response, apiKey, cancellationToken).WithEnforcedCancellation(cancellationToken))
			yield return item;
	}

	public async IAsyncEnumerable<Dividend> GetDividends(TimeSpan delay, string ticker, DateTime? payDate, string order, long limit, string apiKey, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var url = new Url(BaseAddress, "/v3/reference/dividends");

		var qs = url.QueryString;

		qs
			.Append(nameof(ticker), ticker)
			.Append(nameof(order), order)
			.Append("sort", "pay_date")
			.Append(nameof(limit), limit)
			.Append(nameof(apiKey), apiKey)
		;

		if (payDate is not null)
			qs.Append("pay_date.gte", payDate.Value.ToString(_dateFormat));

		var response = await Do<Dividend>(delay, url, cancellationToken);

		await foreach (var item in ProcessResponse(delay, response, apiKey, cancellationToken).WithEnforcedCancellation(cancellationToken))
			yield return item;
	}

	public async IAsyncEnumerable<Bar> GetBars(TimeSpan delay, string ticker, string timespan, int multiplier, DateTime from, DateTime to, bool adjusted, string order, long limit, string apiKey, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var url = new Url(BaseAddress, $"/v2/aggs/ticker/{ticker}/range/{multiplier}/{timespan}/{(long)from.ToUnix(false)}/{(long)to.ToUnix(false)}");

		url.QueryString
			.Append(nameof(adjusted), adjusted)
			.Append(nameof(order), order)
			.Append(nameof(limit), limit)
			.Append(nameof(apiKey), apiKey)
		;

		var response = await Do<Bar>(delay, url, cancellationToken);

		await foreach (var item in ProcessResponse(delay, response, apiKey, cancellationToken).WithEnforcedCancellation(cancellationToken))
			yield return item;
	}

	public async IAsyncEnumerable<Trade> GetTrades(TimeSpan delay, string ticker, DateTime timestamp, string order, long limit, string apiKey, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var url = new Url(BaseAddress, $"/v3/trades/{ticker}");

		url.QueryString
			.Append("timestamp.gte", timestamp.GetUnixDiff().ToNanoseconds())
			.Append(nameof(order), order)
			.Append(nameof(limit), limit)
			.Append(nameof(apiKey), apiKey)
		;

		var response = await Do<Trade>(delay, url, cancellationToken);

		await foreach (var item in ProcessResponse(delay, response, apiKey, cancellationToken).WithEnforcedCancellation(cancellationToken))
			yield return item;
	}

	public async IAsyncEnumerable<Quote> GetQuotes(TimeSpan delay, string ticker, DateTime timestamp, string order, long limit, string apiKey, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var url = new Url(BaseAddress, $"/v3/quotes/{ticker}");

		url.QueryString
			.Append("timestamp.gte", timestamp.GetUnixDiff().ToNanoseconds())
			.Append(nameof(order), order)
			.Append(nameof(limit), limit)
			.Append(nameof(apiKey), apiKey)
		;

		var response = await Do<Quote>(delay, url, cancellationToken);

		await foreach (var item in ProcessResponse(delay, response, apiKey, cancellationToken).WithEnforcedCancellation(cancellationToken))
			yield return item;
	}

	private async IAsyncEnumerable<TResult> ProcessResponse<TResult>(TimeSpan delay, Response<TResult> response, string apiKey, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		while (true)
		{
			if (!response.Status.IsEmpty() && !response.Status.EqualsIgnoreCase("OK") && !response.Status.EqualsIgnoreCase("DELAYED"))
				throw new InvalidOperationException($"{response.Status}: {response.Error ?? response.Message}");

			if (response.Results is null)
				break;

			foreach (var res in response.Results)
				yield return res;

			if (response.NextUrl.IsEmpty())
				yield break;

			response = await Do<TResult>(delay, $"{response.NextUrl}&apiKey={apiKey}".To<Uri>(), cancellationToken);
		}
	}
}
