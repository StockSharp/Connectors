namespace StockSharp.CoinMetrics.Native;

sealed class CoinMetricsApiException(HttpStatusCode statusCode, string message)
	: InvalidOperationException(message)
{
	public HttpStatusCode StatusCode { get; } = statusCode;
}

sealed class CoinMetricsRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 128 * 1024 * 1024;

	private readonly Uri _root;
	private readonly string _apiKey;
	private readonly HttpClient _client;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TimeSpan _requestInterval;
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private DateTime _lastRequestTime;
	private bool _isDisposed;

	public CoinMetricsRestClient(string endpoint, SecureString apiKey,
		TimeSpan requestInterval)
	{
		_root = ValidateEndpoint(endpoint);
		_apiKey = apiKey.IsEmpty() ? null : apiKey.UnSecure().Trim();
		if (requestInterval < TimeSpan.Zero ||
			requestInterval > TimeSpan.FromMinutes(1))
			throw new ArgumentOutOfRangeException(nameof(requestInterval));
		_requestInterval = requestInterval;
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip |
				DecompressionMethods.Deflate | DecompressionMethods.Brotli,
		};
		_client = new(handler, true)
		{
			BaseAddress = _root,
			Timeout = TimeSpan.FromSeconds(90),
		};
		_client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-CoinMetrics/1.0");
	}

	public override string Name => "CoinMetrics_REST";

	public async ValueTask ValidateAsync(string exchangeFilter,
		CancellationToken cancellationToken)
	{
		var markets = await GetMarketsAsync(null, exchangeFilter, 1,
			cancellationToken);
		if (markets.Length == 0)
			throw new InvalidDataException(
				"Coin Metrics did not return any accessible markets.");
	}

	public ValueTask<CoinMetricsMarket[]> GetMarketsAsync(string market,
		string exchange, int maximumItems,
		CancellationToken cancellationToken)
	{
		if (maximumItems is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(maximumItems));
		var query = new List<string>
		{
			"paging_from=" + CoinMetricsPagingDirections.Start.ToWire(),
			"page_size=" + Format(Math.Min(10000, maximumItems)),
		};
		if (!market.IsEmpty())
			query.Add("markets=" + Escape(
				CoinMetricsExtensions.NormalizeMarket(market)));
		if (!exchange.IsEmpty())
			query.Add("exchange=" + Escape(exchange.Trim()));
		return GetPagedAsync<CoinMetricsMarket>(
			"reference-data/markets?" + string.Join('&', query), maximumItems,
			cancellationToken);
	}

	public ValueTask<CoinMetricsTrade[]> GetTradesAsync(string market,
		DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
		=> GetHistoryAsync<CoinMetricsTrade>("market-trades", market, from,
			to, limit, 10000, string.Empty, cancellationToken);

	public ValueTask<CoinMetricsQuote[]> GetQuotesAsync(string market,
		DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
		=> GetHistoryAsync<CoinMetricsQuote>("market-quotes", market, from,
			to, limit, 10000, "&granularity=" +
			CoinMetricsGranularities.Raw.ToWire() + "&include_one_sided=true",
			cancellationToken);

	public ValueTask<CoinMetricsOrderBook[]> GetOrderBooksAsync(string market,
		DateTime from, DateTime to, int limit, int depth,
		CancellationToken cancellationToken)
	{
		if (depth is < 1 or > 30000)
			throw new ArgumentOutOfRangeException(nameof(depth));
		return GetHistoryAsync<CoinMetricsOrderBook>("market-orderbooks",
			market, from, to, limit, 1000,
			"&granularity=" + CoinMetricsGranularities.Raw.ToWire() +
			"&dataset=" + CoinMetricsBookDatasets.Snapshots.ToWire() +
			"&depth_limit=" + Format(depth),
			cancellationToken);
	}

	public ValueTask<CoinMetricsCandle[]> GetCandlesAsync(string market,
		TimeSpan timeFrame, DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
		=> GetHistoryAsync<CoinMetricsCandle>("market-candles", market, from,
			to, limit, 10000, "&frequency=" +
			Escape(timeFrame.ToFrequency().ToWire()),
			cancellationToken);

	private ValueTask<TItem[]> GetHistoryAsync<TItem>(string resource,
		string market, DateTime from, DateTime to, int limit, int maximumPageSize,
		string extraQuery, CancellationToken cancellationToken)
	{
		market = CoinMetricsExtensions.NormalizeMarket(market);
		ValidateHistoryArguments(from, to, limit);
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		var path = "timeseries/" + resource +
			"?markets=" + Escape(market) +
			"&start_time=" + Escape(from.FormatCoinMetricsTime()) +
			"&end_time=" + Escape(to.FormatCoinMetricsTime()) +
			"&start_inclusive=true&end_inclusive=false&paging_from=" +
			CoinMetricsPagingDirections.Start.ToWire() +
			"&page_size=" + Format(Math.Min(maximumPageSize, limit)) +
			"&limit_per_market=" + Format(limit) + extraQuery;
		return GetPagedAsync<TItem>(path, limit, cancellationToken);
	}

	private static void ValidateHistoryArguments(DateTime from, DateTime to,
		int limit)
	{
		if (limit is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, to,
			nameof(from));
	}

	private async ValueTask<TItem[]> GetPagedAsync<TItem>(string path,
		int maximumItems, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		var result = new List<TItem>(maximumItems.Min(4096));
		var visited = new HashSet<string>(StringComparer.Ordinal);
		var next = CreateUri(path);
		while (next is not null && result.Count < maximumItems)
		{
			ValidatePageUri(next);
			if (!visited.Add(next.AbsoluteUri) || visited.Count > 10000)
				throw new InvalidDataException(
					"Coin Metrics pagination contains a cycle or too many pages.");
			var page = await GetAsync<CoinMetricsPage<TItem>>(next,
				cancellationToken) ?? throw new InvalidDataException(
					"Coin Metrics returned an empty JSON response.");
			foreach (var item in page.Data ?? [])
			{
				if (item is not null)
					result.Add(item);
				if (result.Count == maximumItems)
					break;
			}
			next = ParseNextPage(page.NextPageUrl);
		}
		return [.. result];
	}

	private Uri CreateUri(string path)
	{
		var separator = path.Contains('?') ? '&' : '?';
		if (!_apiKey.IsEmpty())
			path += separator + "api_key=" + Escape(_apiKey);
		var uri = new Uri(_root, path);
		ValidatePageUri(uri);
		return uri;
	}

	private Uri ParseNextPage(string value)
	{
		if (value.IsEmpty())
			return null;
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
			throw new InvalidDataException(
				"Coin Metrics returned an invalid pagination URL.");
		ValidatePageUri(uri);
		return uri;
	}

	private void ValidatePageUri(Uri uri)
	{
		if (uri.Scheme != Uri.UriSchemeHttps ||
			!uri.Host.EqualsIgnoreCase(_root.Host) || uri.Port != _root.Port ||
			!uri.AbsolutePath.StartsWith(_root.AbsolutePath,
				StringComparison.OrdinalIgnoreCase) || !uri.UserInfo.IsEmpty() ||
			!uri.Fragment.IsEmpty())
			throw new InvalidDataException(
				"Coin Metrics pagination URL points outside the configured API root.");
		ValidateApiKey(uri);
	}

	private void ValidateApiKey(Uri uri)
	{
		var found = 0;
		string value = null;
		foreach (var part in uri.Query.TrimStart('?').Split('&',
			StringSplitOptions.RemoveEmptyEntries))
		{
			var separator = part.IndexOf('=');
			var name = separator < 0 ? part : part[..separator];
			if (!Uri.UnescapeDataString(name).EqualsIgnoreCase("api_key"))
				continue;
			found++;
			value = separator < 0
				? string.Empty
				: Uri.UnescapeDataString(part[(separator + 1)..]);
		}
		if (_apiKey.IsEmpty())
		{
			if (found != 0)
				throw new InvalidDataException(
					"Coin Metrics pagination unexpectedly introduced an API key.");
		}
		else if (found != 1 || !string.Equals(value, _apiKey,
			StringComparison.Ordinal))
			throw new InvalidDataException(
				"Coin Metrics pagination changed the configured API key.");
	}

	private async ValueTask<T> GetAsync<T>(Uri uri,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			using var response = await SendAsync(uri, cancellationToken);
			var bytes = await ReadResponseAsync(response, cancellationToken);
			if (bytes.Length == 0)
				return default;
			try
			{
				return JsonConvert.DeserializeObject<T>(
					Encoding.UTF8.GetString(bytes), _settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Coin Metrics returned invalid JSON.", error);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async ValueTask<HttpResponseMessage> SendAsync(Uri uri,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await EnforceRequestIntervalAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			HttpResponseMessage response;
			try
			{
				response = await _client.SendAsync(request,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (Exception error) when (error is HttpRequestException or
				TaskCanceledException)
			{
				if (cancellationToken.IsCancellationRequested)
					throw;
				if (attempt < 3)
				{
					await DelayRetryAsync(attempt, null, cancellationToken);
					continue;
				}
				throw new IOException(
					"Coin Metrics REST transport failed: " +
					Redact(error.Message));
			}
			_lastRequestTime = DateTime.UtcNow;
			if (response.IsSuccessStatusCode)
				return response;
			if (attempt < 3 && IsTransient(response.StatusCode))
			{
				var delay = response.Headers.RetryAfter?.Delta;
				response.Dispose();
				await DelayRetryAsync(attempt, delay, cancellationToken);
				continue;
			}
			using (response)
			{
				var bytes = await ReadResponseAsync(response, cancellationToken);
				throw CreateException(response.StatusCode, bytes);
			}
		}
	}

	private async ValueTask EnforceRequestIntervalAsync(
		CancellationToken cancellationToken)
	{
		var remaining = _requestInterval - (DateTime.UtcNow - _lastRequestTime);
		if (remaining > TimeSpan.Zero)
			await Task.Delay(remaining, cancellationToken);
	}

	private static async ValueTask DelayRetryAsync(int attempt,
		TimeSpan? serverDelay, CancellationToken cancellationToken)
	{
		var delay = serverDelay ?? TimeSpan.FromMilliseconds(500 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		if (delay > TimeSpan.FromSeconds(30))
			delay = TimeSpan.FromSeconds(30);
		await Task.Delay(delay, cancellationToken);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			statusCode is HttpStatusCode.InternalServerError or
			HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
			HttpStatusCode.GatewayTimeout;

	private CoinMetricsApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		CoinMetricsErrorResponse response = null;
		if (body.Length > 0)
		{
			try
			{
				response = JsonConvert.DeserializeObject<CoinMetricsErrorResponse>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var detail = response?.Error?.Message
			.IsEmpty(response?.Error?.Type)
			.IsEmpty($"HTTP {(int)statusCode} ({statusCode})");
		return new(statusCode,
			"Coin Metrics request failed: " + Redact(detail));
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Coin Metrics response is too large ({length} bytes).");
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (bytes.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Coin Metrics response is too large ({bytes.Length} bytes).");
		return bytes;
	}

	private static Uri ValidateEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"A valid HTTPS Coin Metrics API root is required.",
				nameof(endpoint));
		if (!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"Coin Metrics API root cannot contain a query or fragment.",
				nameof(endpoint));
		return uri;
	}

	private string Redact(string value)
		=> _apiKey.IsEmpty() || value.IsEmpty()
			? value
			: value.Replace(_apiKey, "***", StringComparison.Ordinal);

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string Format(int value)
		=> value.ToString(CultureInfo.InvariantCulture);

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_client.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
