namespace StockSharp.Pacifica.Native;

sealed class PacificaRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public PacificaRestClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Pacifica REST endpoint must use HTTP or HTTPS.", nameof(endpoint));
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Pacifica-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "Pacifica_REST";

	public async ValueTask<PacificaMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<PacificaMarket[]>("info", cancellationToken)).Data ?? [];

	public async ValueTask<PacificaPrice[]> GetPricesAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<PacificaPrice[]>("info/prices", cancellationToken)).Data ?? [];

	public async ValueTask<PacificaBook> GetBookAsync(string symbol,
		int aggregationLevel, CancellationToken cancellationToken)
		=> (await SendAsync<PacificaBook>("book?symbol=" + Escape(symbol) +
			"&agg_level=" + aggregationLevel.ToString(CultureInfo.InvariantCulture),
			cancellationToken)).Data;

	public async ValueTask<PacificaPublicTrade[]> GetTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendAsync<PacificaPublicTrade[]>("trades?symbol=" + Escape(symbol),
			cancellationToken)).Data ?? [];

	public async ValueTask<PacificaCandle[]> GetCandlesAsync(string symbol,
		PacificaCandleIntervals interval, DateTime from, DateTime to, int limit,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 4000)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Pacifica candle limit must be between 1 and 4000.");
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from), from,
				"Pacifica candle start time cannot be later than end time.");
		var path = new StringBuilder("kline?symbol=")
			.Append(Escape(symbol))
			.Append("&interval=").Append(Escape(interval.ToWire()))
			.Append("&start_time=").Append(from.ToUnixMilliseconds()
				.ToString(CultureInfo.InvariantCulture))
			.Append("&end_time=").Append(to.ToUnixMilliseconds()
				.ToString(CultureInfo.InvariantCulture))
			.Append("&limit=").Append(limit.ToString(CultureInfo.InvariantCulture))
			.ToString();
		return (await SendAsync<PacificaCandle[]>(path, cancellationToken)).Data ?? [];
	}

	public async ValueTask<PacificaAccountInfo> GetAccountAsync(string account,
		CancellationToken cancellationToken)
		=> (await SendAsync<PacificaAccountInfo>("account?account=" + Escape(account),
			cancellationToken)).Data;

	public async ValueTask<PacificaPosition[]> GetPositionsAsync(string account,
		CancellationToken cancellationToken)
		=> (await SendAsync<PacificaPosition[]>("positions?account=" + Escape(account),
			cancellationToken)).Data ?? [];

	public async ValueTask<PacificaOrder[]> GetOrdersAsync(string account,
		CancellationToken cancellationToken)
		=> (await SendAsync<PacificaOrder[]>("orders?account=" + Escape(account),
			cancellationToken)).Data ?? [];

	public ValueTask<PacificaResponse<PacificaOrder[]>> GetOrderHistoryAsync(
		string account, int limit, string cursor,
		CancellationToken cancellationToken)
	{
		ValidateHistoryLimit(limit);
		var path = new StringBuilder("orders/history?account=")
			.Append(Escape(account))
			.Append("&limit=").Append(limit.ToString(CultureInfo.InvariantCulture));
		Append(path, "cursor", cursor);
		return SendAsync<PacificaOrder[]>(path.ToString(), cancellationToken);
	}

	public ValueTask<PacificaResponse<PacificaAccountTrade[]>> GetTradeHistoryAsync(
		string account, string symbol, DateTime? from, DateTime? to, int limit,
		string cursor, CancellationToken cancellationToken)
	{
		ValidateHistoryLimit(limit);
		var path = new StringBuilder("trades/history?account=")
			.Append(Escape(account))
			.Append("&limit=").Append(limit.ToString(CultureInfo.InvariantCulture));
		Append(path, "symbol", symbol);
		Append(path, "start_time", from?.EnsureUtc().ToUnixMilliseconds()
			.ToString(CultureInfo.InvariantCulture));
		Append(path, "end_time", to?.EnsureUtc().ToUnixMilliseconds()
			.ToString(CultureInfo.InvariantCulture));
		Append(path, "cursor", cursor);
		return SendAsync<PacificaAccountTrade[]>(path.ToString(), cancellationToken);
	}

	private async ValueTask<PacificaResponse<T>> SendAsync<T>(string path,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, path.TrimStart('/')));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (attempt + 1 < _maximumReadAttempts && IsTransient(response.StatusCode))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromMilliseconds(250 * (1 << attempt));
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(5)), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					"Pacifica HTTP " + (int)response.StatusCode + " (" +
					response.StatusCode + "): " + Limit(body, 1024), null,
					response.StatusCode);
			PacificaResponse<T> result;
			try
			{
				result = JsonConvert.DeserializeObject<PacificaResponse<T>>(body,
					_jsonSettings) ?? throw new InvalidDataException(
						"Pacifica returned an empty JSON response.");
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Pacifica returned malformed REST JSON.", error);
			}
			if (!result.IsSuccess)
				throw new InvalidOperationException(
					"Pacifica API error" +
					(result.Code is int code ? " " + code : string.Empty) + ": " +
					(result.Error.IsEmpty() ? "request failed" : result.Error));
			return result;
		}
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(50);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static void Append(StringBuilder query, string name, string value)
	{
		if (!value.IsEmpty())
			query.Append('&').Append(name).Append('=').Append(Escape(value));
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static void ValidateHistoryLimit(int limit)
	{
		if (limit is < 1 or > 4000)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Pacifica history limit must be between 1 and 4000.");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Pacifica response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Pacifica response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum ? value : value[..maximum];

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
