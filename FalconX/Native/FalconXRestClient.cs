namespace StockSharp.FalconX.Native;

sealed class FalconXApiException : InvalidOperationException
{
	public FalconXApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class FalconXRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private readonly HttpClient _client;
	private readonly FalconXAuthenticator _authenticator;
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

	public FalconXRestClient(string endpoint,
		FalconXAuthenticator authenticator)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(
			nameof(authenticator));
		var address = NormalizeEndpoint(endpoint);
		_rootPath = address.AbsolutePath.TrimEnd('/');
		_client = new()
		{
			BaseAddress = address,
			Timeout = TimeSpan.FromSeconds(45),
		};
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-FalconX/1.0");
		_client.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "FalconX_REST";

	public ValueTask<FalconXAccountInfo> GetAccountInfoAsync(
		CancellationToken cancellationToken)
		=> SendAsync<FalconXAccountInfo>(HttpMethod.Get, "v1/account_info", null,
			true, cancellationToken);

	public ValueTask<FalconXTokenPair[]> GetPairsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<FalconXTokenPair[]>(HttpMethod.Get, "v1/pairs", null, true,
			cancellationToken);

	public ValueTask<FalconXPortfolioBalance[]> GetPortfolioBalancesAsync(
		CancellationToken cancellationToken)
		=> SendAsync<FalconXPortfolioBalance[]>(HttpMethod.Get,
			"v1/portfolio_balance_details", null, true, cancellationToken);

	public ValueTask<FalconXRestOrder> PlaceOrderAsync(
		FalconXRestOrderRequest request, CancellationToken cancellationToken)
		=> SendAsync<FalconXRestOrder, FalconXRestOrderRequest>(HttpMethod.Post,
			"v3/order", request ?? throw new ArgumentNullException(nameof(request)),
			false, cancellationToken);

	public ValueTask<FalconXRestOrder> GetOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<FalconXRestOrder>(HttpMethod.Get, "v1/orders/" +
			Escape(orderId.ThrowIfEmpty(nameof(orderId))), null, true,
			cancellationToken);

	public async ValueTask<FalconXRestOrder> GetOrderOrQuoteAsync(string orderId,
		CancellationToken cancellationToken)
	{
		orderId = orderId.ThrowIfEmpty(nameof(orderId));
		try
		{
			return await GetOrderAsync(orderId, cancellationToken);
		}
		catch (FalconXApiException error) when (
			error.StatusCode == HttpStatusCode.NotFound)
		{
			return await SendAsync<FalconXRestOrder>(HttpMethod.Get, "v1/quotes/" +
				Escape(orderId), null, true, cancellationToken);
		}
	}

	public async ValueTask<FalconXRestOrder[]> GetOrdersAsync(DateTime from,
		DateTime to, int limit, CancellationToken cancellationToken)
	{
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from));
		if (to - from > TimeSpan.FromDays(31))
			throw new ArgumentOutOfRangeException(nameof(from),
				"FalconX order-history ranges cannot exceed 31 days.");
		if (limit is < 1 or > 100)
			throw new ArgumentOutOfRangeException(nameof(limit));
		var orders = new List<FalconXRestOrder>();
		foreach (var status in new[]
		{
			FalconXOrderQueryStatuses.Open,
			FalconXOrderQueryStatuses.Success,
			FalconXOrderQueryStatuses.Failure,
		})
		{
			var path = "v1/orders?t_start=" + Escape(from.ToFalconXTime()) +
				"&t_end=" + Escape(to.ToFalconXTime()) + "&status=" +
				status.ToString().ToLowerInvariant() + "&limit=" +
				limit.ToString(CultureInfo.InvariantCulture) + "&order=desc";
			orders.AddRange(await SendAsync<FalconXRestOrder[]>(HttpMethod.Get,
				path, null, true, cancellationToken) ?? []);
		}
		return [.. orders
			.Where(static order => order is not null &&
				!order.NativeId.IsEmpty())
			.GroupBy(static order => order.NativeId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(order =>
				GetOrderTime(order))
				.First())
			.OrderByDescending(static order => GetOrderTime(order))
			.Take(limit)
			.OrderBy(static order => GetOrderTime(order))];
	}

	private ValueTask<TResponse> SendAsync<TResponse, TRequest>(
		HttpMethod method, string path, TRequest request, bool isRetryAllowed,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(method, path,
			JsonConvert.SerializeObject(request, _settings), isRetryAllowed,
			cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, string body, bool isRetryAllowed,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		for (var attempt = 0; ; attempt++)
		{
			await WaitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method, path);
			if (!body.IsEmpty())
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");
			_authenticator.AddRestHeaders(request,
				_rootPath + "/" + path, body);
			using var response = await _client.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (isRetryAllowed && attempt < 2 &&
				(response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			var payload = await ReadPayloadAsync(response, cancellationToken);
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
				throw new InvalidDataException("FalconX returned malformed JSON for " +
					method + " /" + path + ".", error);
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
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
		}
		finally
		{
			_gate.Release();
		}
	}

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
				"FalconX response exceeds the maximum supported size.");
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
					"FalconX response exceeds the maximum supported size.");
			await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
		}
		return Encoding.UTF8.GetString(output.GetBuffer(), 0,
			checked((int)output.Length));
	}

	private static DateTime GetOrderTime(FalconXRestOrder order)
		=> order.UpdateTime.TryParseFalconXTime() ??
			order.ExecuteTime.TryParseFalconXTime() ??
			order.CreateTime.TryParseFalconXTime() ??
			order.QuoteTime.TryParseFalconXTime() ?? DateTime.UnixEpoch;

	private FalconXApiException CreateException(HttpStatusCode statusCode,
		string payload)
	{
		FalconXApiErrorEnvelope error = null;
		try
		{
			error = JsonConvert.DeserializeObject<FalconXApiErrorEnvelope>(payload,
				_settings);
		}
		catch (JsonException)
		{
		}
		var message = error?.Error.GetMessage() ?? error?.Errors.GetMessage() ??
			(error?.Reason.IsEmpty() == false ? error.Reason : error?.Message);
		var code = error?.Code;
		if (!code.IsEmpty())
			message = message.IsEmpty() ? code : code + ": " + message;
		if (message.IsEmpty())
			message = payload.IsEmpty()
				? $"FalconX returned HTTP {(int)statusCode} ({statusCode})."
				: payload;
		return new(statusCode, message);
	}

	private static Uri NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute,
			out var address) ||
			address.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException("FalconX REST endpoint must use HTTPS.",
				nameof(endpoint));
		return address;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	protected override void DisposeManaged()
	{
		_isDisposed = true;
		_client.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
