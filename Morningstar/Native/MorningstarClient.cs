namespace StockSharp.Morningstar.Native;

sealed class MorningstarClient : BaseLogReceiver, IDisposable
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

	public MorningstarClient(Uri address, string login, string password)
	{
		_address = address ?? throw new ArgumentNullException(nameof(address));
		_login = login;
		_password = password;
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-Morningstar/1.0");
	}

	public Task Authenticate(CancellationToken cancellationToken)
		=> EnsureToken(true, cancellationToken);

	public Task<MorningstarInvestmentsResponse> GetInvestments(
		MorningstarInvestmentSources source, string universe, string exchangeCode,
		string paginationToken, CancellationToken cancellationToken)
	{
		var query = new MorningstarUniverseQuery
		{
			Source = source,
			Universe = universe,
			ExchangeCode = exchangeCode,
			PaginationToken = paginationToken,
		};
		return Get<MorningstarInvestmentsResponse>(new Uri(_address,
			$"direct-web-services/v1/investments?{query.ToQueryString()}"), cancellationToken);
	}

	public Task<MorningstarDailyOhlcvResponse> GetDailyOhlcv(string identifier,
		MorningstarIdentifierTypes identifierType, string boardCode, DateTime? from,
		DateTime? to, string currency, CancellationToken cancellationToken)
	{
		var query = new MorningstarTimeSeriesQuery
		{
			Identifier = identifier.ThrowIfEmpty(nameof(identifier)),
			IdentifierType = identifierType,
			BoardCode = boardCode,
			From = from,
			To = to,
			Currency = currency.IsEmpty("BASE"),
		};
		return Get<MorningstarDailyOhlcvResponse>(new Uri(_address,
			"direct-web-services/time-series/v1/performance/daily-ohlcv/" +
			$"{Uri.EscapeDataString(query.Identifier)}?{query.ToQueryString()}"), cancellationToken);
	}

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
			if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
			{
				InvalidateToken();
				continue;
			}
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
					$"Morningstar returned an empty response for '{address}'.");
		}
		throw new InvalidOperationException(
			$"Morningstar request '{address}' exhausted its retry limit.");
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

			var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
				$"{_login.ThrowIfEmpty(nameof(_login))}:{_password.ThrowIfEmpty(nameof(_password))}"));
			using var request = new HttpRequestMessage(HttpMethod.Post,
				new Uri(_address, "token/oauth"));
			request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, request.RequestUri);

			var token = JsonConvert.DeserializeObject<MorningstarTokenResponse>(body, _jsonSettings)
				?? throw new InvalidOperationException("Morningstar returned an empty OAuth response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(MorningstarTokenResponse.AccessToken));
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
		MorningstarErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<MorningstarErrorResponse>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var message = error?.GetMessage();
		if (message.IsEmpty())
			message = body?.Length > 1000 ? body[..1000] : body;
		return new InvalidOperationException(
			$"Morningstar request '{address}' failed ({(int)statusCode} {statusCode}): {message}");
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authLock.Dispose();
		base.DisposeManaged();
	}
}
