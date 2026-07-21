namespace StockSharp.ZeroHash.Native;

sealed class ZeroHashApiException : InvalidOperationException
{
	public ZeroHashApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class ZeroHashRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(45);
	private readonly HttpClient _client;
	private readonly ZeroHashAuthenticator _authenticator;
	private readonly string _rootPath;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = { new StringEnumConverter() },
	};
	private DateTime _nextRequestTime;
	private bool _isDisposed;

	public ZeroHashRestClient(string endpoint,
		ZeroHashAuthenticator authenticator)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(
			nameof(authenticator));
		var address = NormalizeEndpoint(endpoint);
		_rootPath = address.AbsolutePath.TrimEnd('/');
		_client = new()
		{
			BaseAddress = address,
			Timeout = Timeout.InfiniteTimeSpan,
		};
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-ZeroHash/1.0");
		_client.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "ZeroHash_REST";

	public ValueTask<ZeroHashInstrumentPage> ListInstrumentsAsync(
		ZeroHashListInstrumentsRequest request,
		CancellationToken cancellationToken)
		=> PostAsync<ZeroHashInstrumentPage, ZeroHashListInstrumentsRequest>(
			"orders/v1/list_instruments", request, true, cancellationToken);

	public ValueTask<ZeroHashInsertOrderResponse> InsertOrderAsync(
		ZeroHashInsertOrderRequest request, CancellationToken cancellationToken)
		=> PostAsync<ZeroHashInsertOrderResponse, ZeroHashInsertOrderRequest>(
			"orders/v1/insert_order", request, false, cancellationToken);

	public ValueTask<ZeroHashEmptyResponse> CancelOrderAsync(
		ZeroHashCancelOrderRequest request, CancellationToken cancellationToken)
		=> PostAsync<ZeroHashEmptyResponse, ZeroHashCancelOrderRequest>(
			"orders/v1/cancel_order", request, false, cancellationToken);

	public ValueTask<ZeroHashSearchOrdersResponse> SearchOrdersAsync(
		ZeroHashSearchOrdersRequest request, CancellationToken cancellationToken)
		=> PostAsync<ZeroHashSearchOrdersResponse, ZeroHashSearchOrdersRequest>(
			"orders/v1/search_orders", request, true, cancellationToken);

	public ValueTask<ZeroHashOpenOrdersResponse> GetOpenOrdersAsync(
		ZeroHashOpenOrdersRequest request, CancellationToken cancellationToken)
		=> PostAsync<ZeroHashOpenOrdersResponse, ZeroHashOpenOrdersRequest>(
			"orders/v1/get_open_orders", request, true, cancellationToken);

	public ValueTask<ZeroHashSearchExecutionsResponse> SearchExecutionsAsync(
		ZeroHashSearchExecutionsRequest request,
		CancellationToken cancellationToken)
		=> PostAsync<ZeroHashSearchExecutionsResponse,
			ZeroHashSearchExecutionsRequest>("orders/v1/search_executions",
			request, true, cancellationToken);

	public ValueTask<ZeroHashBalanceResponse> GetBalancesAsync(
		ZeroHashBalanceRequest request, CancellationToken cancellationToken)
		=> PostAsync<ZeroHashBalanceResponse, ZeroHashBalanceRequest>(
			"orders/v1/list_account_balances", request, true,
			cancellationToken);

	public ValueTask ReadMarketStreamAsync(
		ZeroHashMarketSubscriptionRequest request,
		Func<ZeroHashMarketEnvelope, CancellationToken, ValueTask> handler,
		CancellationToken cancellationToken)
		=> ReadStreamAsync<ZeroHashMarketSubscriptionRequest,
			ZeroHashMarketEnvelope>("orders/v1/create_market_data_subscription",
			request, handler, cancellationToken);

	public ValueTask ReadOrderStreamAsync(
		ZeroHashOrderSubscriptionRequest request,
		Func<ZeroHashOrderEnvelope, CancellationToken, ValueTask> handler,
		CancellationToken cancellationToken)
		=> ReadStreamAsync<ZeroHashOrderSubscriptionRequest,
			ZeroHashOrderEnvelope>("orders/v1/create_order_subscription",
			request, handler, cancellationToken);

	private async ValueTask<TResponse> PostAsync<TResponse, TRequest>(string path,
		TRequest request, bool isRetryAllowed,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentNullException.ThrowIfNull(request);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		var body = JsonConvert.SerializeObject(request, _settings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitAsync(cancellationToken);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken);
			timeout.CancelAfter(_requestTimeout);
			using var message = CreateRequest(path, body);
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
					"Zero Hash returned malformed JSON for POST /" + path + ".",
					error);
			}
		}
	}

	private async ValueTask ReadStreamAsync<TRequest, TResponse>(string path,
		TRequest request,
		Func<TResponse, CancellationToken, ValueTask> handler,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(handler);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		var body = JsonConvert.SerializeObject(request, _settings);
		await WaitAsync(cancellationToken);
		using var message = CreateRequest(path, body);
		using var response = await _client.SendAsync(message,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var payload = await ReadPayloadAsync(response, cancellationToken);
			throw CreateException(response.StatusCode, payload);
		}
		await using var stream = await response.Content.ReadAsStreamAsync(
			cancellationToken);
		using var text = new StreamReader(stream, Encoding.UTF8, true, 8192,
			leaveOpen: false);
		using var reader = new JsonTextReader(text)
		{
			DateParseHandling = DateParseHandling.None,
			FloatParseHandling = FloatParseHandling.Decimal,
			SupportMultipleContent = true,
		};
		var serializer = JsonSerializer.Create(_settings);
		while (await reader.ReadAsync(cancellationToken))
		{
			if (reader.TokenType is JsonToken.Comment or JsonToken.None)
				continue;
			TResponse value;
			try
			{
				value = serializer.Deserialize<TResponse>(reader);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Zero Hash returned malformed streaming JSON for POST /" +
					path + ".", error);
			}
			if (value is not null)
				await handler(value, cancellationToken);
		}
	}

	private HttpRequestMessage CreateRequest(string path, string body)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, path)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
			Version = HttpVersion.Version20,
			VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
		};
		_authenticator.AddHeaders(request, _rootPath + "/" + path, body);
		return request;
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
				"Zero Hash response exceeds the maximum supported size.");
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
					"Zero Hash response exceeds the maximum supported size.");
			await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
		}
		return Encoding.UTF8.GetString(output.GetBuffer(), 0,
			checked((int)output.Length));
	}

	private ZeroHashApiException CreateException(HttpStatusCode statusCode,
		string payload)
	{
		ZeroHashApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<ZeroHashApiError>(payload,
				_settings);
		}
		catch (JsonException)
		{
		}
		var message = error?.GetMessage();
		if (message.IsEmpty())
			message = payload.IsEmpty()
				? $"Zero Hash returned HTTP {(int)statusCode} ({statusCode})."
				: payload;
		return new(statusCode, message);
	}

	private static Uri NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute,
			out var address) || address.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"Zero Hash REST endpoint must use HTTPS.", nameof(endpoint));
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
