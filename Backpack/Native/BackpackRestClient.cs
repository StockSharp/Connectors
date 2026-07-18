namespace StockSharp.Backpack.Native;

sealed class BackpackRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private const long _receiveWindow = 5000;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly BackpackSigner _signer;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public BackpackRestClient(string endpoint, SecureString key, SecureString secret)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		var privateKey = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() != privateKey.IsEmpty())
			throw new ArgumentException(
				"Backpack Exchange public and private API keys must be configured together.");
		_signer = privateKey.IsEmpty() ? null : new(privateKey);
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Backpack-Connector/1.0");
	}

	public override string Name => nameof(Backpack) + "_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _signer is not null;

	public string ApiKey => _apiKey;

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public string CreateWebSocketSignature(long timestamp, long window)
		=> Sign("subscribe", BackpackEmptyParameters.Instance.GetParameters(), timestamp, window);

	public ValueTask<BackpackMarket[]> GetMarketsAsync(CancellationToken cancellationToken)
		=> SendGetAsync<BackpackMarket[]>("api/v1/markets",
			BackpackEmptyParameters.Instance, null, cancellationToken);

	public ValueTask<BackpackDepth> GetDepthAsync(BackpackDepthQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BackpackDepth>("api/v1/depth", query, null, cancellationToken);

	public ValueTask<BackpackTicker> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<BackpackTicker>("api/v1/ticker",
			new BackpackSymbolQuery { Symbol = symbol }, null, cancellationToken);

	public ValueTask<BackpackTrade[]> GetTradesAsync(BackpackTradesQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BackpackTrade[]>("api/v1/trades", query, null, cancellationToken);

	public ValueTask<BackpackTrade[]> GetHistoricalTradesAsync(
		BackpackHistoricalTradesQuery query, CancellationToken cancellationToken)
		=> SendGetAsync<BackpackTrade[]>("api/v1/trades/history", query, null,
			cancellationToken);

	public ValueTask<BackpackKline[]> GetKlinesAsync(BackpackKlinesQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BackpackKline[]>("api/v1/klines", query, null, cancellationToken);

	public ValueTask<BackpackMarkPrice[]> GetMarkPricesAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<BackpackMarkPrice[]>("api/v1/markPrices",
			new BackpackSymbolQuery { Symbol = symbol }, null, cancellationToken);

	public ValueTask<BackpackBalances> GetBalancesAsync(CancellationToken cancellationToken)
		=> SendGetAsync<BackpackBalances>("api/v1/capital",
			BackpackEmptyParameters.Instance, "balanceQuery", cancellationToken);

	public ValueTask<BackpackPosition[]> GetPositionsAsync(BackpackPositionsQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BackpackPosition[]>("api/v1/position", query, "positionQuery",
			cancellationToken);

	public ValueTask<BackpackOrder[]> GetOrdersAsync(BackpackOrdersQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BackpackOrder[]>("api/v1/orders", query, "orderQueryAll",
			cancellationToken);

	public ValueTask<BackpackOrder[]> GetOrderHistoryAsync(BackpackOrderHistoryQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BackpackOrder[]>("wapi/v1/history/orders", query,
			"orderHistoryQueryAll", cancellationToken);

	public ValueTask<BackpackFill[]> GetFillsAsync(BackpackFillHistoryQuery query,
		CancellationToken cancellationToken)
		=> SendGetAsync<BackpackFill[]>("wapi/v1/history/fills", query,
			"fillHistoryQueryAll", cancellationToken);

	public ValueTask<BackpackOrder> PlaceOrderAsync(BackpackOrderRequest request,
		CancellationToken cancellationToken)
		=> SendBodyAsync<BackpackOrder, BackpackOrderRequest>(HttpMethod.Post,
			"api/v1/order", request, "orderExecute", cancellationToken);

	public ValueTask<BackpackOrder> CancelOrderAsync(BackpackCancelOrderRequest request,
		CancellationToken cancellationToken)
		=> SendBodyAsync<BackpackOrder, BackpackCancelOrderRequest>(HttpMethod.Delete,
			"api/v1/order", request, "orderCancel", cancellationToken);

	public ValueTask<BackpackOrder[]> CancelOrdersAsync(BackpackCancelOrdersRequest request,
		CancellationToken cancellationToken)
		=> SendBodyAsync<BackpackOrder[], BackpackCancelOrdersRequest>(HttpMethod.Delete,
			"api/v1/orders", request, "orderCancelAll", cancellationToken);

	private async ValueTask<TResponse> SendGetAsync<TResponse>(string path,
		IBackpackParameters parameters, string instruction,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		if (!instruction.IsEmpty())
			EnsureCredentials();
		var values = parameters.GetParameters();
		var query = BuildQuery(values);
		var target = path.TrimStart('/') + (query.IsEmpty() ? string.Empty : "?" + query);

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, target));
			if (!instruction.IsEmpty())
				AddAuthentication(request, instruction, values);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return response.StatusCode == HttpStatusCode.NoContent
					? default
					: Deserialize<TResponse>(body);
			if (attempt + 1 >= _maximumReadAttempts || !IsTransient(response.StatusCode))
				throw CreateHttpError(response.StatusCode, body);
			await DelayRetryAsync(response, attempt, cancellationToken);
		}
	}

	private async ValueTask<TResponse> SendBodyAsync<TResponse, TRequest>(HttpMethod method,
		string path, TRequest body, string instruction, CancellationToken cancellationToken)
		where TRequest : IBackpackParameters
	{
		ArgumentNullException.ThrowIfNull(body);
		EnsureCredentials();
		var values = body.GetParameters();
		var json = JsonConvert.SerializeObject(body, _jsonSettings);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(method,
			new Uri(_endpoint, path.TrimStart('/')))
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};
		AddAuthentication(request, instruction, values);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, responseBody);
		if (response.StatusCode == HttpStatusCode.NoContent || responseBody.IsEmpty())
			return default;
		return Deserialize<TResponse>(responseBody);
	}

	private void AddAuthentication(HttpRequestMessage request, string instruction,
		BackpackParameter[] parameters)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
		request.Headers.TryAddWithoutValidation("X-Timestamp",
			timestamp.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation("X-Window",
			_receiveWindow.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation("X-Signature",
			Sign(instruction, parameters, timestamp, _receiveWindow));
	}

	private string Sign(string instruction, BackpackParameter[] parameters,
		long timestamp, long window)
	{
		EnsureCredentials();
		var payload = new StringBuilder("instruction=").Append(instruction);
		foreach (var parameter in parameters
			.Where(static value => !value.Name.IsEmpty() && value.Value is not null)
			.OrderBy(static value => value.Name, StringComparer.Ordinal))
			payload.Append('&').Append(parameter.Name).Append('=').Append(parameter.Value);
		payload.Append("&timestamp=").Append(timestamp.ToString(CultureInfo.InvariantCulture))
			.Append("&window=").Append(window.ToString(CultureInfo.InvariantCulture));
		return _signer.Sign(payload.ToString());
	}

	private static string BuildQuery(IEnumerable<BackpackParameter> parameters)
		=> parameters
			.Where(static value => !value.Name.IsEmpty() && value.Value is not null)
			.OrderBy(static value => value.Name, StringComparer.Ordinal)
			.Select(static value => Uri.EscapeDataString(value.Name) + "=" +
				Uri.EscapeDataString(value.Value))
			.Join("&");

	private TResponse Deserialize<TResponse>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException(
					"Backpack Exchange returned an empty response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Backpack Exchange returned an unexpected response shape.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Backpack Exchange public and private API keys are required for private operations.");
	}

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(100);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 || (int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private Exception CreateHttpError(HttpStatusCode statusCode, string body)
	{
		string details;
		try
		{
			var error = JsonConvert.DeserializeObject<BackpackError>(body, _jsonSettings);
			details = error is null
				? body?.Trim()
				: $"{error.Code}: {error.Message}".Trim();
		}
		catch (JsonException)
		{
			details = body?.Trim();
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"Backpack Exchange HTTP {(int)statusCode} ({statusCode}): {details}".Trim(),
			null, statusCode);
	}
}
