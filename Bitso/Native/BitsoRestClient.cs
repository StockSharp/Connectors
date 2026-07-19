namespace StockSharp.Bitso.Native;

sealed class BitsoRestClient : BaseLogReceiver
{
	private enum RateBuckets
	{
		Public,
		Private,
		Cancellation,
	}

	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly byte[] _secret;
	private readonly SemaphoreSlim _publicRateSync = new(1, 1);
	private readonly SemaphoreSlim _privateRateSync = new(1, 1);
	private readonly Lock _nonceSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextPublicRequest;
	private DateTime _nextPrivateRequest;
	private long _nonceTimestamp;
	private int _nonceSalt = RandomNumberGenerator.GetInt32(100000, 1000000);

	public BitsoRestClient(string endpoint, SecureString key, SecureString secret)
	{
		var value = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out _endpoint) ||
			!_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException("Bitso endpoint must be an absolute HTTPS URI.",
				nameof(endpoint));

		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var secretValue = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != secretValue.IsEmpty())
			throw new ArgumentException(
				"Bitso API key and secret must be configured together.");
		_secret = secretValue.IsEmpty() ? null : Encoding.UTF8.GetBytes(secretValue);

		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Bitso-Connector/1.0");
	}

	public override string Name => "Bitso_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _secret is not null;

	protected override void DisposeManaged()
	{
		if (_secret is not null)
			CryptographicOperations.ZeroMemory(_secret);
		_publicRateSync.Dispose();
		_privateRateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<BitsoBook[]> GetBooksAsync(CancellationToken cancellationToken)
		=> SendAsync<BitsoBook[]>(HttpMethod.Get, "available_books/", null, null,
			false, RateBuckets.Public, true, cancellationToken);

	public ValueTask<BitsoTicker> GetTickerAsync(BitsoBookQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "book", request.Book.ThrowIfEmpty(nameof(request.Book)));
		return SendAsync<BitsoTicker>(HttpMethod.Get, "ticker/", query, null, false,
			RateBuckets.Public, true, cancellationToken);
	}

	public ValueTask<BitsoOrderBook> GetOrderBookAsync(BitsoOrderBookQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "book", request.Book.ThrowIfEmpty(nameof(request.Book)));
		Append(query, "aggregate", request.IsAggregate ? "true" : "false");
		return SendAsync<BitsoOrderBook>(HttpMethod.Get, "order_book/", query, null,
			false, RateBuckets.Public, true, cancellationToken);
	}

	public ValueTask<BitsoPublicTrade[]> GetTradesAsync(BitsoTradesQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "book", request.Book.ThrowIfEmpty(nameof(request.Book)));
		Append(query, "limit", request.Limit.Min(100).Max(1));
		Append(query, "marker", request.Marker);
		Append(query, "sort", request.IsAscending ? "asc" : "desc");
		return SendAsync<BitsoPublicTrade[]>(HttpMethod.Get, "trades/", query, null,
			false, RateBuckets.Public, true, cancellationToken);
	}

	public ValueTask<BitsoBalances> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendAsync<BitsoBalances>(HttpMethod.Get, "balance", null, null, true,
			RateBuckets.Private, true, cancellationToken);

	public ValueTask<BitsoOrderId> PlaceOrderAsync(BitsoPlaceOrderRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendAsync<BitsoOrderId>(HttpMethod.Post, "orders", null,
			Serialize(request), true, RateBuckets.Private, false, cancellationToken);
	}

	public ValueTask<BitsoOrderId> ModifyOrderAsync(string orderId,
		BitsoModifyOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var path = "../v4/orders/" + Uri.EscapeDataString(
			orderId.ThrowIfEmpty(nameof(orderId)));
		return SendAsync<BitsoOrderId>(HttpMethod.Patch, path, null,
			Serialize(request), true, RateBuckets.Private, false, cancellationToken);
	}

	public ValueTask<BitsoOrder[]> GetOpenOrdersAsync(BitsoOpenOrdersQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "book", request.Book);
		Append(query, "currency", request.Currency);
		Append(query, "limit", request.Limit.Min(500).Max(1));
		return SendAsync<BitsoOrder[]>(HttpMethod.Get, "open_orders", query, null,
			true, RateBuckets.Private, true, cancellationToken);
	}

	public ValueTask<BitsoOrder[]> GetOrdersAsync(IEnumerable<string> orderIds,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(orderIds);
		var ids = orderIds.Where(static id => !id.IsEmpty()).Distinct(
			StringComparer.OrdinalIgnoreCase).ToArray();
		if (ids.Length == 0)
			return new ValueTask<BitsoOrder[]>([]);
		var query = new StringBuilder();
		Append(query, "oids", ids.JoinComma());
		return SendAsync<BitsoOrder[]>(HttpMethod.Get, "orders", query, null, true,
			RateBuckets.Private, true, cancellationToken);
	}

	public ValueTask<BitsoUserTrade[]> GetUserTradesAsync(BitsoUserTradesQuery request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var query = new StringBuilder();
		Append(query, "book", request.Book);
		Append(query, "limit", request.Limit.Min(100).Max(1));
		Append(query, "marker", request.Marker);
		Append(query, "sort", request.IsAscending ? "asc" : "desc");
		return SendAsync<BitsoUserTrade[]>(HttpMethod.Get, "user_trades", query, null,
			true, RateBuckets.Private, true, cancellationToken);
	}

	public ValueTask<BitsoUserTrade[]> GetOrderTradesAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<BitsoUserTrade[]>(HttpMethod.Get,
			"order_trades/" + Uri.EscapeDataString(
				orderId.ThrowIfEmpty(nameof(orderId))), null, null, true,
			RateBuckets.Private, true, cancellationToken);

	public ValueTask<string[]> CancelOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<string[]>(HttpMethod.Delete,
			"orders/" + Uri.EscapeDataString(orderId.ThrowIfEmpty(nameof(orderId))),
			null, null, true, RateBuckets.Cancellation, false, cancellationToken);

	public ValueTask<string[]> CancelOrdersAsync(IEnumerable<string> orderIds,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(orderIds);
		var ids = orderIds.Where(static id => !id.IsEmpty()).Distinct(
			StringComparer.OrdinalIgnoreCase).ToArray();
		if (ids.Length == 0)
			return new ValueTask<string[]>([]);
		var query = new StringBuilder();
		Append(query, "oids", ids.JoinComma());
		return SendAsync<string[]>(HttpMethod.Delete, "orders", query, null, true,
			RateBuckets.Cancellation, false, cancellationToken);
	}

	private string Serialize<TRequest>(TRequest request)
		=> JsonConvert.SerializeObject(request, _jsonSettings);

	private async ValueTask<TPayload> SendAsync<TPayload>(HttpMethod method,
		string path, StringBuilder query, string body, bool isPrivate,
		RateBuckets bucket, bool isRetrySafe, CancellationToken cancellationToken)
	{
		if (isPrivate)
			EnsureCredentials();
		var uri = CreateUri(path, query?.ToString());
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(bucket, cancellationToken);
			using var request = new HttpRequestMessage(method, uri);
			if (body is not null)
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");
			if (isPrivate)
				request.Headers.TryAddWithoutValidation("Authorization",
					CreateAuthorization(method, uri, body));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TPayload>(responseBody);
			if (!isRetrySafe || attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, responseBody);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private string CreateAuthorization(HttpMethod method, Uri uri, string body)
	{
		var nonce = CreateNonce();
		var payload = nonce + method.Method.ToUpperInvariant() + uri.PathAndQuery +
			(body ?? string.Empty);
		using var hmac = new HMACSHA256(_secret);
		var signature = Convert.ToHexString(
			hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
		return $"Bitso {_apiKey}:{nonce}:{signature}";
	}

	private string CreateNonce()
	{
		using (_nonceSync.EnterScope())
		{
			var timestamp = DateTime.UtcNow.ToMilliseconds().Max(_nonceTimestamp);
			if (timestamp != _nonceTimestamp)
			{
				_nonceTimestamp = timestamp;
				_nonceSalt = RandomNumberGenerator.GetInt32(100000, 1000000);
			}
			else if (++_nonceSalt > 999999)
			{
				_nonceTimestamp++;
				_nonceSalt = RandomNumberGenerator.GetInt32(100000, 1000000);
			}
			return _nonceTimestamp.ToString(CultureInfo.InvariantCulture) +
				_nonceSalt.ToString("D6", CultureInfo.InvariantCulture);
		}
	}

	private Uri CreateUri(string path, string query)
	{
		var uri = new Uri(_endpoint, path.ThrowIfEmpty(nameof(path)));
		if (query.IsEmpty())
			return uri;
		return new UriBuilder(uri) { Query = query }.Uri;
	}

	private TPayload Deserialize<TPayload>(string body)
	{
		if (body.IsEmpty())
			throw new InvalidDataException("Bitso returned an empty response.");
		try
		{
			var response = JsonConvert.DeserializeObject<BitsoResponse<TPayload>>(
				body, _jsonSettings) ?? throw new InvalidDataException(
					"Bitso returned an empty JSON value.");
			if (response.IsSuccess == false)
				throw CreateApiError(response.Error);
			if (response.Payload is null)
				throw new InvalidDataException("Bitso response has no payload.");
			return response.Payload;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bitso returned an unexpected response shape.", error);
		}
	}

	private async ValueTask WaitRateLimitAsync(RateBuckets bucket,
		CancellationToken cancellationToken)
	{
		if (bucket == RateBuckets.Cancellation)
			return;
		var sync = bucket == RateBuckets.Public ? _publicRateSync : _privateRateSync;
		await sync.WaitAsync(cancellationToken);
		try
		{
			var next = bucket == RateBuckets.Public
				? _nextPublicRequest
				: _nextPrivateRequest;
			var delay = next - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			var nextTime = DateTime.UtcNow + (bucket == RateBuckets.Public
				? TimeSpan.FromMilliseconds(1005)
				: TimeSpan.FromMilliseconds(205));
			if (bucket == RateBuckets.Public)
				_nextPublicRequest = nextTime;
			else
				_nextPrivateRequest = nextTime;
		}
		finally
		{
			sync.Release();
		}
	}

	private static void Append(StringBuilder query, string name, string value)
	{
		if (value.IsEmpty())
			return;
		if (query.Length > 0)
			query.Append('&');
		query.Append(name).Append('=').Append(Uri.EscapeDataString(value));
	}

	private static void Append(StringBuilder query, string name, int value)
	{
		if (value > 0)
			Append(query, name, value.ToString(CultureInfo.InvariantCulture));
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			(response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
			(response.StatusCode == (HttpStatusCode)429
				? TimeSpan.FromMinutes(1)
				: TimeSpan.FromMilliseconds(500 * (1 << attempt)));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		await Task.Delay(delay, cancellationToken);
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		var details = body?.Trim();
		try
		{
			var response = JsonConvert.DeserializeObject<BitsoResponse<string>>(
				body ?? string.Empty, _jsonSettings);
			if (response?.Error is not null)
				details = FormatError(response.Error).IsEmpty(details);
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"Bitso HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}

	private static Exception CreateApiError(BitsoError error)
		=> new InvalidOperationException("Bitso API error: " + FormatError(error));

	private static string FormatError(BitsoError error)
		=> error is null
			? "unknown error"
			: new[] { error.Code, error.Message }
				.Where(static value => !value.IsEmpty()).Join(": ")
				.IsEmpty("unknown error");

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bitso API key and secret are required for private operations.");
	}
}
