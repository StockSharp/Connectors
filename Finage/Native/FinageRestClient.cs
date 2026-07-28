namespace StockSharp.Finage.Native;

sealed class FinageRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly Uri _endpoint;
	private readonly string _key;
	private readonly TimeSpan _requestInterval;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTimeOffset _lastRequest;

	public FinageRestClient(Uri endpoint, SecureString key,
		TimeSpan requestInterval, HttpMessageHandler handler = null)
	{
		if (endpoint is null || !endpoint.IsAbsoluteUri ||
			endpoint.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"Finage REST endpoint must be an absolute HTTPS URI.",
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

	public async ValueTask<FinageInstrument[]> GetSymbolsAsync(
		string search, int maximum,
		CancellationToken cancellationToken)
	{
		if (maximum <= 0)
			return [];

		var result = new List<FinageInstrument>();
		var known = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase);

		for (var page = 1; result.Count < maximum; page++)
		{
			var items = (await GetAsync("symbol-list/forex",
				new Dictionary<string, string>
				{
					["page"] = page.ToString(
						CultureInfo.InvariantCulture),
					["search"] = search,
				}, cancellationToken)).ToInstruments();

			foreach (var item in items)
				if (known.Add(item.Symbol))
				{
					result.Add(item);
					if (result.Count >= maximum)
						break;
				}

			if (items.Length < 500)
				break;
		}

		return [.. result];
	}

	public async ValueTask<FinageQuote> GetQuoteAsync(
		string symbol, CancellationToken cancellationToken)
		=> (await GetAsync(
			"last/forex/" + Uri.EscapeDataString(
				symbol.NormalizeFinageSymbol()),
			null, cancellationToken)).ToQuote();

	public async ValueTask<FinageBar[]> GetBarsAsync(
		string symbol, DateTime from, DateTime to,
		TimeSpan timeFrame,
		CancellationToken cancellationToken)
	{
		var interval = timeFrame.ToFinageInterval();
		var path = "agg/forex/" +
			Uri.EscapeDataString(symbol.NormalizeFinageSymbol()) +
			$"/{interval.Multiplier}/{interval.Unit}/" +
			$"{from:yyyy-MM-dd}/{to:yyyy-MM-dd}";

		return (await GetAsync(path,
			new Dictionary<string, string>
			{
				["limit"] = "50000",
				["sort"] = "asc",
				["date_format"] = "ts",
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

			var values = new List<KeyValuePair<string, string>>();
			if (query is not null)
				values.AddRange(query);
			values.Add(new("apikey", _key));

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
				"User-Agent", "StockSharp Finage connector");

			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			_lastRequest = DateTimeOffset.UtcNow;
			var text = await response.Content.ReadAsStringAsync(
				cancellationToken);

			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					$"Finage returned HTTP " +
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
					"Finage returned invalid JSON.", error);
			}

			if (result is JObject errorObject)
			{
				var status = errorObject.Value<int?>("status_code") ??
					errorObject.Value<int?>("status");
				var errorText =
					errorObject.Value<string>("error");

				if (status >= 400 || !errorText.IsEmpty())
					throw new InvalidOperationException(
						errorObject.Value<string>("message")
							.IsEmpty(errorText)
							.IsEmpty($"Finage error {status}."));
			}

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
