namespace StockSharp.Questrade.Native;

sealed class QuestradeRestClient : BaseLogReceiver
{
	private static readonly Uri _tokenUri = new("https://login.questrade.com/oauth2/token");
	private readonly HttpClient _http = new();
	private readonly SemaphoreSlim _tokenLock = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTimeOffset,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private string _accessToken;
	private string _refreshToken;
	private Uri _apiRoot;
	private DateTimeOffset _expiresAt;

	public QuestradeRestClient(string accessToken, string refreshToken, string apiServer)
	{
		_accessToken = accessToken;
		_refreshToken = refreshToken;
		if (!apiServer.IsEmpty())
			_apiRoot = NormalizeApiRoot(apiServer);
		if (!_accessToken.IsEmpty())
			_expiresAt = DateTimeOffset.UtcNow.AddMinutes(25);
	}

	public event Action<string, string, string> CredentialsChanged;

	public string AccessToken => _accessToken;
	public string ApiServer => _apiRoot?.ToString();

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_accessToken.IsEmpty() || _apiRoot == null)
			await EnsureToken(true, null, cancellationToken);
		if (_accessToken.IsEmpty() || _apiRoot == null)
			throw new InvalidOperationException("Questrade requires RefreshToken or both AccessToken and ApiServer.");
		await GetTime(cancellationToken);
	}

	public Task<QuestradeTimeResponse> GetTime(CancellationToken cancellationToken)
		=> Get<QuestradeTimeResponse>("time", cancellationToken);

	public Task<QuestradeAccountsResponse> GetAccounts(CancellationToken cancellationToken)
		=> Get<QuestradeAccountsResponse>("accounts", cancellationToken);

	public Task<QuestradeBalancesResponse> GetBalances(string account, CancellationToken cancellationToken)
		=> Get<QuestradeBalancesResponse>($"accounts/{Escape(account)}/balances", cancellationToken);

	public Task<QuestradePositionsResponse> GetPositions(string account, CancellationToken cancellationToken)
		=> Get<QuestradePositionsResponse>($"accounts/{Escape(account)}/positions", cancellationToken);

	public Task<QuestradeOrdersResponse> GetOrders(string account, DateTimeOffset? from, DateTimeOffset? to,
		CancellationToken cancellationToken)
	{
		var query = new List<string> { "stateFilter=All" };
		if (from != null)
			query.Add($"startTime={Escape(from.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))}");
		if (to != null)
			query.Add($"endTime={Escape(to.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))}");
		return Get<QuestradeOrdersResponse>($"accounts/{Escape(account)}/orders?{string.Join("&", query)}", cancellationToken);
	}

	public Task<QuestradeExecutionsResponse> GetExecutions(string account, DateTimeOffset? from, DateTimeOffset? to,
		CancellationToken cancellationToken)
	{
		var query = new List<string>();
		if (from != null)
			query.Add($"startTime={Escape(from.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))}");
		if (to != null)
			query.Add($"endTime={Escape(to.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))}");
		var suffix = query.Count == 0 ? string.Empty : $"?{string.Join("&", query)}";
		return Get<QuestradeExecutionsResponse>($"accounts/{Escape(account)}/executions{suffix}", cancellationToken);
	}

	public Task<QuestradeSymbolSearchResponse> SearchSymbols(string prefix, int offset, CancellationToken cancellationToken)
		=> Get<QuestradeSymbolSearchResponse>($"symbols/search?prefix={Escape(prefix)}&offset={offset.ToString(CultureInfo.InvariantCulture)}", cancellationToken);

	public Task<QuestradeSymbolsResponse> GetSymbol(long symbolId, CancellationToken cancellationToken)
		=> Get<QuestradeSymbolsResponse>($"symbols/{symbolId.ToString(CultureInfo.InvariantCulture)}", cancellationToken);

	public Task<QuestradeSymbolsResponse> GetSymbols(IEnumerable<long> symbolIds, CancellationToken cancellationToken)
		=> Get<QuestradeSymbolsResponse>($"symbols?ids={symbolIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).JoinComma()}", cancellationToken);

	public Task<QuestradeQuotesResponse> GetQuote(long symbolId, CancellationToken cancellationToken)
		=> Get<QuestradeQuotesResponse>($"markets/quotes/{symbolId.ToString(CultureInfo.InvariantCulture)}", cancellationToken);

	public Task<QuestradeQuotesResponse> StartQuoteStream(IEnumerable<long> symbolIds, CancellationToken cancellationToken)
		=> Get<QuestradeQuotesResponse>($"markets/quotes?ids={symbolIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).JoinComma()}&stream=true&mode=WebSocket", cancellationToken);

	public Task<QuestradeCandlesResponse> GetCandles(long symbolId, DateTimeOffset from, DateTimeOffset to,
		string interval, CancellationToken cancellationToken)
		=> Get<QuestradeCandlesResponse>($"markets/candles/{symbolId.ToString(CultureInfo.InvariantCulture)}" +
			$"?startTime={Escape(from.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))}" +
			$"&endTime={Escape(to.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))}&interval={Escape(interval)}", cancellationToken);

	public Task<QuestradeStreamPortResponse> GetNotificationPort(CancellationToken cancellationToken)
		=> Get<QuestradeStreamPortResponse>("notifications?mode=WebSocket", cancellationToken);

	public Task<QuestradeOrderResult> PlaceOrder(string account, QuestradeOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<QuestradeOrderResult>(HttpMethod.Post, $"accounts/{Escape(account)}/orders", request, cancellationToken);

	public Task<QuestradeOrderResult> ReplaceOrder(string account, long orderId, QuestradeOrderRequest request,
		CancellationToken cancellationToken)
		=> Send<QuestradeOrderResult>(HttpMethod.Post,
			$"accounts/{Escape(account)}/orders/{orderId.ToString(CultureInfo.InvariantCulture)}", request, cancellationToken);

	public Task<QuestradeCancelResult> CancelOrder(string account, long orderId, CancellationToken cancellationToken)
		=> Send<QuestradeCancelResult>(HttpMethod.Delete,
			$"accounts/{Escape(account)}/orders/{orderId.ToString(CultureInfo.InvariantCulture)}", null, cancellationToken);

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, null, cancellationToken);

	private async Task<T> Send<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
	{
		await EnsureToken(false, null, cancellationToken);
		var rejectedToken = _accessToken;
		var authorizationRetried = false;
		var rateLimitRetried = false;
		while (true)
		{
			using var request = new HttpRequestMessage(method, new Uri(_apiRoot, path));
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (body != null)
				request.Content = new StringContent(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8, "application/json");
			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.StatusCode == HttpStatusCode.Unauthorized && !authorizationRetried && !_refreshToken.IsEmpty())
			{
				authorizationRetried = true;
				await EnsureToken(true, rejectedToken, cancellationToken);
				continue;
			}
			if (response.StatusCode == HttpStatusCode.TooManyRequests && !rateLimitRetried &&
				GetRateLimitDelay(response) is { } delay && delay <= TimeSpan.FromSeconds(30))
			{
				rateLimitRetried = true;
				await Task.Delay(delay, cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content);
			if (typeof(T) == typeof(string))
				return (T)(object)content;
			return Deserialize<T>(content) ?? throw new InvalidOperationException($"Questrade {path} returned an empty response.");
		}
	}

	private async Task EnsureToken(bool force, string rejectedToken, CancellationToken cancellationToken)
	{
		if (!force && !_accessToken.IsEmpty() && (_expiresAt == default || _expiresAt > DateTimeOffset.UtcNow.AddMinutes(5)))
			return;
		if (_refreshToken.IsEmpty())
			return;
		await _tokenLock.WaitAsync(cancellationToken);
		try
		{
			if (force && !rejectedToken.IsEmpty() && !_accessToken.Equals(rejectedToken, StringComparison.Ordinal))
				return;
			if (!force && !_accessToken.IsEmpty() && (_expiresAt == default || _expiresAt > DateTimeOffset.UtcNow.AddMinutes(5)))
				return;
			using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUri)
			{
				Content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
				{
					new("grant_type", "refresh_token"),
					new("refresh_token", _refreshToken),
				}),
			};
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			using var response = await _http.SendAsync(request, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content);
			var token = Deserialize<QuestradeTokenResponse>(content)
				?? throw new InvalidOperationException("Questrade token endpoint returned an empty response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(QuestradeTokenResponse.AccessToken));
			_refreshToken = token.RefreshToken.IsEmpty(_refreshToken);
			_apiRoot = NormalizeApiRoot(token.ApiServer.ThrowIfEmpty(nameof(QuestradeTokenResponse.ApiServer)));
			_expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, token.ExpiresIn));
			CredentialsChanged?.Invoke(_accessToken, _refreshToken, _apiRoot.ToString());
		}
		finally
		{
			_tokenLock.Release();
		}
	}

	private T Deserialize<T>(string content)
		=> content.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(content, _jsonSettings);

	private Exception CreateError(HttpStatusCode statusCode, string content)
	{
		QuestradeErrorResponse error = null;
		try
		{
			error = Deserialize<QuestradeErrorResponse>(content);
		}
		catch (JsonException)
		{
		}
		var message = error?.Message.IsEmpty(error?.ErrorDescription).IsEmpty(error?.Error).IsEmpty(content);
		if (message?.Length > 1000)
			message = message[..1000];
		return new HttpRequestException($"Questrade API error {(int)statusCode}" +
			(error?.Code == null ? string.Empty : $"/{error.Code.Value.ToString(CultureInfo.InvariantCulture)}") +
			(message.IsEmpty() ? string.Empty : $": {message}"), null, statusCode);
	}

	private static Uri NormalizeApiRoot(string apiServer)
	{
		var uri = new Uri(apiServer, UriKind.Absolute);
		var builder = new UriBuilder(uri);
		var path = builder.Path.TrimEnd('/');
		if (!path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
			path += "/v1";
		builder.Path = path + "/";
		builder.Query = string.Empty;
		builder.Fragment = string.Empty;
		return builder.Uri;
	}

	private static TimeSpan? GetRateLimitDelay(HttpResponseMessage response)
	{
		var retryAfter = response.Headers.RetryAfter;
		if (retryAfter?.Delta is { } delta)
			return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
		if (retryAfter?.Date is { } date)
			return date <= DateTimeOffset.UtcNow ? TimeSpan.Zero : date - DateTimeOffset.UtcNow;
		if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
			long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var reset))
		{
			var target = DateTimeOffset.FromUnixTimeSeconds(reset);
			return target <= DateTimeOffset.UtcNow ? TimeSpan.FromMilliseconds(100) : target - DateTimeOffset.UtcNow;
		}
		return null;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_tokenLock.Dispose();
		base.DisposeManaged();
	}
}
