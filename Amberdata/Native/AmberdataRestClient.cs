namespace StockSharp.Amberdata.Native;

sealed class AmberdataApiException(HttpStatusCode statusCode, string message)
	: InvalidOperationException(message)
{
	public HttpStatusCode StatusCode { get; } = statusCode;
}

sealed class AmberdataRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 128 * 1024 * 1024;
	private const string _apiVersion = "2023-09-30";
	private static readonly TimeSpan _maximumHistoryRange =
		TimeSpan.FromDays(731);
	private static readonly TimeSpan _maximumBookRange =
		TimeSpan.FromDays(540);

	private readonly Uri _root;
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

	public AmberdataRestClient(string endpoint, SecureString apiKey,
		TimeSpan requestInterval)
	{
		_root = ValidateEndpoint(endpoint);
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
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
			"StockSharp-Amberdata/1.0");
		_client.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key",
			apiKey.UnSecure().Trim());
		_client.DefaultRequestHeaders.TryAddWithoutValidation("api-version",
			_apiVersion);
	}

	public override string Name => "Amberdata_REST";

	public async ValueTask ValidateAsync(string exchangeFilter,
		bool isInactiveIncluded, CancellationToken cancellationToken)
	{
		var references = await GetReferencesAsync(exchangeFilter, null,
			isInactiveIncluded, 1, cancellationToken);
		if (references.Length == 0)
			throw new InvalidDataException(
				"Amberdata did not return any accessible spot instruments.");
	}

	public ValueTask<AmberdataReference[]> GetReferencesAsync(
		string exchangeFilter, string instrumentFilter, bool isInactiveIncluded,
		int maximumItems, CancellationToken cancellationToken)
	{
		if (maximumItems is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(maximumItems));
		var query = new List<string>
		{
			"includeInactive=" + (isInactiveIncluded ? "true" : "false"),
			"includeOriginalReference=false",
			"timeFormat=milliseconds",
		};
		if (!exchangeFilter.IsEmpty())
			query.Add("exchange=" + Escape(exchangeFilter));
		if (!instrumentFilter.IsEmpty())
			query.Add("instrument=" + Escape(instrumentFilter));
		return GetPagedAsync<AmberdataReference>(
			"spot/exchanges/reference?" + string.Join('&', query), maximumItems,
			cancellationToken);
	}

	public ValueTask<AmberdataTrade[]> GetTradesAsync(
		AmberdataSecurityKey security, DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
		=> GetHistoryAsync<AmberdataTrade>("spot/trades/", security, from, to,
			limit, _maximumHistoryRange, string.Empty, cancellationToken);

	public ValueTask<AmberdataTicker[]> GetTickersAsync(
		AmberdataSecurityKey security, DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
		=> GetHistoryAsync<AmberdataTicker>("spot/tickers/", security, from, to,
			limit, _maximumHistoryRange, string.Empty, cancellationToken);

	public ValueTask<AmberdataBookSnapshot[]> GetOrderBooksAsync(
		AmberdataSecurityKey security, DateTime from, DateTime to, int limit,
		int depth, CancellationToken cancellationToken)
	{
		if (depth is < 1 or > 5000)
			throw new ArgumentOutOfRangeException(nameof(depth));
		return GetHistoryAsync<AmberdataBookSnapshot>(
			"spot/order-book-snapshots/", security, from, to, limit,
			_maximumBookRange, "&maxLevel=" + Format(depth), cancellationToken);
	}

	public ValueTask<AmberdataOhlcv[]> GetOhlcvAsync(
		AmberdataSecurityKey security, AmberdataTimeIntervals interval,
		DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
	{
		if (interval == AmberdataTimeIntervals.Unknown)
			throw new ArgumentOutOfRangeException(nameof(interval));
		return GetHistoryAsync<AmberdataOhlcv>("spot/ohlcv/", security, from,
			to, limit, _maximumHistoryRange, "&timeInterval=" + Escape(
				AmberdataEnumConverter<AmberdataTimeIntervals>.ToWire(interval)),
			cancellationToken);
	}

	private async ValueTask<TItem[]> GetHistoryAsync<TItem>(string resource,
		AmberdataSecurityKey security, DateTime from, DateTime to, int limit,
		TimeSpan maximumRange, string extraQuery,
		CancellationToken cancellationToken)
	{
		security = security.Normalize();
		ValidateHistoryArguments(from, to, limit);
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		var result = new List<TItem>(limit.Min(4096));
		var cursor = from;
		while (cursor < to && result.Count < limit)
		{
			var chunkEnd = to - cursor > maximumRange
				? cursor + maximumRange
				: to;
			var path = resource + Escape(security.Instrument) +
				"?exchange=" + Escape(security.Exchange) +
				"&startDate=" + Escape(cursor.FormatAmberdataTime()) +
				"&endDate=" + Escape(chunkEnd.FormatAmberdataTime()) +
				"&timeFormat=milliseconds" + extraQuery;
			var items = await GetPagedAsync<TItem>(path, limit - result.Count,
				cancellationToken);
			result.AddRange(items);
			cursor = chunkEnd;
		}
		return [.. result];
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
		var next = new Uri(_root, path);
		while (next is not null && result.Count < maximumItems)
		{
			ValidatePageUri(next);
			if (!visited.Add(next.AbsoluteUri) || visited.Count > 10000)
				throw new InvalidDataException(
					"Amberdata pagination contains a cycle or too many pages.");
			var response = await GetAsync<AmberdataResponse<
				AmberdataPagedPayload<TItem>>>(next, cancellationToken);
			ValidateResponse(response);
			var payload = response.Payload ?? throw new InvalidDataException(
				"Amberdata response payload is missing.");
			foreach (var item in payload.Data ?? [])
			{
				if (item is not null)
					result.Add(item);
				if (result.Count == maximumItems)
					break;
			}
			next = ParseNextPage(payload.Metadata?.Next);
		}
		return [.. result];
	}

	private static void ValidateResponse<TPayload>(
		AmberdataResponse<TPayload> response)
	{
		if (response is null)
			throw new InvalidDataException(
				"Amberdata returned an empty JSON response.");
		if (response.Status is < 200 or >= 300)
			throw new InvalidOperationException(
				$"Amberdata response {response.Status}: " +
				response.Description.IsEmpty(response.Title).IsEmpty("request failed"));
	}

	private Uri ParseNextPage(string value)
	{
		if (value.IsEmpty())
			return null;
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
			throw new InvalidDataException(
				"Amberdata returned an invalid pagination URL.");
		ValidatePageUri(uri);
		return uri;
	}

	private void ValidatePageUri(Uri uri)
	{
		if (uri.Scheme != Uri.UriSchemeHttps ||
			!uri.Host.EqualsIgnoreCase(_root.Host) || uri.Port != _root.Port ||
			!uri.Fragment.IsEmpty())
			throw new InvalidDataException(
				"Amberdata pagination URL points outside the configured API origin.");
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
					"Amberdata returned invalid JSON.", error);
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
			catch (Exception error) when (attempt < 3 &&
				!cancellationToken.IsCancellationRequested &&
				error is HttpRequestException or TaskCanceledException)
			{
				await DelayRetryAsync(attempt, null, cancellationToken);
				continue;
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
		var delay = serverDelay ??
			TimeSpan.FromMilliseconds(500 * (1 << attempt));
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

	private AmberdataApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		AmberdataError error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<AmberdataError>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var detail = error?.Description.IsEmpty(error?.Title)
			.IsEmpty($"HTTP {(int)statusCode} ({statusCode})");
		return new(statusCode, $"Amberdata request failed: {detail}");
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Amberdata response is too large ({length} bytes).");
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (bytes.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Amberdata response is too large ({bytes.Length} bytes).");
		return bytes;
	}

	private static Uri ValidateEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host.IsEmpty())
			throw new ArgumentException(
				"A valid HTTPS Amberdata market API endpoint is required.",
				nameof(endpoint));
		if (!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"Amberdata endpoint cannot contain a query or fragment.",
				nameof(endpoint));
		return uri;
	}

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
