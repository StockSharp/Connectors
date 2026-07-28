namespace StockSharp.JQuants.Native;

sealed class JQuantsRestClient : BaseLogReceiver
{
	private readonly HttpClient _http;
	private readonly string _endpoint;
	private readonly string _apiKey;
	private readonly TimeSpan _requestInterval;
	private readonly int _maximumPages;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private DateTimeOffset _lastRequest;

	public JQuantsRestClient(string endpoint, SecureString apiKey,
		TimeSpan requestInterval, int maximumPages,
		HttpMessageHandler handler = null)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint))
			.Trim().TrimEnd('/');
		_apiKey = apiKey.IsEmpty()
			? throw new ArgumentNullException(nameof(apiKey))
			: apiKey.UnSecure();
		_requestInterval = requestInterval >= TimeSpan.Zero
			? requestInterval
			: throw new ArgumentOutOfRangeException(
				nameof(requestInterval));
		_maximumPages = maximumPages is >= 1 and <= 10000
			? maximumPages
			: throw new ArgumentOutOfRangeException(
				nameof(maximumPages));
		_http = handler is null ? new() : new(handler, true);
		_http.Timeout = TimeSpan.FromMinutes(5);
	}

	public ValueTask<JObject[]> GetEquitiesAsync(string code,
		DateTime? date, CancellationToken cancellationToken)
		=> GetPaginatedAsync("/equities/master",
			Parameters(
				("code", code),
				("date", FormatDate(date))),
			cancellationToken);

	public ValueTask<JObject[]> GetDailyBarsAsync(string code,
		DateTime from, DateTime to,
		CancellationToken cancellationToken)
		=> GetPaginatedAsync("/equities/bars/daily",
			Parameters(
				("code", code),
				("from", FormatDate(from)),
				("to", FormatDate(to))),
			cancellationToken);

	public ValueTask<JObject[]> GetMinuteBarsAsync(string code,
		DateTime from, DateTime to,
		CancellationToken cancellationToken)
		=> GetPaginatedAsync("/equities/bars/minute",
			Parameters(
				("code", code),
				("from", FormatDate(from)),
				("to", FormatDate(to))),
			cancellationToken);

	public ValueTask<JObject[]> GetFuturesAsync(DateTime date,
		CancellationToken cancellationToken)
		=> GetPaginatedAsync(
			"/derivatives/bars/daily/futures",
			Parameters(("date", FormatDate(date))),
			cancellationToken);

	public ValueTask<JObject[]> GetOptionsAsync(string code,
		DateTime date, CancellationToken cancellationToken)
		=> GetPaginatedAsync(
			"/derivatives/bars/daily/options",
			Parameters(
				("code", code),
				("date", FormatDate(date))),
			cancellationToken);

	public async ValueTask<JQuantsTrade[]> GetTradesAsync(
		string code, DateTime date,
		CancellationToken cancellationToken)
	{
		var response = await GetObjectAsync("/bulk/get",
			Parameters(
				("endpoint", "/equities/trades"),
				("date", FormatDate(date))),
			cancellationToken);
		var url = response.Value<string>("url")
			.ThrowIfEmpty("url");
		using var request = new HttpRequestMessage(HttpMethod.Get,
			url);
		using var download = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var bytes = await download.Content.ReadAsByteArrayAsync(
			cancellationToken);
		if (!download.IsSuccessStatusCode)
			throw new HttpRequestException(
				$"J-Quants bulk download returned HTTP " +
					$"{(int)download.StatusCode}.",
				null, download.StatusCode);
		return JQuantsExtensions.ParseTrades(
			DecodeCsv(bytes), code);
	}

	internal static string DecodeCsv(byte[] bytes)
	{
		if (bytes is null || bytes.Length == 0)
			return string.Empty;
		if (bytes.Length >= 2 &&
			bytes[0] == 0x1f && bytes[1] == 0x8b)
		{
			using var source = new MemoryStream(bytes);
			using var gzip = new GZipStream(source,
				CompressionMode.Decompress);
			using var target = new MemoryStream();
			gzip.CopyTo(target);
			bytes = target.ToArray();
		}
		return Encoding.UTF8.GetString(bytes)
			.TrimStart('\uFEFF');
	}

	private async ValueTask<JObject[]> GetPaginatedAsync(string path,
		Dictionary<string, string> parameters,
		CancellationToken cancellationToken)
	{
		var result = new List<JObject>();
		var seenKeys = new HashSet<string>(
			StringComparer.Ordinal);
		for (var page = 0; page < _maximumPages; page++)
		{
			var response = await GetObjectAsync(path, parameters,
				cancellationToken);
			if (response["data"] is JArray data)
				result.AddRange(data.OfType<JObject>());
			var key = response.Value<string>("pagination_key");
			if (key.IsEmpty())
				return [.. result];
			if (!seenKeys.Add(key))
				throw new InvalidDataException(
					"J-Quants repeated a pagination key.");
			parameters["pagination_key"] = key;
		}
		throw new InvalidDataException(
			$"J-Quants response exceeded {_maximumPages} pages.");
	}

	private async ValueTask<JObject> GetObjectAsync(string path,
		Dictionary<string, string> parameters,
		CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var wait = _requestInterval -
				(DateTimeOffset.UtcNow - _lastRequest);
			if (wait > TimeSpan.Zero)
				await Task.Delay(wait, cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				_endpoint + path + BuildQuery(parameters));
			request.Headers.Accept.Add(new("application/json"));
			request.Headers.UserAgent.ParseAdd(
				"StockSharp.JQuants/1.0");
			request.Headers.TryAddWithoutValidation(
				"x-api-key", _apiKey);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			_lastRequest = DateTimeOffset.UtcNow;
			var text = await response.Content.ReadAsStringAsync(
				cancellationToken);
			JObject value = null;
			if (!text.IsEmpty())
			{
				try
				{
					value = JObject.Parse(text);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						"J-Quants returned invalid JSON.", error);
				}
			}
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					ReadError(value).IsEmpty(
						$"J-Quants returned HTTP " +
							$"{(int)response.StatusCode}."),
					null, response.StatusCode);
			return value ?? new();
		}
		finally
		{
			_requestSync.Release();
		}
	}

	private static Dictionary<string, string> Parameters(
		params (string Name, string Value)[] values)
		=> values
			.Where(static value => !value.Value.IsEmpty())
			.ToDictionary(static value => value.Name,
				static value => value.Value,
				StringComparer.Ordinal);

	private static string BuildQuery(
		IEnumerable<KeyValuePair<string, string>> parameters)
	{
		var query = parameters
			.Where(static pair => !pair.Value.IsEmpty())
			.Select(static pair =>
				$"{Uri.EscapeDataString(pair.Key)}=" +
					Uri.EscapeDataString(pair.Value))
			.Join("&");
		return query.IsEmpty() ? string.Empty : $"?{query}";
	}

	private static string FormatDate(DateTime? value)
		=> value?.ToString("yyyy-MM-dd",
			CultureInfo.InvariantCulture);

	private static string ReadError(JObject value)
		=> value?.Value<string>("message")
			.IsEmpty(value?.Value<string>("error"));

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}
}
