namespace StockSharp.PhillipPoems.Native;

sealed class PhillipPoemsClient : Disposable
{
	private const string _apiPrefix = "mobile2/1.0/";
	private const string _tokenPath = "auth/1.0/oauth/token";

	private readonly string _apiKey;
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _tokenSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private string _accessToken;
	private string _refreshToken;
	private DateTime _expiresAt = DateTime.MaxValue;

	public PhillipPoemsClient(bool isDemo, string apiKey, string clientId,
		string clientSecret, string accessToken, string refreshToken)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_clientId = clientId;
		_clientSecret = clientSecret;
		_accessToken = accessToken;
		_refreshToken = refreshToken;
		_http = new()
		{
			BaseAddress = new Uri(isDemo
				? "https://sandboxapi.poems.com.sg/api-gateway/pspl/"
				: "https://api.poems.com.sg/api-gateway/pspl/"),
			Timeout = TimeSpan.FromSeconds(60),
		};
		_http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-PhillipPOEMS");
	}

	public string AccessToken => _accessToken;
	public string RefreshToken => _refreshToken;
	public string AccountNo { get; private set; }
	public string AccountType { get; private set; }

	public Task<PoemsMarketsResponse> GetMarkets(CancellationToken cancellationToken)
		=> Get<PoemsMarketsResponse>("st/counter/exchange", new PoemsCommonRequest(),
			cancellationToken);

	public Task<PoemsCounterSearchResponse> SearchCounters(PoemsSecuritySearchRequest request,
		CancellationToken cancellationToken)
		=> Get<PoemsCounterSearchResponse>("global/counter/search", request, cancellationToken);

	public Task<PoemsCounterIdResponse> GetCounterId(string exchange, string feedCode,
		CancellationToken cancellationToken)
		=> Get<PoemsCounterIdResponse>(
			$"st/counter/counterID/{Escape(exchange)}/{Escape(feedCode)}",
			new PoemsCommonRequest(), cancellationToken);

	public Task<PoemsCounterInfoResponse> GetCounterInfo(string counterId,
		CancellationToken cancellationToken)
		=> Get<PoemsCounterInfoResponse>("st/counter/info", new PoemsCounterInfoRequest
		{
			CounterId = counterId,
		}, cancellationToken);

	public Task<PoemsPriceListResponse> GetPrices(string[] counterIds,
		CancellationToken cancellationToken)
		=> Get<PoemsPriceListResponse>("global/counter/pricelist", new PoemsPriceListRequest
		{
			CounterIds = (counterIds ?? []).Where(id => !id.IsEmpty()).Join(","),
			Size = counterIds?.Length ?? 0,
		}, cancellationToken);

	public Task<PoemsTimeSalesResponse> GetTimeSales(string counterId, string from,
		string to, int size, int page, CancellationToken cancellationToken)
		=> Get<PoemsTimeSalesResponse>("st/counter/timesales", new PoemsTimeSalesRequest
		{
			CounterId = counterId,
			From = from,
			To = to,
			Size = size,
			Page = page,
		}, cancellationToken);

	public Task<PoemsMarketDepthResponse> GetMarketDepth(string counterId,
		CancellationToken cancellationToken)
		=> Post<PoemsMarketDepthResponse>("st/counter/marketdepth",
			new PoemsMarketDepthRequest { CounterId = counterId }, null, null, true,
			cancellationToken);

	public Task<PoemsOrdersResponse> GetTodayOrders(CancellationToken cancellationToken)
		=> Get<PoemsOrdersResponse>("global/order/today", new PoemsCommonRequest(),
			cancellationToken);

	public Task<PoemsAccountDetailsResponse> GetAccountDetails(string accountType,
		CancellationToken cancellationToken)
		=> Get<PoemsAccountDetailsResponse>("st/portfolio/accountdetails",
			new PoemsAccountRequest { AccountType = accountType }, cancellationToken);

	public Task<PoemsHoldingsResponse> GetHoldings(string accountType,
		CancellationToken cancellationToken)
		=> Get<PoemsHoldingsResponse>("st/portfolio/holdings",
			new PoemsAccountRequest { AccountType = accountType }, cancellationToken);

	public Task<PoemsOrderActionResponse> ValidateOrder(PoemsOrderRequest request,
		string encryptedPin, CancellationToken cancellationToken)
		=> Post<PoemsOrderActionResponse>("st/trade/validate", request, encryptedPin,
			null, true, cancellationToken);

	public Task<PoemsOrderActionResponse> SubmitOrder(PoemsOrderRequest request,
		string authToken, CancellationToken cancellationToken)
		=> Post<PoemsOrderActionResponse>("st/trade/submit", request, null,
			authToken.ThrowIfEmpty(nameof(authToken)), false, cancellationToken);

	public Task<PoemsOrderActionResponse> AmendOrder(string orderNo,
		PoemsAmendOrderRequest request, string encryptedPin,
		CancellationToken cancellationToken)
		=> Post<PoemsOrderActionResponse>($"st/order/{Escape(orderNo)}/amend", request,
			encryptedPin, null, false, cancellationToken);

	public Task<PoemsOrderActionResponse> CancelOrder(string orderNo,
		PoemsCancelOrderRequest request, string encryptedPin,
		CancellationToken cancellationToken)
		=> Post<PoemsOrderActionResponse>($"st/order/{Escape(orderNo)}/withdraw", request,
			encryptedPin, null, false, cancellationToken);

	private Task<TResponse> Get<TResponse>(string path, PoemsRequest query,
		CancellationToken cancellationToken)
		where TResponse : PoemsResponse
		=> Send<TResponse>(HttpMethod.Get, path, query, null, null, null, true,
			cancellationToken);

	private Task<TResponse> Post<TResponse>(string path, PoemsRequest form,
		string encryptedPin, string authToken, bool isRetryable,
		CancellationToken cancellationToken)
		where TResponse : PoemsResponse
		=> Send<TResponse>(HttpMethod.Post, path, null, form, encryptedPin, authToken,
			isRetryable, cancellationToken);

	private async Task<TResponse> Send<TResponse>(HttpMethod method, string path,
		PoemsRequest query, PoemsRequest form, string encryptedPin, string authToken,
		bool isRetryable, CancellationToken cancellationToken)
		where TResponse : PoemsResponse
	{
		var relativePath = _apiPrefix + path;
		if (query != null)
			relativePath = BuildPath(relativePath, query);
		var formContent = form == null ? null : SerializeRequest(form);
		var isAuthorizationRetried = false;

		for (var attempt = 1; ; attempt++)
		{
			using var message = new HttpRequestMessage(method, relativePath);
			message.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
				await GetAccessToken(cancellationToken));
			message.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
			if (!encryptedPin.IsEmpty())
				message.Headers.TryAddWithoutValidation("encryptedPIN", encryptedPin);
			if (!authToken.IsEmpty())
				message.Headers.TryAddWithoutValidation("authToken", authToken);
			if (formContent != null)
				message.Content = new StringContent(formContent, Encoding.UTF8,
					"application/x-www-form-urlencoded");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (Exception error) when (isRetryable && attempt < 3 &&
				error is HttpRequestException or TaskCanceledException &&
				!cancellationToken.IsCancellationRequested)
			{
				await Delay(attempt, null, cancellationToken);
				continue;
			}

			using (response)
			{
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.StatusCode == HttpStatusCode.Unauthorized &&
					!isAuthorizationRetried && CanRefresh)
				{
					await RefreshAccessToken(true, cancellationToken);
					isAuthorizationRetried = true;
					continue;
				}
				if (response.IsSuccessStatusCode)
				{
					var result = JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings)
						?? throw new InvalidOperationException(
							"POEMS returned an empty JSON response.");
					if (result.Code != 1)
						throw new InvalidOperationException(
							$"POEMS API error {result.Code}: {result.Message.IsEmpty("Unknown error")}.");
					return result;
				}
				if (isRetryable && attempt < 3 && IsTransient(response.StatusCode))
				{
					await Delay(attempt, GetRetryDelay(response), cancellationToken);
					continue;
				}
				throw CreateException(response, content);
			}
		}
	}

	private bool CanRefresh => !_refreshToken.IsEmpty() && !_clientId.IsEmpty() &&
		!_clientSecret.IsEmpty();

	private async Task<string> GetAccessToken(CancellationToken cancellationToken)
	{
		if (_accessToken.IsEmpty() || _expiresAt <= DateTime.UtcNow.AddSeconds(30))
			await RefreshAccessToken(false, cancellationToken);
		return _accessToken.ThrowIfEmpty(nameof(AccessToken));
	}

	private async Task RefreshAccessToken(bool isForced, CancellationToken cancellationToken)
	{
		if (!isForced && !_accessToken.IsEmpty() &&
			_expiresAt > DateTime.UtcNow.AddSeconds(30))
			return;
		if (!CanRefresh)
			throw new InvalidOperationException(
				"The POEMS access token is unavailable or expired, and refresh credentials are incomplete.");

		await _tokenSync.WaitAsync(cancellationToken);
		try
		{
			if (!isForced && !_accessToken.IsEmpty() &&
				_expiresAt > DateTime.UtcNow.AddSeconds(30))
				return;

			var request = new PoemsTokenRequest
			{
				GrantType = "refresh_token",
				RefreshToken = _refreshToken,
				ClientId = _clientId,
				ClientSecret = _clientSecret,
			};
			using var message = new HttpRequestMessage(HttpMethod.Post, _tokenPath)
			{
				Content = new StringContent(SerializeRequest(request), Encoding.UTF8,
					"application/x-www-form-urlencoded"),
			};
			using var response = await _http.SendAsync(message,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateException(response, content);
			var token = JsonConvert.DeserializeObject<PoemsTokenResponse>(content, _jsonSettings)
				?? throw new InvalidOperationException("POEMS returned an empty token response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(token.AccessToken));
			if (!token.RefreshToken.IsEmpty())
				_refreshToken = token.RefreshToken;
			_expiresAt = DateTime.UtcNow.AddSeconds(Math.Max(1, token.ExpiresIn - 60));
			AccountNo = token.AccountNo;
			AccountType = token.AccountType;
		}
		finally
		{
			_tokenSync.Release();
		}
	}

	private static string BuildPath(string path, object request)
	{
		var query = SerializeRequest(request);
		return query.IsEmpty() ? path : $"{path}?{query}";
	}

	private static string SerializeRequest(object request)
	{
		if (request == null)
			return string.Empty;
		return request.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Select(property => (property,
				attribute: property.GetCustomAttribute<JsonPropertyAttribute>(),
				value: property.GetValue(request)))
			.Where(item => item.attribute != null && item.value != null)
			.Select(item => (item.attribute.PropertyName, value: ToWireValue(item.value)))
			.Where(item => !item.value.IsEmpty())
			.Select(item => $"{Encode(item.PropertyName)}={Encode(item.value)}")
			.Join("&");
	}

	private static string ToWireValue(object value)
	{
		var type = value.GetType();
		if (type.IsEnum)
		{
			var field = type.GetField(value.ToString());
			return field?.GetCustomAttribute<EnumMemberAttribute>()?.Value
				?? Convert.ToInt64(value, CultureInfo.InvariantCulture)
					.ToString(CultureInfo.InvariantCulture);
		}
		return value switch
		{
			bool flag => flag ? "true" : "false",
			DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString(),
		};
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.RequestTimeout || (int)statusCode == 429 ||
			(int)statusCode >= 500;

	private static TimeSpan? GetRetryDelay(HttpResponseMessage response)
		=> response.Headers.RetryAfter?.Delta;

	private static Task Delay(int attempt, TimeSpan? requested,
		CancellationToken cancellationToken)
	{
		var delay = requested ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
		if (delay < TimeSpan.FromMilliseconds(250))
			delay = TimeSpan.FromMilliseconds(250);
		if (delay > TimeSpan.FromSeconds(30))
			delay = TimeSpan.FromSeconds(30);
		return Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateException(HttpResponseMessage response, string content)
	{
		PoemsResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<PoemsResponse>(content);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message.IsEmpty(response.ReasonPhrase).IsEmpty("Unknown error");
		return new HttpRequestException(
			$"POEMS HTTP {(int)response.StatusCode}: {detail}.", null, response.StatusCode);
	}

	private static string Escape(string value)
		=> Encode(value.ThrowIfEmpty(nameof(value)));

	private static string Encode(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_tokenSync.Dispose();
		base.DisposeManaged();
	}
}
