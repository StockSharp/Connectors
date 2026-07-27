namespace StockSharp.Finam.Native;

sealed class FinamRestClient : BaseLogReceiver
{
	private readonly HttpClient _http = new();
	private readonly SemaphoreSlim _authSync = new(1, 1);
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = CreateJsonSettings();
	private readonly string _secret;
	private readonly string _appId;
	private readonly int _maxAttempts;
	private string _accessToken;
	private DateTime _accessExpiresAt;
	private string[] _accountIds = [];
	private DateTime _lastRequestAt;

	public FinamRestClient(string address, SecureString token, string appId,
		int maxAttempts)
	{
		if (!Uri.TryCreate(address?.Trim().TrimEnd('/') + "/", UriKind.Absolute,
			out var uri) || uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"A valid Finam REST API address is required.", nameof(address));

		_secret = token?.UnSecure().ThrowIfEmpty(nameof(token));
		_appId = appId.ThrowIfEmpty(nameof(appId));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.BaseAddress = uri;
		_http.Timeout = TimeSpan.FromSeconds(30);
		_http.DefaultRequestVersion = HttpVersion.Version20;
		_http.DefaultVersionPolicy =
			HttpVersionPolicy.RequestVersionOrHigher;
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Finam/1.0");
	}

	public override string Name => "Finam_REST";

	public string AccessToken
		=> _accessToken.ThrowIfEmpty(nameof(AccessToken));

	public string[] AccountIds => _accountIds;

	public async Task Authenticate(CancellationToken cancellationToken)
	{
		if (!_accessToken.IsEmpty() &&
			_accessExpiresAt > DateTime.UtcNow.AddMinutes(1))
			return;

		await _authSync.WaitAsync(cancellationToken);
		try
		{
			if (!_accessToken.IsEmpty() &&
				_accessExpiresAt > DateTime.UtcNow.AddMinutes(1))
				return;

			var response = await SendUnauthenticated<FinamAuthRequest,
				FinamAuthResponse>("v1/sessions", new()
				{
					Secret = _secret,
					SourceAppId = _appId,
				}, cancellationToken);

			if (response?.Token.IsEmpty() != false)
				throw new InvalidDataException(
					"Finam did not return a session token.");

			_accessToken = response.Token;
			var details = await SendUnauthenticated<FinamTokenDetailsRequest,
				FinamTokenDetails>("v1/sessions/details", new()
				{
					Token = _accessToken,
				}, cancellationToken);

			_accountIds = details?.AccountIds?
				.Where(id => !id.IsEmpty())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray() ?? [];
			_accessExpiresAt = details?.ExpiresAt?.ToUniversalTime()
				?? DateTime.UtcNow.AddMinutes(10);
		}
		finally
		{
			_authSync.Release();
		}
	}

	public Task<FinamAssetPage> GetAssets(string cursor,
		CancellationToken cancellationToken)
		=> Get<FinamAssetPage>("v1/assets/all?only_active=true" +
			(cursor.IsEmpty() ? string.Empty : $"&cursor={Escape(cursor)}"),
			cancellationToken);

	public Task<FinamAssetDetails> GetAsset(string symbol, string accountId,
		CancellationToken cancellationToken)
		=> Get<FinamAssetDetails>($"v1/assets/{Escape(symbol)}" +
			(accountId.IsEmpty() ? string.Empty
				: $"?account_id={Escape(accountId)}"), cancellationToken);

	public Task<FinamQuoteResponse> GetQuote(string symbol,
		CancellationToken cancellationToken)
		=> Get<FinamQuoteResponse>(
			$"v1/instruments/{Escape(symbol)}/quotes/latest",
			cancellationToken);

	public Task<FinamOrderBookResponse> GetOrderBook(string symbol,
		CancellationToken cancellationToken)
		=> Get<FinamOrderBookResponse>(
			$"v1/instruments/{Escape(symbol)}/orderbook",
			cancellationToken);

	public Task<FinamMarketTradesResponse> GetLatestTrades(string symbol,
		CancellationToken cancellationToken)
		=> Get<FinamMarketTradesResponse>(
			$"v1/instruments/{Escape(symbol)}/trades/latest",
			cancellationToken);

	public Task<FinamBarsResponse> GetBars(string symbol, string timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
		=> Get<FinamBarsResponse>(
			$"v1/instruments/{Escape(symbol)}/bars" +
			$"?timeframe={Escape(timeFrame)}" +
			$"&interval.start_time={Escape(FormatDate(from))}" +
			$"&interval.end_time={Escape(FormatDate(to))}",
			cancellationToken);

	public Task<FinamAccount> GetAccount(string accountId,
		CancellationToken cancellationToken)
		=> Get<FinamAccount>($"v1/accounts/{Escape(accountId)}",
			cancellationToken);

	public Task<FinamOrdersResponse> GetOrders(string accountId,
		CancellationToken cancellationToken)
		=> Get<FinamOrdersResponse>(
			$"v1/accounts/{Escape(accountId)}/orders", cancellationToken);

	public Task<FinamOrderState> GetOrder(string accountId, string orderId,
		CancellationToken cancellationToken)
		=> Get<FinamOrderState>(
			$"v1/accounts/{Escape(accountId)}/orders/{Escape(orderId)}",
			cancellationToken);

	public Task<FinamOrderState> PlaceOrder(string accountId,
		FinamOrderRequest request, CancellationToken cancellationToken)
		=> Post<FinamOrderRequest, FinamOrderState>(
			$"v1/accounts/{Escape(accountId)}/orders", request,
			cancellationToken);

	public Task<FinamOrderState> CancelOrder(string accountId, string orderId,
		CancellationToken cancellationToken)
		=> Delete<FinamOrderState>(
			$"v1/accounts/{Escape(accountId)}/orders/{Escape(orderId)}",
			cancellationToken);

	public Task<FinamAccountTradesResponse> GetTrades(string accountId,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		var path = $"v1/accounts/{Escape(accountId)}/trades" +
			$"?limit={Math.Clamp(limit, 1, 1000)}";
		if (from is not null)
			path += $"&interval.start_time={Escape(FormatDate(from.Value))}";
		if (to is not null)
			path += $"&interval.end_time={Escape(FormatDate(to.Value))}";
		return Get<FinamAccountTradesResponse>(path, cancellationToken);
	}

	internal static string SerializeBody<T>(T body)
		=> JsonConvert.SerializeObject(body, CreateJsonSettings());

	private static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			ContractResolver = new DefaultContractResolver
			{
				NamingStrategy = new SnakeCaseNamingStrategy(),
			},
			NullValueHandling = NullValueHandling.Ignore,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc,
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
		};

	private Task<T> Get<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Get, path, null, cancellationToken);

	private Task<T> Delete<T>(string path, CancellationToken cancellationToken)
		=> Send<T>(HttpMethod.Delete, path, null, cancellationToken);

	private Task<TResponse> Post<TRequest, TResponse>(string path, TRequest body,
		CancellationToken cancellationToken)
		=> Send<TResponse>(HttpMethod.Post, path,
			JsonConvert.SerializeObject(body, _jsonSettings), cancellationToken);

	private async Task<TResponse> SendUnauthenticated<TRequest, TResponse>(
		string path, TRequest body, CancellationToken cancellationToken)
	{
		await WaitRateLimit(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Post, path)
		{
			Content = new StringContent(
				JsonConvert.SerializeObject(body, _jsonSettings),
				Encoding.UTF8, "application/json"),
		};
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseContentRead, cancellationToken);
		var payload = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateError(response.StatusCode, payload);
		return payload.IsEmpty()
			? default
			: JsonConvert.DeserializeObject<TResponse>(payload, _jsonSettings);
	}

	private async Task<T> Send<T>(HttpMethod method, string path, string body,
		CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			await Authenticate(cancellationToken);
			await WaitRateLimit(cancellationToken);

			using var request = new HttpRequestMessage(method, path);
			SetAuthorization(request, _accessToken);
			if (body is not null)
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseContentRead, cancellationToken);
			}
			catch (HttpRequestException error) when (attempt < _maxAttempts)
			{
				this.AddWarningLog(
					"Finam {0} retry {1} after a transport error: {2}",
					method, attempt, error.Message);
				await DelayRetry(null, attempt, cancellationToken);
				continue;
			}

			using (response)
			{
				var payload = await response.Content.ReadAsStringAsync(
					cancellationToken);
				if (response.IsSuccessStatusCode)
					return payload.IsEmpty()
						? default
						: JsonConvert.DeserializeObject<T>(payload, _jsonSettings);

				if (response.StatusCode == HttpStatusCode.Unauthorized &&
					attempt < _maxAttempts)
				{
					_accessToken = null;
					_accessExpiresAt = default;
					continue;
				}

				if (attempt < _maxAttempts && IsTransient(response.StatusCode))
				{
					this.AddWarningLog(
						"Finam {0} {1} retry {2} after HTTP {3}.",
						method, SafePath(path), attempt, (int)response.StatusCode);
					await DelayRetry(response, attempt, cancellationToken);
					continue;
				}

				throw CreateError(response.StatusCode, payload);
			}
		}
	}

	private static Exception CreateError(HttpStatusCode statusCode,
		string payload)
	{
		FinamError error = null;
		try
		{
			if (!payload.IsEmpty())
				error = JsonConvert.DeserializeObject<FinamError>(
					payload, CreateJsonSettings());
		}
		catch (JsonException)
		{
		}

		var message = error?.Message;
		if (message.IsEmpty())
			message = payload?.Length > 1000 ? payload[..1000] : payload;

		return new HttpRequestException(
			$"Finam API error {(int)statusCode} {statusCode}" +
			(message.IsEmpty() ? string.Empty : $": {message}"),
			null, statusCode);
	}

	internal static void SetAuthorization(HttpRequestMessage request,
		string token)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));
		request.Headers.Authorization =
			new("Bearer", token.ThrowIfEmpty(nameof(token)));
	}

	private async Task WaitRateLimit(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = TimeSpan.FromMilliseconds(310) -
				(DateTime.UtcNow - _lastRequestAt);
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_lastRequestAt = DateTime.UtcNow;
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.RequestTimeout ||
			statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async Task DelayRetry(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response?.Headers.RetryAfter?.Delta ??
			TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		else if (delay > TimeSpan.FromSeconds(60))
			delay = TimeSpan.FromSeconds(60);
		await Task.Delay(delay, cancellationToken);
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string FormatDate(DateTime value)
		=> value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	private static string SafePath(string path)
	{
		var query = path.IndexOf('?');
		return query < 0 ? path : path[..query];
	}

	protected override void DisposeManaged()
	{
		_authSync.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
