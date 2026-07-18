namespace StockSharp.SpGlobal.Native;

sealed class SpGlobalClient : BaseLogReceiver, IDisposable
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	private readonly Uri _address;
	private readonly string _login;
	private readonly string _password;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly SemaphoreSlim _authLock = new(1, 1);
	private string _accessToken;
	private DateTime _tokenExpires;

	public SpGlobalClient(Uri address, string login, string password)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_login = login;
		_password = password;
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-SpGlobal/1.0");
	}

	public Task Authenticate(CancellationToken cancellationToken)
		=> EnsureToken(true, cancellationToken);

	public Task<SpGlobalSymbolResponse> GetSymbols(SpGlobalSymbolQuery query,
		CancellationToken cancellationToken)
		=> Get<SpGlobalSymbolResponse>(new Uri(_address,
			$"market-data/reference-data/v3/search?{query.ToQueryString()}"), cancellationToken);

	public Task<SpGlobalAssessmentResponse> GetCurrentAssessments(
		SpGlobalAssessmentQuery query, CancellationToken cancellationToken)
		=> Get<SpGlobalAssessmentResponse>(new Uri(_address,
			$"market-data/v3/value/current/symbol?{query.ToQueryString(true)}"), cancellationToken);

	public Task<SpGlobalAssessmentResponse> GetHistoricalAssessments(
		SpGlobalAssessmentQuery query, CancellationToken cancellationToken)
		=> Get<SpGlobalAssessmentResponse>(new Uri(_address,
			$"market-data/v3/value/history/symbol?{query.ToQueryString(false)}"), cancellationToken);

	private async Task<T> Get<T>(Uri address, CancellationToken cancellationToken)
		where T : class
	{
		for (var attempt = 0; attempt < 4; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, address);
			await SetAuthorization(request, cancellationToken);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);

			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden &&
				attempt == 0)
			{
				InvalidateToken();
				continue;
			}
			if (response.StatusCode == HttpStatusCode.TooManyRequests && IsDailyLimit(response))
				throw CreateApiError(response.StatusCode, body, address);
			if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode >= 500) && attempt < 3)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, address);

			return JsonConvert.DeserializeObject<T>(body, _jsonSettings)
				?? throw new InvalidOperationException(
					$"S&P Global returned an empty response for '{address}'.");
		}
		throw new InvalidOperationException(
			$"S&P Global request '{address}' exhausted its retry limit.");
	}

	private async Task SetAuthorization(HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		await EnsureToken(false, cancellationToken);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
	}

	private async Task EnsureToken(bool force, CancellationToken cancellationToken)
	{
		if (!force && !_accessToken.IsEmpty() && DateTime.UtcNow < _tokenExpires)
			return;

		await _authLock.WaitAsync(cancellationToken);
		try
		{
			if (!force && !_accessToken.IsEmpty() && DateTime.UtcNow < _tokenExpires)
				return;

			using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_address, "auth/api"))
			{
				Content = new FormUrlEncodedContent([
					new("username", _login.ThrowIfEmpty(nameof(_login))),
					new("password", _password.ThrowIfEmpty(nameof(_password))),
				]),
			};
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, request.RequestUri);

			var token = JsonConvert.DeserializeObject<SpGlobalTokenResponse>(body, _jsonSettings)
				?? throw new InvalidOperationException(
					"S&P Global returned an empty authentication response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(SpGlobalTokenResponse.AccessToken));
			var lifetime = TimeSpan.FromSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600);
			var safety = TimeSpan.FromSeconds(Math.Min(60, lifetime.TotalSeconds / 2));
			_tokenExpires = DateTime.UtcNow + lifetime - safety;
		}
		finally
		{
			_authLock.Release();
		}
	}

	private void InvalidateToken()
	{
		_accessToken = null;
		_tokenExpires = default;
	}

	private static bool IsDailyLimit(HttpResponseMessage response)
	{
		if (!response.Headers.TryGetValues("x-ratelimit-remaining-day", out var values))
			return false;
		return long.TryParse(values.FirstOrDefault(), out var remaining) && remaining <= 0;
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		var delay = response.Headers.RetryAfter?.Delta;
		if (delay == null && response.Headers.RetryAfter?.Date is DateTimeOffset date)
			delay = date - DateTimeOffset.UtcNow;
		if (delay != null && delay.Value > TimeSpan.Zero)
			return delay.Value > TimeSpan.FromSeconds(30)
				? TimeSpan.FromSeconds(30)
				: delay.Value;
		return TimeSpan.FromSeconds(Math.Pow(2, attempt));
	}

	private static Exception CreateApiError(HttpStatusCode statusCode, string body, Uri address)
	{
		SpGlobalErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<SpGlobalErrorResponse>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var message = error?.GetMessage();
		if (message.IsEmpty())
			message = body?.Length > 1000 ? body[..1000] : body;
		return new InvalidOperationException(
			$"S&P Global request '{address}' failed ({(int)statusCode} {statusCode}): {message}");
	}

	private static Uri EnsureTrailingSlash(Uri address)
	{
		var value = address.AbsoluteUri;
		return value.EndsWith('/') ? address : new Uri(value + "/");
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authLock.Dispose();
		base.DisposeManaged();
	}
}
