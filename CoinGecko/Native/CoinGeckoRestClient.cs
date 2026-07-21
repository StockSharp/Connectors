namespace StockSharp.CoinGecko.Native;

sealed class CoinGeckoApiException : InvalidOperationException
{
	public CoinGeckoApiException(HttpStatusCode statusCode, int? errorCode,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		ErrorCode = errorCode;
	}

	public HttpStatusCode StatusCode { get; }
	public int? ErrorCode { get; }
}

sealed class CoinGeckoRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 64 * 1024 * 1024;
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

	public CoinGeckoRestClient(string endpoint, CoinGeckoApiTiers tier,
		SecureString apiKey, TimeSpan requestInterval)
	{
		var root = ValidateEndpoint(endpoint, nameof(endpoint));
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		if (requestInterval < TimeSpan.Zero || requestInterval > TimeSpan.FromMinutes(1))
			throw new ArgumentOutOfRangeException(nameof(requestInterval));
		_requestInterval = requestInterval;
		_client = new()
		{
			BaseAddress = root,
			Timeout = TimeSpan.FromSeconds(60),
		};
		_client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-CoinGecko/1.0");
		_client.DefaultRequestHeaders.TryAddWithoutValidation(
			tier.GetApiKeyHeader(), apiKey.UnSecure().Trim());
	}

	public override string Name => "CoinGecko_REST";

	public async ValueTask ValidateAsync(CancellationToken cancellationToken)
	{
		var response = await GetAsync<CoinGeckoPing>("ping", cancellationToken);
		if (response?.Message.IsEmpty() != false)
			throw new InvalidDataException("CoinGecko ping returned no status message.");
	}

	public async ValueTask<CoinGeckoCoin[]> GetCoinsAsync(
		CancellationToken cancellationToken)
		=> await GetAsync<CoinGeckoCoin[]>("coins/list?include_platform=false",
			cancellationToken) ?? [];

	public async ValueTask<string[]> GetSupportedCurrenciesAsync(
		CancellationToken cancellationToken)
		=> await GetAsync<string[]>("simple/supported_vs_currencies",
			cancellationToken) ?? [];

	public async ValueTask<CoinGeckoCoinMarket> GetCoinMarketAsync(string coinId,
		string quoteCurrency, CancellationToken cancellationToken)
	{
		var result = await GetAsync<CoinGeckoCoinMarket[]>("coins/markets?vs_currency=" +
			Escape(quoteCurrency) + "&ids=" + Escape(coinId) +
			"&sparkline=false&price_change_percentage=24h&precision=full",
			cancellationToken) ?? [];
		return result.FirstOrDefault(item => item?.Id.EqualsIgnoreCase(coinId) == true);
	}

	public ValueTask<CoinGeckoPoolSearchResponse> SearchPoolsAsync(string query,
		string network, int page, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
		var path = "onchain/search/pools?query=" + Escape(query) +
			"&include=base_token%2Cquote_token%2Cdex&page=" + Format(page);
		if (!network.IsEmpty())
			path += "&network=" + Escape(network);
		return GetAsync<CoinGeckoPoolSearchResponse>(path, cancellationToken);
	}

	public ValueTask<CoinGeckoPoolResponse> GetPoolAsync(string network,
		string poolAddress, CancellationToken cancellationToken)
		=> GetAsync<CoinGeckoPoolResponse>("onchain/networks/" + Escape(network) +
			"/pools/" + Escape(poolAddress) +
			"?include=base_token%2Cquote_token%2Cdex", cancellationToken);

	public async ValueTask<CoinGeckoTradeResource[]> GetPoolTradesAsync(
		string network, string poolAddress, CancellationToken cancellationToken)
		=> (await GetAsync<CoinGeckoTradesResponse>("onchain/networks/" +
			Escape(network) + "/pools/" + Escape(poolAddress) +
			"/trades?token=base", cancellationToken))?.Data ?? [];

	public async ValueTask<CoinGeckoPoolOhlcv[]> GetPoolOhlcvPageAsync(
		string network, string poolAddress, CoinGeckoOhlcvTimeframes timeframe,
		int aggregate, long? beforeTimestamp, int limit,
		CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(aggregate, 1);
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		var path = "onchain/networks/" + Escape(network) + "/pools/" +
			Escape(poolAddress) + "/ohlcv/" + Escape(
				CoinGeckoEnumConverter<CoinGeckoOhlcvTimeframes>.ToWire(timeframe)) +
			"?aggregate=" + Format(aggregate) + "&limit=" + Format(limit) +
			"&currency=usd&token=base&include_empty_intervals=true";
		if (beforeTimestamp is long timestamp)
			path += "&before_timestamp=" + Format(timestamp);
		return (await GetAsync<CoinGeckoPoolOhlcvResponse>(path,
			cancellationToken))?.Data?.Attributes?.Items ?? [];
	}

	public async ValueTask<CoinGeckoCoinOhlc[]> GetCoinOhlcRangeAsync(
		string coinId, string quoteCurrency, DateTime from, DateTime to,
		CoinGeckoCoinIntervals interval, int maximum,
		CancellationToken cancellationToken)
	{
		if (from >= to)
			return [];
		ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
		var window = interval == CoinGeckoCoinIntervals.Hourly
			? TimeSpan.FromDays(31)
			: TimeSpan.FromDays(180);
		var result = new SortedDictionary<decimal, CoinGeckoCoinOhlc>();
		var cursor = from.EnsureUtc();
		to = to.EnsureUtc();
		while (cursor < to)
		{
			var end = cursor + window;
			if (end > to)
				end = to;
			var path = "coins/" + Escape(coinId) + "/ohlc/range?vs_currency=" +
				Escape(quoteCurrency) + "&from=" + Format(ToUnix(cursor)) +
				"&to=" + Format(ToUnix(end)) + "&interval=" + Escape(
					CoinGeckoEnumConverter<CoinGeckoCoinIntervals>.ToWire(interval));
			var page = await GetAsync<CoinGeckoCoinOhlc[]>(path,
				cancellationToken) ?? [];
			foreach (var item in page.Where(static item => item is not null))
				result[item.Timestamp] = item;
			if (result.Count > maximum)
				foreach (var timestamp in result.Keys.Take(result.Count - maximum)
					.ToArray())
					result.Remove(timestamp);
			cursor = end;
		}
		return [.. result.Values];
	}

	public async ValueTask<CoinGeckoCoinOhlc[]> GetCoinOhlcRecentAsync(
		string coinId, string quoteCurrency, CoinGeckoRecentDays days,
		CancellationToken cancellationToken)
		=> await GetAsync<CoinGeckoCoinOhlc[]>("coins/" + Escape(coinId) +
			"/ohlc?vs_currency=" + Escape(quoteCurrency) + "&days=" +
			Escape(CoinGeckoEnumConverter<CoinGeckoRecentDays>.ToWire(days)) +
			"&precision=full", cancellationToken) ?? [];

	private async ValueTask<T> GetAsync<T>(string path,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		await _gate.WaitAsync(cancellationToken);
		try
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
				using (response)
				{
					_lastRequestTime = DateTime.UtcNow;
					var bytes = await ReadResponseAsync(response, cancellationToken);
					if (response.IsSuccessStatusCode)
					{
						if (bytes.Length == 0)
							return default;
						return JsonConvert.DeserializeObject<T>(
							Encoding.UTF8.GetString(bytes), _settings);
					}
					if (attempt < 3 && IsTransient(response.StatusCode))
					{
						await DelayRetryAsync(attempt,
							response.Headers.RetryAfter?.Delta, cancellationToken);
						continue;
					}
					throw CreateException(response.StatusCode, bytes);
				}
			}
		}
		finally
		{
			_gate.Release();
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

	private CoinGeckoApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		CoinGeckoErrorResponse error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<CoinGeckoErrorResponse>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var detail = error?.Status?.Message;
		if (detail.IsEmpty())
			detail = error?.Error;
		var message = detail.IsEmpty()
			? $"CoinGecko API returned HTTP {(int)statusCode} ({statusCode})."
			: $"CoinGecko API returned HTTP {(int)statusCode}: {detail}";
		return new(statusCode, error?.Status?.Code, message);
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"CoinGecko response is too large ({length} bytes).");
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (bytes.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"CoinGecko response is too large ({bytes.Length} bytes).");
		return bytes;
	}

	private static Uri ValidateEndpoint(string endpoint, string parameterName)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/", UriKind.Absolute,
			out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host.IsEmpty())
			throw new ArgumentException("A valid HTTPS endpoint is required.",
				parameterName);
		if (!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"CoinGecko endpoint cannot contain a query or fragment.", parameterName);
		if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/api/v3",
			StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				"CoinGecko REST endpoint must end in /api/v3.", parameterName);
		return uri;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string Format(int value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private static string Format(long value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private static long ToUnix(DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch).TotalSeconds);

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
