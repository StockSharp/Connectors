namespace StockSharp.TraderMade.Native;

sealed class TraderMadeRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly Uri _endpoint;
	private readonly string _key;
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTimeOffset _lastRequest;

	public TraderMadeRestClient(Uri endpoint, SecureString key,
		TimeSpan requestInterval, HttpMessageHandler handler = null)
	{
		if (endpoint is null || !endpoint.IsAbsoluteUri ||
			endpoint.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"TraderMade REST endpoint must be an absolute HTTPS URI.",
				nameof(endpoint));
		_endpoint = endpoint.AbsoluteUri.EndsWith("/",
			StringComparison.Ordinal)
				? endpoint
				: new(endpoint.AbsoluteUri + "/");
		_key = key.IsEmpty()
			? throw new ArgumentNullException(nameof(key))
			: key.UnSecure().Trim();
		_requestInterval = requestInterval >= TimeSpan.Zero &&
			requestInterval <= TimeSpan.FromMinutes(1)
				? requestInterval
				: throw new ArgumentOutOfRangeException(
					nameof(requestInterval));
		if (handler is null)
			handler = new HttpClientHandler
			{
				AutomaticDecompression =
					DecompressionMethods.GZip |
					DecompressionMethods.Deflate,
			};
		_http = new(handler, true)
		{
			Timeout = TimeSpan.FromMinutes(2),
		};
	}

	public async ValueTask<Dictionary<string, string>>
		GetCurrenciesAsync(CancellationToken cancellationToken)
		=> (await GetAsync("live_currencies_list", null,
			cancellationToken)).ToCurrencies();

	public async ValueTask<TraderMadeQuote[]> GetLiveAsync(
		IEnumerable<string> symbols,
		CancellationToken cancellationToken)
	{
		var value = (symbols ?? [])
			.Select(static symbol =>
				symbol.NormalizeTraderMadeSymbol())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Join(",");
		if (value.IsEmpty())
			throw new ArgumentException(
				"At least one symbol is required.",
				nameof(symbols));
		return (await GetAsync("live",
			new Dictionary<string, string>
			{
				["currency"] = value,
			}, cancellationToken)).ToLiveQuotes();
	}

	public async ValueTask<TraderMadeBar[]> GetBarsAsync(
		string symbol, DateTime from, DateTime to,
		TimeSpan timeFrame, bool weekend,
		CancellationToken cancellationToken)
	{
		var interval = timeFrame.ToTraderMadeInterval();
		var dateFormat = interval.Interval == "daily"
			? "yyyy-MM-dd"
			: "yyyy-MM-dd-HH:mm";
		return (await GetAsync("timeseries",
			new Dictionary<string, string>
			{
				["currency"] =
					symbol.NormalizeTraderMadeSymbol(),
				["start_date"] = from.ToString(dateFormat,
					CultureInfo.InvariantCulture),
				["end_date"] = to.ToString(dateFormat,
					CultureInfo.InvariantCulture),
				["format"] = "records",
				["interval"] = interval.Interval,
				["period"] = interval.Period.ToString(
					CultureInfo.InvariantCulture),
				["weekend"] =
					weekend.ToString().ToLowerInvariant(),
			}, cancellationToken)).ToBars();
	}

	private async ValueTask<JToken> GetAsync(string path,
		IReadOnlyDictionary<string, string> query,
		CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var wait = _requestInterval -
				(DateTimeOffset.UtcNow - _lastRequest);
			if (wait > TimeSpan.Zero)
				await Task.Delay(wait, cancellationToken);
			var values = new List<KeyValuePair<string, string>>
			{
				new("api_key", _key),
			};
			if (query is not null)
				values.AddRange(query);
			var suffix = "?" + values
				.Where(static pair => !pair.Value.IsEmpty())
				.Select(static pair =>
					$"{Uri.EscapeDataString(pair.Key)}=" +
					Uri.EscapeDataString(pair.Value))
				.Join("&");
			using var request = new HttpRequestMessage(
				HttpMethod.Get, new Uri(_endpoint, path + suffix));
			request.Headers.Accept.Add(
				new MediaTypeWithQualityHeaderValue(
					"application/json"));
			request.Headers.TryAddWithoutValidation(
				"User-Agent", "StockSharp TraderMade connector");
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			_lastRequest = DateTimeOffset.UtcNow;
			var text = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					$"TraderMade returned HTTP " +
						$"{(int)response.StatusCode}: {text}",
					null, response.StatusCode);
			JToken result;
			try
			{
				result = JToken.Parse(text);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"TraderMade returned invalid JSON.", error);
			}
			if (result is JObject errorObject &&
				(errorObject.Value<int?>("code") is 401 or 204 ||
					!errorObject.Value<string>("error").IsEmpty()))
				throw new InvalidOperationException(
					errorObject.Value<string>("message")
						.IsEmpty(errorObject.Value<string>("error"))
						.IsEmpty($"TraderMade error " +
							$"{errorObject.Value<int?>("code")}."));
			return result;
		}
		finally
		{
			_requestSync.Release();
		}
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}
}
