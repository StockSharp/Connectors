namespace StockSharp.Paxos.Native;

using StockSharp.Paxos.Native.Model;

sealed class PaxosApiException : InvalidOperationException
{
	public PaxosApiException(HttpStatusCode statusCode, string errorType,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		ErrorType = errorType;
	}

	public HttpStatusCode StatusCode { get; }
	public string ErrorType { get; }
}

sealed class PaxosRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 32 * 1024 * 1024;
	private static readonly TimeSpan _minimumRequestInterval =
		TimeSpan.FromMilliseconds(50);
	private readonly HttpClient _client;
	private readonly Uri _oauthEndpoint;
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly string _scopes;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private string _accessToken;
	private DateTime _tokenExpiresAt;
	private DateTime _lastRequestTime;
	private bool _isDisposed;

	public PaxosRestClient(string apiEndpoint, string oauthEndpoint,
		SecureString clientId, SecureString clientSecret, string scopes)
	{
		var apiUri = ValidateEndpoint(apiEndpoint, nameof(apiEndpoint), true);
		_oauthEndpoint = ValidateEndpoint(oauthEndpoint, nameof(oauthEndpoint),
			false);
		var hasClientId = !clientId.IsEmpty();
		var hasClientSecret = !clientSecret.IsEmpty();
		if (hasClientId != hasClientSecret)
			throw new ArgumentException(
				"Paxos Client ID and Client Secret must be configured together.");
		_clientId = hasClientId ? clientId.UnSecure().Trim() : null;
		_clientSecret = hasClientSecret ? clientSecret.UnSecure().Trim() : null;
		_scopes = scopes?.Trim();
		_client = new()
		{
			BaseAddress = apiUri,
			Timeout = TimeSpan.FromSeconds(45),
		};
		_client.DefaultRequestHeaders.Accept.Add(new("application/json"));
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Paxos/1.0");
	}

	public override string Name => "Paxos_REST";

	public bool IsAuthenticationAvailable => !_clientId.IsEmpty();

	public async ValueTask<PaxosMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> (await GetAsync<PaxosMarketsResponse>("markets", false,
			cancellationToken))?.Markets ?? [];

	public ValueTask<PaxosOrderBook> GetOrderBookAsync(string market,
		CancellationToken cancellationToken)
		=> GetAsync<PaxosOrderBook>("markets/" + Escape(market) +
			"/order-book", false, cancellationToken);

	public ValueTask<PaxosTicker> GetTickerAsync(string market,
		CancellationToken cancellationToken)
		=> GetAsync<PaxosTicker>("markets/" + Escape(market) + "/ticker",
			false, cancellationToken);

	public async ValueTask<PaxosPublicExecution[]> GetRecentExecutionsAsync(
		string market, CancellationToken cancellationToken)
		=> (await GetAsync<PaxosRecentExecutionsResponse>("markets/" +
			Escape(market) + "/recent-executions", false,
			cancellationToken))?.Items ?? [];

	public async ValueTask<PaxosCandle[]> GetCandlesAsync(string market,
		PaxosCandleIncrements increment, DateTime? from, DateTime? to,
		int pageSize, int maximum, CancellationToken cancellationToken)
	{
		ValidatePaging(pageSize, maximum, 1000);
		var path = "markets/" + Escape(market) + "/historical-candles?limit=" +
			Format(pageSize.Min(maximum)) + "&order=ASC&increment=" + Escape(
				PaxosEnumConverter<PaxosCandleIncrements>.ToWire(increment));
		if (from is DateTime start)
			path += "&range.begin=" + Escape(start.ToPaxosTime());
		if (to is DateTime end)
			path += "&range.end=" + Escape(end.ToPaxosTime());

		var result = new List<PaxosCandle>();
		string cursor = null;
		var visited = new HashSet<string>(StringComparer.Ordinal);
		while (result.Count < maximum)
		{
			var requestPath = AddCursor(path, cursor);
			if (!visited.Add(requestPath))
				break;
			var response = await GetAsync<PaxosCandlesResponse>(requestPath,
				false, cancellationToken);
			var items = response?.Items ?? [];
			result.AddRange(items.Take(maximum - result.Count));
			if (items.Length == 0 || response?.NextPageCursor.IsEmpty() != false)
				break;
			cursor = response.NextPageCursor;
		}
		return [.. result];
	}

	public ValueTask<PaxosProfile[]> GetProfilesAsync(int pageSize, int maximum,
		CancellationToken cancellationToken)
		=> GetPagesAsync<PaxosProfile>("profiles?limit=" +
			Format(ValidatePaging(pageSize, maximum, 1000)), maximum,
			cancellationToken);

	public async ValueTask<PaxosProfileBalance[]> GetBalancesAsync(
		string profileId, CancellationToken cancellationToken)
		=> (await GetAsync<PaxosItemsResponse<PaxosProfileBalance>>(
			"profiles/" + Escape(profileId) + "/balances", true,
			cancellationToken))?.Items ?? [];

	public ValueTask<PaxosOrder> CreateOrderAsync(string profileId,
		PaxosCreateOrderRequest request, CancellationToken cancellationToken)
		=> PostAsync<PaxosOrder, PaxosCreateOrderRequest>("profiles/" +
			Escape(profileId) + "/orders", request, cancellationToken);

	public ValueTask<PaxosOrder> GetOrderAsync(string profileId, string orderId,
		CancellationToken cancellationToken)
		=> GetAsync<PaxosOrder>("profiles/" + Escape(profileId) + "/orders/" +
			Escape(orderId), true, cancellationToken);

	public async ValueTask CancelOrderAsync(string profileId, string orderId,
		CancellationToken cancellationToken)
		=> _ = await SendAsync<PaxosEmptyResponse>(HttpMethod.Delete,
			"profiles/" + Escape(profileId) + "/orders/" + Escape(orderId), [],
			null, true, true, cancellationToken);

	public ValueTask<PaxosOrder[]> GetOrdersAsync(string profileId,
		string refId, DateTime? from, DateTime? to, int pageSize, int maximum,
		CancellationToken cancellationToken)
	{
		var size = ValidatePaging(pageSize, maximum, 1000);
		var path = "orders?limit=" + Format(size);
		if (!profileId.IsEmpty())
			path += "&profile_id=" + Escape(profileId);
		if (!refId.IsEmpty())
			path += "&ref_ids=" + Escape(refId);
		if (from is DateTime start)
			path += "&order_time.begin=" + Escape(start.ToPaxosTime());
		if (to is DateTime end)
			path += "&order_time.end=" + Escape(end.ToPaxosTime());
		return GetPagesAsync<PaxosOrder>(path, maximum, cancellationToken);
	}

	public ValueTask<PaxosPrivateExecution[]> GetExecutionsAsync(
		string profileId, string orderId, DateTime? from, DateTime? to,
		int pageSize, int maximum, CancellationToken cancellationToken)
	{
		var size = ValidatePaging(pageSize, maximum, 1000);
		var path = "executions?limit=" + Format(size);
		if (!profileId.IsEmpty())
			path += "&profile_id=" + Escape(profileId);
		if (!orderId.IsEmpty())
			path += "&order_id=" + Escape(orderId);
		if (from is DateTime start)
			path += "&range.begin=" + Escape(start.ToPaxosTime());
		if (to is DateTime end)
			path += "&range.end=" + Escape(end.ToPaxosTime());
		return GetPagesAsync<PaxosPrivateExecution>(path, maximum,
			cancellationToken);
	}

	public ValueTask<PaxosTransfer> CreateCryptoWithdrawalAsync(
		PaxosCryptoWithdrawalRequest request,
		CancellationToken cancellationToken)
		=> PostAsync<PaxosTransfer, PaxosCryptoWithdrawalRequest>(
			"transfer/crypto-withdrawals", request, cancellationToken);

	public ValueTask<PaxosTransfer> CreateInternalTransferAsync(
		PaxosProfileTransferRequest request,
		CancellationToken cancellationToken)
		=> PostAsync<PaxosTransfer, PaxosProfileTransferRequest>(
			"transfer/internal", request, cancellationToken);

	public ValueTask<PaxosTransfer> CreatePaxosTransferAsync(
		PaxosProfileTransferRequest request,
		CancellationToken cancellationToken)
		=> PostAsync<PaxosTransfer, PaxosProfileTransferRequest>(
			"transfer/paxos", request, cancellationToken);

	public ValueTask<PaxosTransfer> GetTransferAsync(string id,
		CancellationToken cancellationToken)
		=> GetAsync<PaxosTransfer>("transfer/transfers/" + Escape(id), true,
			cancellationToken);

	public ValueTask<PaxosTransfer[]> GetTransfersAsync(string profileId,
		string refId, DateTime? from, DateTime? to, int pageSize, int maximum,
		CancellationToken cancellationToken)
	{
		var size = ValidatePaging(pageSize, maximum, 1000);
		var path = "transfer/transfers?limit=" + Format(size) +
			"&order=ASC&order_by=CREATED_AT";
		if (!profileId.IsEmpty())
			path += "&profile_ids=" + Escape(profileId);
		if (!refId.IsEmpty())
			path += "&ref_ids=" + Escape(refId);
		if (from is DateTime start)
			path += "&created_at.gte=" + Escape(start.ToPaxosTime());
		if (to is DateTime end)
			path += "&created_at.lte=" + Escape(end.ToPaxosTime());
		return GetPagesAsync<PaxosTransfer>(path, maximum, cancellationToken);
	}

	public ValueTask<PaxosStablecoinConversion>
		CreateStablecoinConversionAsync(PaxosStablecoinConversionRequest request,
			CancellationToken cancellationToken)
		=> PostAsync<PaxosStablecoinConversion,
			PaxosStablecoinConversionRequest>("conversion/stablecoins", request,
				cancellationToken);

	public ValueTask<PaxosStablecoinConversion> GetStablecoinConversionAsync(
		string id, CancellationToken cancellationToken)
		=> GetAsync<PaxosStablecoinConversion>("conversion/stablecoins/" +
			Escape(id), true, cancellationToken);

	public ValueTask<PaxosStablecoinConversion[]> GetStablecoinConversionsAsync(
		string profileId, string refId, DateTime? from, DateTime? to,
		int pageSize, int maximum, CancellationToken cancellationToken)
	{
		var size = ValidatePaging(pageSize, maximum, 1000);
		var path = "conversion/stablecoins?limit=" + Format(size) +
			"&order=ASC";
		if (!profileId.IsEmpty())
			path += "&profile_id=" + Escape(profileId);
		if (!refId.IsEmpty())
			path += "&ref_id=" + Escape(refId);
		if (from is DateTime start)
			path += "&created_at.begin=" + Escape(start.ToPaxosTime());
		if (to is DateTime end)
			path += "&created_at.end=" + Escape(end.ToPaxosTime());
		return GetPagesAsync<PaxosStablecoinConversion>(path, maximum,
			cancellationToken);
	}

	public ValueTask<PaxosStablecoinConversion> CancelStablecoinConversionAsync(
		string id, CancellationToken cancellationToken)
		=> SendAsync<PaxosStablecoinConversion>(HttpMethod.Delete,
			"conversion/stablecoins/" + Escape(id), [], null, true, true,
			cancellationToken);

	private async ValueTask<TItem[]> GetPagesAsync<TItem>(string firstPath,
		int maximum, CancellationToken cancellationToken)
	{
		if (maximum is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var result = new List<TItem>();
		string cursor = null;
		var visited = new HashSet<string>(StringComparer.Ordinal);
		while (result.Count < maximum)
		{
			var path = AddCursor(firstPath, cursor);
			if (!visited.Add(path))
				break;
			var response = await GetAsync<PaxosItemsResponse<TItem>>(path, true,
				cancellationToken);
			var items = response?.Items ?? [];
			result.AddRange(items.Take(maximum - result.Count));
			if (items.Length == 0 || response?.NextPageCursor.IsEmpty() != false)
				break;
			cursor = response.NextPageCursor;
		}
		return [.. result];
	}

	private ValueTask<TResponse> GetAsync<TResponse>(string path,
		bool isAuthenticationRequired, CancellationToken cancellationToken)
		=> SendAsync<TResponse>(HttpMethod.Get, path, [], null,
			isAuthenticationRequired, true, cancellationToken);

	private ValueTask<TResponse> PostAsync<TResponse, TRequest>(string path,
		TRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request,
			_settings));
		return SendAsync<TResponse>(HttpMethod.Post, path, body,
			"application/json", true, false, cancellationToken);
	}

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, byte[] body, string contentType,
		bool isAuthenticationRequired, bool isRetryAllowed,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		if (isAuthenticationRequired)
			EnsureAuthenticationAvailable();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 0; ; attempt++)
			{
				await EnforceRequestIntervalAsync(cancellationToken);
				if (isAuthenticationRequired)
					await EnsureTokenAsync(cancellationToken);
				using var request = new HttpRequestMessage(method, path);
				if (isAuthenticationRequired)
					request.Headers.Authorization = new("Bearer", _accessToken);
				if (body.Length > 0)
					request.Content = new ByteArrayContent(body)
					{
						Headers = { ContentType = new(contentType) },
					};

				HttpResponseMessage response;
				try
				{
					response = await _client.SendAsync(request,
						HttpCompletionOption.ResponseHeadersRead,
						cancellationToken);
				}
				catch (Exception error) when (isRetryAllowed && attempt < 3 &&
					!cancellationToken.IsCancellationRequested &&
					error is HttpRequestException or TaskCanceledException)
				{
					await DelayRetryAsync(attempt, null, cancellationToken);
					continue;
				}
				using (response)
				{
					_lastRequestTime = DateTime.UtcNow;
					var bytes = await ReadResponseAsync(response,
						cancellationToken);
					if (response.IsSuccessStatusCode)
					{
						if (bytes.Length == 0)
							return default;
						return JsonConvert.DeserializeObject<TResponse>(
							Encoding.UTF8.GetString(bytes), _settings);
					}
					if (response.StatusCode == HttpStatusCode.Unauthorized &&
						isAuthenticationRequired && attempt == 0)
					{
						_accessToken = null;
						_tokenExpiresAt = default;
						continue;
					}
					if (isRetryAllowed && attempt < 3 &&
						IsTransient(response.StatusCode))
					{
						await DelayRetryAsync(attempt,
							response.Headers.RetryAfter?.Delta, cancellationToken);
						continue;
					}
					throw CreateException(response.StatusCode, bytes);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private void EnsureAuthenticationAvailable()
	{
		if (!IsAuthenticationAvailable)
			throw new InvalidOperationException(
				"Paxos Client ID and Client Secret are required for this operation.");
		if (_scopes.IsEmpty())
			throw new InvalidOperationException(
				"At least one Paxos OAuth scope is required for private API access.");
	}

	private async ValueTask EnsureTokenAsync(
		CancellationToken cancellationToken)
	{
		if (!_accessToken.IsEmpty() && DateTime.UtcNow < _tokenExpiresAt)
			return;
		var tokenRequest = new PaxosTokenRequest
		{
			GrantType = PaxosOAuthGrantTypes.ClientCredentials,
			ClientId = _clientId,
			ClientSecret = _clientSecret,
			Scope = _scopes,
		};
		var form = "grant_type=" + FormEscape(
			PaxosEnumConverter<PaxosOAuthGrantTypes>.ToWire(
				tokenRequest.GrantType)) +
			"&client_id=" + FormEscape(tokenRequest.ClientId) +
			"&client_secret=" + FormEscape(tokenRequest.ClientSecret) +
			"&scope=" + FormEscape(tokenRequest.Scope);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			_oauthEndpoint)
		{
			Content = new StringContent(form, Encoding.UTF8,
				"application/x-www-form-urlencoded"),
		};
		using var response = await _client.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var bytes = await ReadResponseAsync(response, cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateException(response.StatusCode, bytes);
		var token = JsonConvert.DeserializeObject<PaxosTokenResponse>(
			Encoding.UTF8.GetString(bytes), _settings);
		if (token?.AccessToken.IsEmpty() != false)
			throw new InvalidDataException(
				"Paxos OAuth returned no access token.");
		_accessToken = token.AccessToken;
		_tokenExpiresAt = DateTime.UtcNow.AddSeconds(
			(token.ExpiresIn - 30).Max(1));
	}

	private async ValueTask EnforceRequestIntervalAsync(
		CancellationToken cancellationToken)
	{
		var remaining = _minimumRequestInterval -
			(DateTime.UtcNow - _lastRequestTime);
		if (remaining > TimeSpan.Zero)
			await Task.Delay(remaining, cancellationToken);
	}

	private static async ValueTask DelayRetryAsync(int attempt,
		TimeSpan? serverDelay, CancellationToken cancellationToken)
	{
		var delay = serverDelay ?? TimeSpan.FromMilliseconds(200 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		if (delay > TimeSpan.FromSeconds(10))
			delay = TimeSpan.FromSeconds(10);
		await Task.Delay(delay, cancellationToken);
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Paxos response is too large ({length} bytes).");
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (bytes.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Paxos response is too large ({bytes.Length} bytes).");
		return bytes;
	}

	private PaxosApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		PaxosErrorDetails error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<PaxosErrorDetails>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var detail = error?.Detail;
		if (detail.IsEmpty())
			detail = error?.Message;
		if (detail.IsEmpty())
			detail = error?.ErrorDescription;
		if (detail.IsEmpty())
			detail = error?.Title;
		var type = error?.Type;
		if (type.IsEmpty())
			type = error?.Error;
		var message = detail.IsEmpty()
			? $"Paxos API returned HTTP {(int)statusCode} ({statusCode})."
			: $"Paxos API returned HTTP {(int)statusCode}: {detail}";
		return new(statusCode, type, message);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 ||
			statusCode is HttpStatusCode.InternalServerError or
			HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
			HttpStatusCode.GatewayTimeout;

	private static Uri ValidateEndpoint(string endpoint, string parameterName,
		bool isApiRoot)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host.IsEmpty())
			throw new ArgumentException("A valid HTTPS endpoint is required.",
				parameterName);
		if (!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"Paxos endpoints cannot contain a query or fragment.",
				parameterName);
		if (!isApiRoot)
			return new Uri(uri.AbsoluteUri.TrimEnd('/'), UriKind.Absolute);
		if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/v2",
			StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				"The Paxos REST endpoint must end in /v2.", parameterName);
		return uri;
	}

	private static int ValidatePaging(int pageSize, int maximum,
		int maximumPageSize)
	{
		if (pageSize is < 1 || pageSize > maximumPageSize)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		if (maximum is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		return pageSize.Min(maximum);
	}

	private static string AddCursor(string path, string cursor)
		=> cursor.IsEmpty()
			? path
			: path + (path.Contains('?') ? "&" : "?") +
				"page_cursor=" + Escape(cursor);

	private static string Format(int value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private static string Escape(string value)
		=> value.ThrowIfEmpty(nameof(value)).DataEscape();

	private static string FormEscape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)))
			.Replace("%20", "+", StringComparison.Ordinal);

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_accessToken = null;
		_client.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
