namespace StockSharp.QFEX.Native;

sealed class QFEXRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;

	private readonly Uri _endpoint;
	private readonly QFEXAuthenticator _authenticator;
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

	public QFEXRestClient(string endpoint, string publicKey,
		SecureString secret, string accountId)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"QFEX REST endpoint must use HTTP or HTTPS.", nameof(endpoint));
		_authenticator = new(publicKey, secret, accountId);
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-QFEX-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "QFEX_REST";

	public bool IsCredentialsAvailable => _authenticator.IsAvailable;

	public string AccountId => _authenticator.AccountId;

	public async ValueTask<QFEXReferenceDataSymbol[]> GetReferenceDataAsync(
		string ticker, CancellationToken cancellationToken)
	{
		var path = "refdata" + (ticker.IsEmpty()
			? string.Empty
			: "?ticker=" + Escape(ticker.Trim()));
		return (await SendAsync<QFEXReferenceDataResponse>(path, false,
			cancellationToken)).Data ?? [];
	}

	public async ValueTask<QFEXCandle[]> GetCandlesAsync(string symbol,
		QFEXCandleIntervals interval, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim();
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from), from,
				"QFEX candle start time cannot be later than end time.");
		var path = "candles/" + Escape(symbol) + "?resolution=" +
			Escape(interval.ToWire()) + "&fromISO=" +
			Escape(from.ToIso8601()) + "&toISO=" + Escape(to.ToIso8601());
		return (await SendAsync<QFEXCandlesResponse>(path, false,
			cancellationToken)).Candles ?? [];
	}

	public ValueTask<QFEXPortfolioResponse> GetPortfolioAsync(
		CancellationToken cancellationToken)
		=> SendAsync<QFEXPortfolioResponse>("user/positions", true,
			cancellationToken);

	public ValueTask<QFEXHistoricOrdersResponse> GetHistoricOrdersAsync(
		string symbol, string clientOrderId, DateTime? from, DateTime? to,
		int limit, CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var query = new StringBuilder("user/historic-orders?limit=")
			.Append(limit.ToString(CultureInfo.InvariantCulture))
			.Append("&offset=0");
		Append(query, "symbol", symbol);
		Append(query, "client_order_id", clientOrderId);
		Append(query, "start", from?.EnsureUtc().ToIso8601());
		Append(query, "end", to?.EnsureUtc().ToIso8601());
		return SendAsync<QFEXHistoricOrdersResponse>(query.ToString(), true,
			cancellationToken);
	}

	public ValueTask<QFEXUserTradesResponse> GetUserTradesAsync(string symbol,
		string orderId, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var query = new StringBuilder("user/trade?limit=")
			.Append(limit.ToString(CultureInfo.InvariantCulture))
			.Append("&offset=0");
		Append(query, "symbol", symbol);
		Append(query, "exchange_order_id", orderId);
		Append(query, "start", from?.EnsureUtc().ToIso8601());
		Append(query, "end", to?.EnsureUtc().ToIso8601());
		return SendAsync<QFEXUserTradesResponse>(query.ToString(), true,
			cancellationToken);
	}

	private async ValueTask<T> SendAsync<T>(string path, bool isPrivate,
		CancellationToken cancellationToken)
		where T : class
	{
		if (isPrivate && !_authenticator.IsAvailable)
			throw new InvalidOperationException(
				"QFEX API credentials are required for private REST data.");
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, path));
			if (isPrivate)
				_authenticator.Sign(request);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (attempt < 3 && IsTransient(response.StatusCode))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(5)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					$"QFEX HTTP {(int)response.StatusCode} " +
					$"({response.StatusCode}): {Limit(body, 1024)}");
			try
			{
				return JsonConvert.DeserializeObject<T>(body, _jsonSettings) ??
					throw new InvalidDataException(
						"QFEX returned an empty JSON response.");
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"QFEX returned malformed REST JSON.", error);
			}
		}
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
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
		=> Uri.EscapeDataString(value);

	private static void ValidateLimit(int limit)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"QFEX REST history limit must be between 1 and 1000.");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"QFEX response exceeds the 16 MiB safety limit.");
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
					"QFEX response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
