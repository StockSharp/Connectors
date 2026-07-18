namespace StockSharp.Intrinio.Native;

sealed class IntrinioApiException : InvalidOperationException
{
	public IntrinioApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class IntrinioRestClient : BaseLogReceiver, IDisposable
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
	};

	private readonly Uri _address;
	private readonly string _apiKey;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

	public IntrinioRestClient(Uri address, string apiKey)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-Intrinio/1.0");
	}

	public Task<IntrinioSecuritiesResponse> GetSecurities(
		IntrinioSecuritiesRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioSecuritiesResponse>("securities", request, cancellationToken);

	public Task<IntrinioSecuritySearchResponse> SearchSecurities(
		IntrinioSecuritySearchRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioSecuritySearchResponse>("securities/search", request, cancellationToken);

	public Task<IntrinioSecurity> GetSecurity(string identifier,
		CancellationToken cancellationToken)
		=> Get<IntrinioSecurity>($"securities/{Escape(identifier)}", null, cancellationToken);

	public Task<IntrinioOptionsResponse> GetOptions(string symbol,
		IntrinioOptionsRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioOptionsResponse>($"options/{Escape(symbol)}", request, cancellationToken);

	public Task<IntrinioRealtimeStockPrice> GetRealtimePrice(string identifier,
		IntrinioRealtimePriceRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioRealtimeStockPrice>(
			$"securities/{Escape(identifier)}/prices/realtime", request, cancellationToken);

	public Task<IntrinioSecurityQuote> GetQuote(string identifier,
		IntrinioQuoteRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioSecurityQuote>(
			$"securities/{Escape(identifier)}/quote", request, cancellationToken);

	public Task<IntrinioStockPricesResponse> GetStockPrices(string identifier,
		IntrinioStockPricesRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioStockPricesResponse>(
			$"securities/{Escape(identifier)}/prices", request, cancellationToken);

	public Task<IntrinioSecurityIntervalsResponse> GetSecurityIntervals(string identifier,
		IntrinioSecurityIntervalsRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioSecurityIntervalsResponse>(
			$"securities/{Escape(identifier)}/prices/intervals", request, cancellationToken);

	public Task<IntrinioSecurityTradesResponse> GetSecurityTrades(string identifier,
		IntrinioTradesRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioSecurityTradesResponse>(
			$"securities/{Escape(identifier)}/trades", request, cancellationToken);

	public Task<IntrinioOptionRealtimeResponse> GetOptionRealtime(string identifier,
		IntrinioOptionRealtimeRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioOptionRealtimeResponse>(
			$"options/prices/{Escape(identifier)}/realtime", request, cancellationToken);

	public Task<IntrinioOptionPricesEodResponse> GetOptionPricesEod(string identifier,
		IntrinioOptionPricesEodRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioOptionPricesEodResponse>(
			$"options/prices/{Escape(identifier)}/eod", request, cancellationToken);

	public Task<IntrinioOptionIntervalsResponse> GetOptionIntervals(string identifier,
		IntrinioOptionIntervalsRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioOptionIntervalsResponse>(
			$"options/interval/{Escape(identifier)}", request, cancellationToken);

	public Task<IntrinioOptionTradesResponse> GetOptionTrades(string identifier,
		IntrinioOptionTradesRequest request, CancellationToken cancellationToken)
		=> Get<IntrinioOptionTradesResponse>(
			$"options/{Escape(identifier)}/trades", request, cancellationToken);

	public Task<IntrinioNewsResponse> GetNews(IntrinioNewsRequest request,
		CancellationToken cancellationToken)
		=> Get<IntrinioNewsResponse>("companies/news", request, cancellationToken);

	private async Task<T> Get<T>(string relative, IntrinioRequest query,
		CancellationToken cancellationToken)
		where T : class
	{
		var address = BuildUri(relative, query);
		for (var attempt = 0; attempt < 4; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, address);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);

			if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode >= 500) && attempt < 3)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (response.StatusCode == HttpStatusCode.NoContent)
				return null;
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, address);
			if (body.IsEmpty())
				return null;

			return JsonConvert.DeserializeObject<T>(body, _jsonSettings)
				?? throw new InvalidOperationException(
					$"Intrinio returned an empty response for '{Sanitize(address)}'.");
		}

		throw new InvalidOperationException(
			$"Intrinio request '{Sanitize(address)}' exhausted its retry limit.");
	}

	private Uri BuildUri(string relative, IntrinioRequest query)
	{
		var pairs = new List<(string name, string value)>
		{
			("api_key", _apiKey),
		};
		if (query != null)
		{
			foreach (var property in query.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
			{
				var value = property.GetValue(query);
				if (value == null)
					continue;
				var name = property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
				if (name.IsEmpty())
					throw new InvalidOperationException(
						$"Intrinio request property '{property.Name}' has no protocol name.");
				pairs.Add((name, FormatQueryValue(value)));
			}
		}

		var queryString = pairs.Select(pair =>
			$"{Uri.EscapeDataString(pair.name)}={Uri.EscapeDataString(pair.value)}").Join("&");
		return new Uri(new Uri(_address, relative).AbsoluteUri + "?" + queryString);
	}

	private static string FormatQueryValue(object value)
		=> value switch
		{
			DateTime time => time.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			bool flag => flag ? "true" : "false",
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString(),
		};

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		var delay = response.Headers.RetryAfter?.Delta;
		if (delay == null && response.Headers.RetryAfter?.Date != null)
			delay = response.Headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow;
		if (delay != null && delay.Value > TimeSpan.Zero)
			return delay.Value.Min(TimeSpan.FromSeconds(30));
		return TimeSpan.FromSeconds(Math.Pow(2, attempt));
	}

	private static IntrinioApiException CreateApiError(HttpStatusCode statusCode,
		string body, Uri address)
	{
		IntrinioErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<IntrinioErrorResponse>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var details = error?.GetMessage();
		if (details.IsEmpty())
			details = body?.Length > 1000 ? body[..1000] : body;
		return new(statusCode,
			$"Intrinio request '{Sanitize(address)}' failed ({(int)statusCode} {statusCode}): {details}");
	}

	private static string Sanitize(Uri address)
		=> address.GetLeftPart(UriPartial.Path);

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static Uri EnsureTrailingSlash(Uri address)
	{
		var value = address.AbsoluteUri;
		return value.EndsWith('/') ? address : new Uri(value + "/");
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
