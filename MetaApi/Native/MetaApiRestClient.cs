namespace StockSharp.MetaApi.Native;

sealed class MetaApiApiException : InvalidOperationException
{
	public MetaApiApiException(HttpStatusCode statusCode, string errorCode,
		string message, DateTime? recommendedRetryTime = null)
		: base(message)
	{
		StatusCode = statusCode;
		ErrorCode = errorCode;
		RecommendedRetryTime = recommendedRetryTime;
	}

	public HttpStatusCode StatusCode { get; }
	public string ErrorCode { get; }
	public DateTime? RecommendedRetryTime { get; }
}

sealed class MetaApiRestClient : BaseLogReceiver
{
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };
	private readonly string _token;
	private readonly string _configuredDomain;
	private string _region;
	private readonly int _maxAttempts;
	private string _resolvedDomain;

	public MetaApiRestClient(string domain, SecureString token, string region,
		int maxAttempts)
	{
		_configuredDomain = NormalizeDomain(domain);
		_resolvedDomain = _configuredDomain;
		_token = token?.UnSecure().ThrowIfEmpty(nameof(token));
		_region = region;
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => nameof(MetaApi) + "_REST";

	public static string SerializeBody<T>(T body)
		=> MetaApiJsonSerializer.Serialize(body);

	public void SetRegion(string region)
		=> _region = region.ThrowIfEmpty(nameof(region));

	public async Task<MetaApiServerSettings> GetServerSettingsAsync(
		CancellationToken cancellationToken)
	{
		var result = await GetAsync<MetaApiServerSettings>(new Uri(
			$"https://mt-provisioning-api-v1.{_configuredDomain}/" +
			"users/current/servers/mt-client-api"), cancellationToken);
		if (result?.Domain.IsEmpty() == false)
			_resolvedDomain = NormalizeDomain(result.Domain);
		return result;
	}

	public Task<MetaApiAccount> GetAccountAsync(string accountId,
		CancellationToken cancellationToken)
		=> GetAsync<MetaApiAccount>(ProvisioningUri(
			$"users/current/accounts/{Escape(accountId)}"), cancellationToken);

	public Task<MetaApiAccountInformation> GetAccountInformationAsync(
		string accountId, CancellationToken cancellationToken)
		=> GetAsync<MetaApiAccountInformation>(ClientUri(accountId,
			"account-information"), cancellationToken);

	public Task<MetaApiPosition[]> GetPositionsAsync(string accountId,
		CancellationToken cancellationToken)
		=> GetAsync<MetaApiPosition[]>(ClientUri(accountId, "positions"),
			cancellationToken);

	public Task<MetaApiOrder[]> GetOrdersAsync(string accountId,
		CancellationToken cancellationToken)
		=> GetAsync<MetaApiOrder[]>(ClientUri(accountId, "orders"),
			cancellationToken);

	public Task<MetaApiOrder[]> GetHistoryOrdersAsync(string accountId,
		DateTime from, DateTime to, int offset, int limit,
		CancellationToken cancellationToken)
		=> GetAsync<MetaApiOrder[]>(ClientUri(accountId,
			$"history-orders/time/{Escape(FormatTime(from))}/{Escape(FormatTime(to))}" +
			$"?offset={Math.Max(0, offset)}&limit={Math.Clamp(limit, 1, 1000)}"),
			cancellationToken);

	public Task<MetaApiDeal[]> GetDealsAsync(string accountId, DateTime from,
		DateTime to, int offset, int limit, CancellationToken cancellationToken)
		=> GetAsync<MetaApiDeal[]>(ClientUri(accountId,
			$"history-deals/time/{Escape(FormatTime(from))}/{Escape(FormatTime(to))}" +
			$"?offset={Math.Max(0, offset)}&limit={Math.Clamp(limit, 1, 1000)}"),
			cancellationToken);

	public Task<string[]> GetSymbolsAsync(string accountId,
		CancellationToken cancellationToken)
		=> GetAsync<string[]>(ClientUri(accountId, "symbols"), cancellationToken);

	public Task<MetaApiSymbolSpecification> GetSpecificationAsync(string accountId,
		string symbol, CancellationToken cancellationToken)
		=> GetAsync<MetaApiSymbolSpecification>(ClientUri(accountId,
			$"symbols/{Escape(symbol)}/specification"), cancellationToken);

	public Task<MetaApiSymbolPrice> GetPriceAsync(string accountId, string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<MetaApiSymbolPrice>(ClientUri(accountId,
			$"symbols/{Escape(symbol)}/current-price"), cancellationToken);

	public Task<MetaApiCandle> GetCandleAsync(string accountId, string symbol,
		string timeFrame, CancellationToken cancellationToken)
		=> GetAsync<MetaApiCandle>(ClientUri(accountId,
			$"symbols/{Escape(symbol)}/current-candles/{Escape(timeFrame)}"),
			cancellationToken);

	public Task<MetaApiTick> GetTickAsync(string accountId, string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<MetaApiTick>(ClientUri(accountId,
			$"symbols/{Escape(symbol)}/current-tick"), cancellationToken);

	public Task<MetaApiBook> GetBookAsync(string accountId, string symbol,
		CancellationToken cancellationToken)
		=> GetAsync<MetaApiBook>(ClientUri(accountId,
			$"symbols/{Escape(symbol)}/current-book"), cancellationToken);

	public Task<MetaApiCandle[]> GetHistoricalCandlesAsync(string accountId,
		string symbol, string timeFrame, DateTime? startTime, int limit,
		CancellationToken cancellationToken)
	{
		var query = new List<string> { $"limit={Math.Clamp(limit, 1, 1000)}" };
		if (startTime is not null)
			query.Add("startTime=" + Escape(FormatTime(startTime.Value)));
		return GetAsync<MetaApiCandle[]>(HistoricalUri(accountId,
			$"symbols/{Escape(symbol)}/timeframes/{Escape(timeFrame)}/candles?" +
			string.Join('&', query)), cancellationToken);
	}

	public Task<MetaApiTick[]> GetHistoricalTicksAsync(string accountId,
		string symbol, DateTime? startTime, int offset, int limit,
		CancellationToken cancellationToken)
	{
		var query = new List<string>
		{
			$"offset={Math.Max(0, offset)}",
			$"limit={Math.Clamp(limit, 1, 1000)}",
		};
		if (startTime is not null)
			query.Add("startTime=" + Escape(FormatTime(startTime.Value)));
		return GetAsync<MetaApiTick[]>(HistoricalUri(accountId,
			$"symbols/{Escape(symbol)}/ticks?" + string.Join('&', query)),
			cancellationToken);
	}

	public Task<MetaApiTradeResponse> TradeAsync(string accountId,
		MetaApiTradeRequest trade, CancellationToken cancellationToken)
		=> SendAsync<MetaApiTradeResponse>(HttpMethod.Post,
			ClientUri(accountId, "trade"), SerializeBody(trade ??
			throw new ArgumentNullException(nameof(trade))), false, cancellationToken);

	private Task<T> GetAsync<T>(Uri uri, CancellationToken cancellationToken)
		=> SendAsync<T>(HttpMethod.Get, uri, null, true, cancellationToken);

	private async Task<T> SendAsync<T>(HttpMethod method, Uri uri, string json,
		bool isRetryEnabled, CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(method, uri);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.TryAddWithoutValidation("auth-token", _token);
			if (json is not null)
				request.Content = new StringContent(json, Encoding.UTF8, "application/json");

			this.AddDebugLog("MetaApi REST {0} {1} (attempt {2}).",
				method, uri.GetLeftPart(UriPartial.Path), attempt);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var payload = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				if (payload.IsEmpty())
					throw new InvalidDataException(
						$"MetaApi {method} {uri.AbsolutePath} returned an empty response.");
				return MetaApiJsonSerializer.Deserialize<T>(payload);
			}

			var error = TryReadError(payload);
			var exception = new MetaApiApiException(response.StatusCode, error?.Error,
				BuildErrorMessage(method, uri, response.StatusCode, error, payload),
				error?.Metadata?.RecommendedRetryTime);
			if (!isRetryEnabled || attempt >= _maxAttempts ||
				response.StatusCode != HttpStatusCode.TooManyRequests &&
				(int)response.StatusCode < 500)
				throw exception;

			var delay = GetRetryDelay(response, exception, attempt);
			this.AddDebugLog("MetaApi REST retry in {0} after status {1}.",
				delay, (int)response.StatusCode);
			await Task.Delay(delay, cancellationToken);
		}
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response,
		MetaApiApiException exception, int attempt)
	{
		var delay = response.Headers.RetryAfter?.Delta;
		if (delay is null && exception.RecommendedRetryTime is { } retryTime)
			delay = retryTime.ToUniversalTime() - DateTime.UtcNow;
		if (delay is null || delay <= TimeSpan.Zero)
			delay = TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
		return delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay.Value;
	}

	private static MetaApiError TryReadError(string payload)
	{
		if (payload.IsEmpty())
			return null;
		try
		{
			return MetaApiJsonSerializer.Deserialize<MetaApiError>(payload);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string BuildErrorMessage(HttpMethod method, Uri uri,
		HttpStatusCode statusCode, MetaApiError error, string payload)
		=> $"MetaApi {method} {uri.AbsolutePath} failed ({(int)statusCode}): " +
			(error?.Message.IsEmpty(error?.Error).IsEmpty(payload).IsEmpty(statusCode.ToString()));

	private Uri ProvisioningUri(string path)
		=> new($"https://mt-provisioning-api-v1.{_configuredDomain}/" + path);

	private Uri ClientUri(string accountId, string path)
	{
		EnsureRegion();
		return new($"https://mt-client-api-v1.{_region}.{_resolvedDomain}/" +
			$"users/current/accounts/{Escape(accountId)}/{path}");
	}

	private Uri HistoricalUri(string accountId, string path)
	{
		EnsureRegion();
		return new($"https://mt-market-data-client-api-v1.{_region}.{_resolvedDomain}/" +
			$"users/current/accounts/{Escape(accountId)}/historical-market-data/{path}");
	}

	private void EnsureRegion()
	{
		if (_region.IsEmpty())
			throw new InvalidOperationException("MetaApi account region is not resolved.");
	}

	private static string NormalizeDomain(string domain)
	{
		domain = domain.ThrowIfEmpty(nameof(domain)).Trim();
		if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
			domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			domain = new Uri(domain).Host;
		return domain.Trim().TrimEnd('/');
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string FormatTime(DateTime value)
		=> value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
