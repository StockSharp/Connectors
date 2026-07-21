namespace StockSharp.BitGo.Native;

sealed class BitGoApiException : InvalidOperationException
{
	public BitGoApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class BitGoRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(45);
	private readonly HttpClient _client;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters =
		{
			new BitGoTimeInForceConverter(),
			new StringEnumConverter(),
		},
	};
	private DateTime _nextRequestTime;
	private bool _isDisposed;

	public BitGoRestClient(string endpoint, SecureString accessToken)
	{
		var token = accessToken.ThrowIfEmpty(nameof(accessToken)).UnSecure().Trim();
		if (token.IsEmpty())
			throw new ArgumentException("BitGo access token is empty.",
				nameof(accessToken));
		_client = new()
		{
			BaseAddress = NormalizeEndpoint(endpoint),
			Timeout = Timeout.InfiniteTimeSpan,
		};
		_client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", token);
		_client.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-BitGo/1.0");
	}

	public override string Name => "BitGo_REST";

	public async ValueTask<BitGoAccount[]> GetAccountsAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<BitGoDataResponse<BitGoAccount>>(
			HttpMethod.Get, "api/prime/trading/v1/accounts", true,
			cancellationToken))?.Data ?? [];

	public async ValueTask<BitGoProduct[]> GetProductsAsync(string accountId,
		CancellationToken cancellationToken)
		=> (await SendAsync<BitGoDataResponse<BitGoProduct>>(HttpMethod.Get,
			AccountPath(accountId, "products"), true,
			cancellationToken))?.Data ?? [];

	public async ValueTask<BitGoBalance[]> GetBalancesAsync(string accountId,
		bool isIncludeUnsettled, CancellationToken cancellationToken)
		=> (await SendAsync<BitGoDataResponse<BitGoBalance>>(HttpMethod.Get,
			AccountPath(accountId, "balances") +
			"?includeUnsettledInAvailable=" +
			(isIncludeUnsettled ? "true" : "false"), true,
			cancellationToken))?.Data ?? [];

	public ValueTask<BitGoOrder> PlaceOrderAsync(string accountId,
		BitGoOrderRequest request, CancellationToken cancellationToken)
		=> SendAsync<BitGoOrder, BitGoOrderRequest>(HttpMethod.Post,
			AccountPath(accountId, "orders"), request, false,
			cancellationToken);

	public ValueTask<BitGoOrder> GetOrderAsync(string accountId,
		string orderId, CancellationToken cancellationToken)
		=> SendAsync<BitGoOrder>(HttpMethod.Get,
			AccountPath(accountId, "orders/" + Encode(orderId)), true,
			cancellationToken);

	public async ValueTask<BitGoOrder[]> GetOrdersAsync(string accountId,
		BitGoOrderQuery query, CancellationToken cancellationToken)
		=> (await SendAsync<BitGoDataResponse<BitGoOrder>>(HttpMethod.Get,
			AccountPath(accountId, "orders") + BuildOrderQuery(query), true,
			cancellationToken))?.Data ?? [];

	public async ValueTask<BitGoTrade[]> GetTradesAsync(string accountId,
		BitGoTradeQuery query, CancellationToken cancellationToken)
		=> (await SendAsync<BitGoDataResponse<BitGoTrade>>(HttpMethod.Get,
			AccountPath(accountId, "trades") + BuildTradeQuery(query), true,
			cancellationToken))?.Data ?? [];

	public ValueTask<BitGoEmptyResponse> CancelOrderAsync(string accountId,
		string orderId, CancellationToken cancellationToken)
		=> SendAsync<BitGoEmptyResponse>(HttpMethod.Put,
			AccountPath(accountId, "orders/" + Encode(orderId) + "/cancel"),
			false, cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, bool isRetryAllowed,
		CancellationToken cancellationToken)
		=> await SendCoreAsync<TResponse>(method, path, null, isRetryAllowed,
			cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse, TRequest>(
		HttpMethod method, string path, TRequest request, bool isRetryAllowed,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		ArgumentNullException.ThrowIfNull(request);
		return await SendCoreAsync<TResponse>(method, path,
			JsonConvert.SerializeObject(request, _settings), isRetryAllowed,
			cancellationToken);
	}

	private async ValueTask<TResponse> SendCoreAsync<TResponse>(
		HttpMethod method, string path, string body, bool isRetryAllowed,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		for (var attempt = 0; ; attempt++)
		{
			await WaitAsync(cancellationToken);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken);
			timeout.CancelAfter(_requestTimeout);
			using var message = new HttpRequestMessage(method, path)
			{
				Version = HttpVersion.Version20,
				VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
			};
			if (body is not null)
				message.Content = new StringContent(body, Encoding.UTF8,
					"application/json");
			using var response = await _client.SendAsync(message,
				HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			if (isRetryAllowed && attempt < 2 && IsRetryable(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			var payload = await ReadPayloadAsync(response, timeout.Token);
			if (!response.IsSuccessStatusCode)
				throw CreateException(response.StatusCode, payload);
			if (payload.IsEmpty())
				return default;
			try
			{
				return JsonConvert.DeserializeObject<TResponse>(payload, _settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"BitGo returned malformed JSON for " + method + " /" + path +
					".", error);
			}
		}
	}

	private async ValueTask WaitAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
		}
		finally
		{
			_gate.Release();
		}
	}

	private static string AccountPath(string accountId, string suffix)
		=> "api/prime/trading/v1/accounts/" + Encode(accountId) + "/" +
			suffix.TrimStart('/');

	private static string BuildOrderQuery(BitGoOrderQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);
		var values = new List<string>
		{
			"offset=" + query.Offset.ToString(CultureInfo.InvariantCulture),
			"limit=" + query.Limit.ToString(CultureInfo.InvariantCulture),
		};
		if (!query.ClientOrderId.IsEmpty())
			values.Add("clientOrderId=" + Encode(query.ClientOrderId));
		if (query.From is DateTime from)
			values.Add("dateGte=" + Encode(from.ToBitGoTime()));
		if (query.To is DateTime to)
			values.Add("dateLt=" + Encode(to.ToBitGoTime()));
		if (query.Status is BitGoOrderStatuses status)
			values.Add("status=" + status.ToBitGoWire());
		if (query.FundingType is BitGoFundingTypes funding)
			values.Add("fundingType=" + funding.ToBitGoWire());
		return "?" + string.Join("&", values);
	}

	private static string BuildTradeQuery(BitGoTradeQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);
		var values = new List<string>
		{
			"offset=" + query.Offset.ToString(CultureInfo.InvariantCulture),
			"limit=" + query.Limit.ToString(CultureInfo.InvariantCulture),
		};
		if (!query.OrderId.IsEmpty())
			values.Add("orderId=" + Encode(query.OrderId));
		if (query.From is DateTime from)
			values.Add("dateGte=" + Encode(from.ToBitGoTime()));
		if (query.To is DateTime to)
			values.Add("dateLt=" + Encode(to.ToBitGoTime()));
		if (query.FundingType is BitGoFundingTypes funding)
			values.Add("fundingType=" + funding.ToBitGoWire());
		return "?" + string.Join("&", values);
	}

	private static string Encode(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static bool IsRetryable(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(500 * (attempt + 1));
		if (delay > TimeSpan.FromSeconds(10))
			delay = TimeSpan.FromSeconds(10);
		await Task.Delay(delay, cancellationToken);
	}

	private static async ValueTask<string> ReadPayloadAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				"BitGo response exceeds the maximum supported size.");
		await using var input = await response.Content.ReadAsStreamAsync(
			cancellationToken);
		using var output = new MemoryStream();
		var buffer = new byte[81920];
		for (;;)
		{
			var read = await input.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (output.Length + read > _maximumResponseLength)
				throw new InvalidDataException(
					"BitGo response exceeds the maximum supported size.");
			await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
		}
		return Encoding.UTF8.GetString(output.GetBuffer(), 0,
			checked((int)output.Length));
	}

	private BitGoApiException CreateException(HttpStatusCode statusCode,
		string payload)
	{
		BitGoApiError error = null;
		BitGoValidationApiError validation = null;
		try
		{
			error = JsonConvert.DeserializeObject<BitGoApiError>(payload,
				_settings);
		}
		catch (JsonException)
		{
			try
			{
				validation = JsonConvert.DeserializeObject<BitGoValidationApiError>(
					payload, _settings);
			}
			catch (JsonException)
			{
			}
		}
		var message = error?.GetMessage() ?? validation?.GetMessage();
		if (message.IsEmpty())
			message = payload.IsEmpty()
				? $"BitGo returned HTTP {(int)statusCode} ({statusCode})."
				: payload;
		return new(statusCode, message);
	}

	private static Uri NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute,
			out var address) ||
			(address.Scheme != Uri.UriSchemeHttps &&
				address.Scheme != Uri.UriSchemeHttp))
			throw new ArgumentException(
				"BitGo REST endpoint must use HTTP or HTTPS.", nameof(endpoint));
		return address;
	}

	protected override void DisposeManaged()
	{
		_isDisposed = true;
		_client.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
