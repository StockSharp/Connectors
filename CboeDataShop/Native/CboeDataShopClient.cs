namespace StockSharp.CboeDataShop.Native;

sealed class CboeDataShopClient : BaseLogReceiver, IDisposable
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	private readonly Uri _address;
	private readonly Uri _tokenAddress;
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly string _mode;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly SemaphoreSlim _authLock = new(1, 1);
	private string _accessToken;
	private DateTime _tokenExpires;

	public CboeDataShopClient(Uri address, Uri tokenAddress, string clientId,
		string clientSecret, CboeDataModes mode)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_tokenAddress = tokenAddress ?? throw new ArgumentNullException(nameof(tokenAddress));
		_clientId = clientId;
		_clientSecret = clientSecret;
		_mode = mode.ToString().ToLowerInvariant();
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"User-Agent", "StockSharp-CboeDataShop/1.0");
	}

	public Task Authenticate(CancellationToken cancellationToken)
		=> EnsureToken(true, cancellationToken);

	public async Task<CboeSymbol[]> GetSymbols(CancellationToken cancellationToken)
		=> await Get<CboeSymbol[]>(CreateAddress("reference/symbols"), cancellationToken) ?? [];

	public async Task<DateTime[]> GetTradingDays(CboeTradingDaysQuery query,
		CancellationToken cancellationToken)
	{
		var values = await Get<string[]>(CreateAddress(
			$"reference/trading-days?{query.ToQueryString()}"), cancellationToken) ?? [];
		return values
			.Select(value => DateTime.TryParseExact(value, "yyyy-MM-dd",
				CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
				? (DateTime?)date.Date : null)
			.WhereNotNull()
			.OrderBy(date => date)
			.ToArray();
	}

	public async Task<CboeUnderlyingSnapshot[]> GetUnderlyingQuotes(
		CboeUnderlyingQuoteQuery query, CancellationToken cancellationToken)
		=> await Get<CboeUnderlyingSnapshot[]>(CreateAddress(
			$"market/underlying-quotes?{query.ToQueryString()}"), cancellationToken) ?? [];

	public Task<CboeOptionQuoteResponse> GetOptionQuotes(CboeOptionQuoteQuery query,
		CancellationToken cancellationToken)
		=> Get<CboeOptionQuoteResponse>(CreateAddress(
			$"market/option-and-underlying-quotes?{query.ToQueryString()}"), cancellationToken);

	public async Task<CboeUnderlyingTrade[]> GetUnderlyingTrades(CboeTradeQuery query,
		CancellationToken cancellationToken)
		=> await Get<CboeUnderlyingTrade[]>(CreateAddress(
			$"time-and-sales/underlying-trades?{query.ToQueryString()}"), cancellationToken) ?? [];

	public async Task<CboeOptionTrade[]> GetOptionTrades(CboeTradeQuery query,
		CancellationToken cancellationToken)
		=> await Get<CboeOptionTrade[]>(CreateAddress(
			$"time-and-sales/option-trades?{query.ToQueryString()}"), cancellationToken) ?? [];

	private Uri CreateAddress(string relative)
		=> new(_address, $"{_mode}/allaccess/{relative}");

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

			if ((response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) &&
				attempt == 0)
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
			if (response.StatusCode == HttpStatusCode.NoContent)
				return null;
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, address);
			if (body.IsEmpty())
				return null;

			return JsonConvert.DeserializeObject<T>(body, _jsonSettings)
				?? throw new InvalidOperationException(
					$"Cboe returned an empty response for '{address}'.");
		}
		throw new InvalidOperationException(
			$"Cboe request '{address}' exhausted its retry limit.");
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

			using var request = new HttpRequestMessage(HttpMethod.Post, _tokenAddress)
			{
				Content = new FormUrlEncodedContent([
					new("grant_type", "client_credentials"),
				]),
			};
			var credentials = $"{_clientId.ThrowIfEmpty(nameof(_clientId))}:{_clientSecret.ThrowIfEmpty(nameof(_clientSecret))}"
				.UTF8().Base64();
			request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseContentRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateApiError(response.StatusCode, body, _tokenAddress);

			var token = JsonConvert.DeserializeObject<CboeTokenResponse>(body, _jsonSettings)
				?? throw new InvalidOperationException(
					"Cboe returned an empty authentication response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(CboeTokenResponse.AccessToken));
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
		if (delay == null && response.Headers.RetryAfter?.Date != null)
			delay = response.Headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow;
		if (delay != null && delay.Value > TimeSpan.Zero)
			return delay.Value > TimeSpan.FromSeconds(30)
				? TimeSpan.FromSeconds(30)
				: delay.Value;
		return TimeSpan.FromSeconds(Math.Pow(2, attempt));
	}

	private static Exception CreateApiError(HttpStatusCode statusCode, string body, Uri address)
	{
		CboeErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<CboeErrorResponse>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var message = error?.GetMessage();
		if (message.IsEmpty())
			message = body?.Length > 1000 ? body[..1000] : body;
		return new InvalidOperationException(
			$"Cboe request '{address}' failed ({(int)statusCode} {statusCode}): {message}");
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
