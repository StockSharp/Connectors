namespace StockSharp.CoinApi.Native;

sealed class CoinApiApiException(HttpStatusCode statusCode, string message)
	: InvalidOperationException(message)
{
	public HttpStatusCode StatusCode { get; } = statusCode;
}

sealed class CoinApiRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 128 * 1024 * 1024;
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

	public CoinApiRestClient(string endpoint, SecureString apiKey,
		TimeSpan requestInterval)
	{
		var root = ValidateEndpoint(endpoint);
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
			BaseAddress = root,
			Timeout = TimeSpan.FromSeconds(90),
		};
		_client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-CoinAPI/1.0");
		_client.DefaultRequestHeaders.TryAddWithoutValidation("X-CoinAPI-Key",
			apiKey.UnSecure().Trim());
	}

	public override string Name => "CoinAPI_REST";

	public async ValueTask ValidateAsync(CancellationToken cancellationToken)
	{
		var periods = await GetPeriodsAsync(cancellationToken);
		var required = new[]
		{
			CoinApiPeriodIds.Second1,
			CoinApiPeriodIds.Minute1,
			CoinApiPeriodIds.Hour1,
			CoinApiPeriodIds.Day1,
		};
		if (periods.Length == 0 || required.Any(period =>
			!periods.Any(item => item?.PeriodId == period)))
			throw new InvalidDataException(
				"CoinAPI did not return the required OHLCV periods.");
	}

	public async ValueTask<CoinApiSymbol[]> GetSymbolsAsync(
		string exchangeFilter, string symbolFilter, string assetFilter,
		int maximumItems, CancellationToken cancellationToken)
	{
		if (maximumItems is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(maximumItems));
		var query = new List<string>();
		if (!exchangeFilter.IsEmpty())
			query.Add("filter_exchange_id=" + Escape(exchangeFilter));
		if (!symbolFilter.IsEmpty())
			query.Add("filter_symbol_id=" + Escape(symbolFilter));
		if (!assetFilter.IsEmpty())
			query.Add("filter_asset_id=" + Escape(assetFilter));
		var path = "v1/symbols" + (query.Count == 0
			? string.Empty
			: "?" + string.Join('&', query));

		ObjectDisposedException.ThrowIf(_isDisposed, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			using var response = await SendAsync(path, cancellationToken);
			await using var stream = await response.Content.ReadAsStreamAsync(
				cancellationToken);
			using var textReader = new StreamReader(stream, Encoding.UTF8, true,
				16 * 1024, false);
			using var jsonReader = new JsonTextReader(textReader)
			{
				DateParseHandling = DateParseHandling.None,
				FloatParseHandling = FloatParseHandling.Decimal,
			};
			if (!await jsonReader.ReadAsync(cancellationToken) ||
				jsonReader.TokenType != JsonToken.StartArray)
				throw new InvalidDataException(
					"CoinAPI symbols response is not a JSON array.");
			var serializer = JsonSerializer.Create(_settings);
			var result = new List<CoinApiSymbol>(maximumItems.Min(4096));
			while (result.Count < maximumItems &&
				await jsonReader.ReadAsync(cancellationToken))
			{
				if (jsonReader.TokenType == JsonToken.EndArray)
					break;
				if (jsonReader.TokenType == JsonToken.Null)
					continue;
				if (jsonReader.TokenType != JsonToken.StartObject)
					throw new InvalidDataException(
						"CoinAPI symbols response contains an invalid item.");
				var symbol = serializer.Deserialize<CoinApiSymbol>(jsonReader);
				if (symbol is not null)
					result.Add(symbol);
			}
			return [.. result];
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinAPI returned invalid symbol JSON.", error);
		}
		finally
		{
			_gate.Release();
		}
	}

	public ValueTask<CoinApiTrade[]> GetTradesAsync(string symbolId,
		DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
		=> GetAsync<CoinApiTrade[]>(BuildHistoryPath("trades", symbolId,
			from, to, limit) + "&include_id=true", cancellationToken);

	public ValueTask<CoinApiQuote[]> GetQuotesAsync(string symbolId,
		DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
		=> GetAsync<CoinApiQuote[]>(BuildHistoryPath("quotes", symbolId,
			from, to, limit), cancellationToken);

	public ValueTask<CoinApiOrderBook[]> GetOrderBooksAsync(string symbolId,
		DateTime from, DateTime to, int limit, int depth,
		CancellationToken cancellationToken)
	{
		if (depth is < 1 or > 50)
			throw new ArgumentOutOfRangeException(nameof(depth));
		return GetAsync<CoinApiOrderBook[]>(BuildHistoryPath("orderbooks",
			symbolId, from, to, limit) + "&limit_levels=" + Format(depth),
			cancellationToken);
	}

	public ValueTask<CoinApiOhlcv[]> GetOhlcvAsync(string symbolId,
		CoinApiPeriodIds periodId, DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
	{
		if (periodId == CoinApiPeriodIds.Unknown)
			throw new ArgumentOutOfRangeException(nameof(periodId));
		ValidateHistoryArguments(symbolId, from, to, limit);
		return GetAsync<CoinApiOhlcv[]>("v1/ohlcv/" + Escape(symbolId) +
			"/history?period_id=" + Escape(
				CoinApiEnumConverter<CoinApiPeriodIds>.ToWire(periodId)) +
			"&time_start=" + Escape(from.EnsureUtc().FormatCoinApiTime()) +
			"&time_end=" + Escape(to.EnsureUtc().FormatCoinApiTime()) +
			"&limit=" + Format(limit), cancellationToken);
	}

	private ValueTask<CoinApiPeriod[]> GetPeriodsAsync(
		CancellationToken cancellationToken)
		=> GetAsync<CoinApiPeriod[]>("v1/ohlcv/periods", cancellationToken);

	private static string BuildHistoryPath(string resource, string symbolId,
		DateTime from, DateTime to, int limit)
	{
		ValidateHistoryArguments(symbolId, from, to, limit);
		return "v1/" + resource + "/" + Escape(symbolId) +
			"/history?time_start=" +
			Escape(from.EnsureUtc().FormatCoinApiTime()) + "&time_end=" +
			Escape(to.EnsureUtc().FormatCoinApiTime()) + "&limit=" +
			Format(limit);
	}

	private static void ValidateHistoryArguments(string symbolId,
		DateTime from, DateTime to, int limit)
	{
		symbolId.ThrowIfEmpty(nameof(symbolId));
		if (limit is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, to,
			nameof(from));
	}

	private async ValueTask<T> GetAsync<T>(string path,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			using var response = await SendAsync(path, cancellationToken);
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
					"CoinAPI returned invalid JSON.", error);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async ValueTask<HttpResponseMessage> SendAsync(string path,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await EnforceRequestIntervalAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get, path);
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
		var remaining = _requestInterval -
			(DateTime.UtcNow - _lastRequestTime);
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

	private CoinApiApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		CoinApiError error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<CoinApiError>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var detail = error?.Error.IsEmpty(
			$"HTTP {(int)statusCode} ({statusCode})");
		return new(statusCode,
			$"CoinAPI request failed: {detail}");
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"CoinAPI response is too large ({length} bytes).");
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (bytes.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"CoinAPI response is too large ({bytes.Length} bytes).");
		return bytes;
	}

	private static Uri ValidateEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host.IsEmpty())
			throw new ArgumentException(
				"A valid HTTPS CoinAPI endpoint is required.", nameof(endpoint));
		if (!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"CoinAPI endpoint cannot contain a query or fragment.",
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
