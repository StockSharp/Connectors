namespace StockSharp.Saxo.Native;

sealed class SaxoRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly HttpClient _httpClient = new();
	private readonly SemaphoreSlim _refreshLock = new(1, 1);
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly string _redirectUri;
	private readonly Uri _apiRoot;
	private readonly Uri _tokenUri;
	private readonly Uri _streamAuthorizeUri;
	private string _accessToken;
	private string _refreshToken;
	private DateTimeOffset? _expiresAt;

	public SaxoRestClient(SaxoEnvironments environment, string accessToken, string refreshToken, string clientId,
		string clientSecret, string redirectUri)
	{
		_accessToken = accessToken;
		_refreshToken = refreshToken;
		_clientId = clientId;
		_clientSecret = clientSecret;
		_redirectUri = redirectUri;

		if (environment == SaxoEnvironments.Simulation)
		{
			_apiRoot = new("https://gateway.saxobank.com/sim/openapi/");
			_tokenUri = new("https://sim.logonvalidation.net/token");
			_streamAuthorizeUri = new("https://sim-streaming.saxobank.com/sim/oapi/streaming/ws/authorize");
		}
		else
		{
			_apiRoot = new("https://gateway.saxobank.com/openapi/");
			_tokenUri = new("https://live.logonvalidation.net/token");
			_streamAuthorizeUri = new("https://live-streaming.saxobank.com/oapi/streaming/ws/authorize");
		}

		_httpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
		_httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new("en"));
		TryReadExpiration(_accessToken);
	}

	public override string Name => nameof(Saxo) + "_" + nameof(SaxoRestClient);

	public string AccessToken => _accessToken;
	public string RefreshToken => _refreshToken;

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		_refreshLock.Dispose();
		base.DisposeManaged();
	}

	public async Task EnsureToken(CancellationToken cancellationToken)
	{
		if (_accessToken.IsEmpty() || (_expiresAt is not null && _expiresAt <= DateTimeOffset.UtcNow.AddMinutes(2)))
		{
			if (_refreshToken.IsEmpty())
				throw new InvalidOperationException("The Saxo access token is missing or about to expire and no refresh token is configured.");
			await RefreshAccessToken(cancellationToken);
		}
	}

	public async Task<SaxoSessionInfo> GetSession(string configuredAccountKey, CancellationToken cancellationToken)
	{
		var user = await Get<SaxoUser>("port/v1/users/me", cancellationToken);
		var client = await Get<SaxoClient>("port/v1/clients/me", cancellationToken);
		var accounts = await GetFeed<SaxoAccount>("port/v1/accounts/me?$top=1000", cancellationToken);
		var clientKey = user.ClientKey.IsEmpty(client.ClientKey);
		var accountKey = configuredAccountKey.IsEmpty(client.DefaultAccountKey).IsEmpty(
			accounts.Data?.FirstOrDefault()?.AccountKey);
		var account = (accounts.Data ?? []).FirstOrDefault(a => a.AccountKey.EqualsIgnoreCase(accountKey));
		if (account == null)
			throw new InvalidOperationException($"Saxo account '{accountKey}' is not available to the current token.");
		return new()
		{
			ClientKey = clientKey,
			AccountKey = account.AccountKey,
			AccountId = account.AccountId,
			Currency = account.Currency,
		};
	}

	public Task<SaxoFeed<SaxoInstrumentSummary>> FindInstruments(string keywords, string assetTypes, int count,
		string accountKey, CancellationToken cancellationToken)
	{
		var path = $"ref/v1/instruments?$top={Math.Clamp(count, 1, 1000)}&IncludeNonTradable=false&AccountKey={Escape(accountKey)}";
		if (!keywords.IsEmpty())
			path += $"&Keywords={Escape(keywords)}";
		if (!assetTypes.IsEmpty())
			path += $"&AssetTypes={Escape(assetTypes)}";
		return Get<SaxoFeed<SaxoInstrumentSummary>>(path, cancellationToken);
	}

	public Task<SaxoInstrumentDetails> GetInstrument(long uic, string assetType, string accountKey,
		CancellationToken cancellationToken)
		=> Get<SaxoInstrumentDetails>($"ref/v1/instruments/details/{uic}/{Escape(assetType)}" +
			$"?AccountKey={Escape(accountKey)}&FieldGroups=OrderSetting,SupportedOrderTypeSettings", cancellationToken);

	public Task<SaxoFutureSpace> GetFutureSpace(long identifier, CancellationToken cancellationToken)
		=> Get<SaxoFutureSpace>($"ref/v1/instruments/futuresspaces/{identifier}", cancellationToken);

	public Task<SaxoOptionSpace> GetOptionSpace(long identifier, string clientKey, CancellationToken cancellationToken)
		=> Get<SaxoOptionSpace>($"ref/v1/instruments/contractoptionspaces/{identifier}?ClientKey={Escape(clientKey)}" +
			"&OptionSpaceSegment=AllDates", cancellationToken);

	public Task<SaxoChartResponse> GetCandles(SaxoInstrument instrument, int horizon, int count, DateTimeOffset? to,
		CancellationToken cancellationToken)
	{
		var path = $"chart/v3/charts?Uic={instrument.Uic}&AssetType={Escape(instrument.AssetType)}" +
			$"&FieldGroups=ChartInfo,Data,DisplayAndFormat&Horizon={horizon}&Count={Math.Clamp(count, 1, 1200)}";
		if (to is not null)
			path += $"&Mode=UpTo&Time={Escape(to.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}";
		return Get<SaxoChartResponse>(path, cancellationToken);
	}

	public Task<SaxoSubscriptionResponse<SaxoFeed<SaxoInfoPrice>>> SubscribePrices(
		SaxoInfoPriceSubscriptionRequest request, CancellationToken cancellationToken)
		=> Post<SaxoInfoPriceSubscriptionRequest, SaxoSubscriptionResponse<SaxoFeed<SaxoInfoPrice>>>(
			"trade/v1/infoprices/subscriptions", request, cancellationToken);

	public Task<SaxoSubscriptionResponse<SaxoChartResponse>> SubscribeCandles(SaxoChartSubscriptionRequest request,
		CancellationToken cancellationToken)
		=> Post<SaxoChartSubscriptionRequest, SaxoSubscriptionResponse<SaxoChartResponse>>(
			"chart/v3/charts/subscriptions", request, cancellationToken);

	public Task<SaxoSubscriptionResponse<SaxoBalance>> SubscribeBalance(SaxoBalanceSubscriptionRequest request,
		CancellationToken cancellationToken)
		=> Post<SaxoBalanceSubscriptionRequest, SaxoSubscriptionResponse<SaxoBalance>>(
			"port/v1/balances/subscriptions", request, cancellationToken);

	public Task<SaxoSubscriptionResponse<SaxoFeed<SaxoNetPosition>>> SubscribePositions(
		SaxoNetPositionSubscriptionRequest request, CancellationToken cancellationToken)
		=> Post<SaxoNetPositionSubscriptionRequest, SaxoSubscriptionResponse<SaxoFeed<SaxoNetPosition>>>(
			"port/v1/netpositions/subscriptions", request, cancellationToken);

	public Task<SaxoSubscriptionResponse<SaxoFeed<SaxoActivity>>> SubscribeActivities(
		SaxoActivitySubscriptionRequest request, CancellationToken cancellationToken)
		=> Post<SaxoActivitySubscriptionRequest, SaxoSubscriptionResponse<SaxoFeed<SaxoActivity>>>(
			"ens/v1/activities/subscriptions", request, cancellationToken);

	public Task DeleteSubscription(string servicePath, string contextId, string referenceId,
		CancellationToken cancellationToken)
		=> Delete<SaxoError>($"{servicePath}/{Escape(contextId)}/{Escape(referenceId)}", cancellationToken);

	public Task<SaxoOrderResult> PlaceOrder(SaxoOrderRequest request, CancellationToken cancellationToken)
		=> Post<SaxoOrderRequest, SaxoOrderResult>("trade/v2/orders", request, cancellationToken, request.ExternalReference);

	public Task<SaxoOrderResult> ModifyOrder(SaxoOrderRequest request, CancellationToken cancellationToken)
		=> Patch<SaxoOrderRequest, SaxoOrderResult>("trade/v2/orders", request, cancellationToken, request.ExternalReference);

	public Task<SaxoOrderResult> CancelOrder(string accountKey, string orderId, CancellationToken cancellationToken)
		=> Delete<SaxoOrderResult>($"trade/v2/orders/{Escape(orderId)}?AccountKey={Escape(accountKey)}", cancellationToken);

	public Task<SaxoFeed<SaxoOpenOrder>> GetOpenOrders(string accountKey, CancellationToken cancellationToken)
		=> GetFeed<SaxoOpenOrder>($"port/v1/orders?$top=1000&Status=All&AccountKey={Escape(accountKey)}", cancellationToken);

	public Task<SaxoFeed<SaxoActivity>> GetOrderActivities(string accountKey, DateTimeOffset? from, DateTimeOffset? to,
		CancellationToken cancellationToken)
	{
		var path = $"cs/v1/audit/orderactivities?$top=500&EntryType=All&AccountKey={Escape(accountKey)}";
		if (from is not null)
			path += $"&FromDateTime={Escape(from.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}";
		if (to is not null)
			path += $"&ToDateTime={Escape(to.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}";
		return GetFeed<SaxoActivity>(path, cancellationToken);
	}

	public Task<SaxoBalance> GetBalance(string clientKey, string accountKey, CancellationToken cancellationToken)
		=> Get<SaxoBalance>($"port/v1/balances?ClientKey={Escape(clientKey)}&AccountKey={Escape(accountKey)}" +
			"&FieldGroups=CalculateCashForTrading,MarginOverview", cancellationToken);

	public Task<SaxoFeed<SaxoNetPosition>> GetPositions(string clientKey, string accountKey, CancellationToken cancellationToken)
		=> GetFeed<SaxoNetPosition>($"port/v1/netpositions?$top=1000&ClientKey={Escape(clientKey)}" +
			$"&AccountKey={Escape(accountKey)}&FieldGroups=DisplayAndFormat,ExchangeInfo,NetPositionBase,NetPositionView", cancellationToken);

	public Task ReauthorizeStreaming(string contextId, CancellationToken cancellationToken)
		=> Send<SaxoError>(HttpMethod.Put,
			$"{_streamAuthorizeUri}?contextid={Escape(contextId)}", null, null, cancellationToken, false);

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, null, null, cancellationToken, true);

	private async Task<SaxoFeed<T>> GetFeed<T>(string path, CancellationToken cancellationToken)
	{
		var data = new List<T>();
		var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		SaxoFeed<T> page = null;
		for (var pageNumber = 0; !path.IsEmpty() && pageNumber < 100; pageNumber++)
		{
			if (!pages.Add(path))
				throw new InvalidOperationException($"Saxo OpenAPI returned a repeated pagination link '{path}'.");
			page = await Get<SaxoFeed<T>>(path, cancellationToken);
			data.AddRange(page?.Data ?? []);
			path = page?.Next;
		}
		if (!path.IsEmpty())
			throw new InvalidOperationException("Saxo OpenAPI pagination exceeded 100 pages.");
		return new()
		{
			Data = [.. data],
			Count = page?.Count ?? data.Count,
			NextPoll = page?.NextPoll,
		};
	}

	private Task<TResponse> Post<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken,
		string requestId = null)
		=> Send<TResponse>(HttpMethod.Post, path, Serialize(request), requestId, cancellationToken, true);

	private Task<TResponse> Patch<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken,
		string requestId = null)
		=> Send<TResponse>(HttpMethod.Patch, path, Serialize(request), requestId, cancellationToken, true);

	private Task<T> Delete<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Delete, path, null, null, cancellationToken, true);

	private async Task<T> Send<T>(HttpMethod method, string path, string body, string requestId,
		CancellationToken cancellationToken, bool retryUnauthorized)
	{
		await EnsureToken(cancellationToken);
		using var request = new HttpRequestMessage(method, CreateUri(path));
		var requestToken = _accessToken;
		request.Headers.Authorization = new("Bearer", requestToken);
		if (!requestId.IsEmpty())
			request.Headers.TryAddWithoutValidation("X-Request-ID", requestId);
		if (body != null)
			request.Content = new StringContent(body, Encoding.UTF8, "application/json");

		using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (response.StatusCode == HttpStatusCode.Unauthorized && retryUnauthorized && !_refreshToken.IsEmpty())
		{
			await RefreshAccessToken(cancellationToken, requestToken);
			return await Send<T>(method, path, body, requestId, cancellationToken, false);
		}
		if (!response.IsSuccessStatusCode)
		{
			var error = Deserialize<SaxoError>(content);
			throw new InvalidOperationException($"Saxo OpenAPI {method} {request.RequestUri.AbsolutePath} failed " +
				$"({(int)response.StatusCode}, {error?.ErrorCode}): {error?.Message.IsEmpty(response.ReasonPhrase)}");
		}
		return content.IsEmpty() ? default : Deserialize<T>(content);
	}

	private async Task RefreshAccessToken(CancellationToken cancellationToken, string rejectedToken = null)
	{
		await _refreshLock.WaitAsync(cancellationToken);
		try
		{
			if (!rejectedToken.IsEmpty() && !rejectedToken.Equals(_accessToken, StringComparison.Ordinal))
				return;
			if (rejectedToken.IsEmpty() && !_accessToken.IsEmpty() && _expiresAt is not null &&
				_expiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
				return;
			if (_refreshToken.IsEmpty() || _clientId.IsEmpty() || _clientSecret.IsEmpty())
				throw new InvalidOperationException("Saxo token refresh requires RefreshToken, ClientId, and ClientSecret.");

			var form = $"grant_type=refresh_token&refresh_token={Escape(_refreshToken)}";
			if (!_redirectUri.IsEmpty())
				form += $"&redirect_uri={Escape(_redirectUri)}";
			using var request = new HttpRequestMessage(HttpMethod.Post, _tokenUri)
			{
				Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"),
			};
			var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
			request.Headers.Authorization = new("Basic", credentials);
			using var response = await _httpClient.SendAsync(request, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				var error = Deserialize<SaxoError>(content);
				throw new InvalidOperationException($"Saxo OAuth token refresh failed ({(int)response.StatusCode}, " +
					$"{error?.ErrorCode}): {error?.Message.IsEmpty(response.ReasonPhrase)}");
			}

			var token = Deserialize<SaxoTokenResponse>(content)
				?? throw new InvalidOperationException("Saxo OAuth token refresh returned an empty response.");
			_accessToken = token.AccessToken.ThrowIfEmpty(nameof(SaxoTokenResponse.AccessToken));
			if (!token.RefreshToken.IsEmpty())
				_refreshToken = token.RefreshToken;
			_expiresAt = token.ExpiresIn > 0 ? DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn) : null;
			TryReadExpiration(_accessToken);
		}
		finally
		{
			_refreshLock.Release();
		}
	}

	private void TryReadExpiration(string token)
	{
		try
		{
			var parts = token?.Split('.');
			if (parts?.Length != 3)
				return;
			var payload = parts[1].Replace('-', '+').Replace('_', '/');
			payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
			var jwt = Deserialize<SaxoJwtPayload>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
			if (jwt?.Expiration > 0)
				_expiresAt = DateTimeOffset.FromUnixTimeSeconds(jwt.Expiration);
		}
		catch (FormatException)
		{
		}
	}

	private Uri CreateUri(string path)
	{
		if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
			return absolute;
		if (path.StartsWith('/'))
			return new(new Uri(_apiRoot.GetLeftPart(UriPartial.Authority)), path);
		return new(_apiRoot, path.TrimStart('/'));
	}

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);
	private static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, _jsonSettings);
	private static T Deserialize<T>(string value)
		=> value.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(value, _jsonSettings);
}
