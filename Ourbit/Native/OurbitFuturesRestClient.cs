namespace StockSharp.Ourbit.Native;

sealed class OurbitFuturesRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _signSync = new();
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public OurbitFuturesRestClient(string endpoint, SecureString key, SecureString secret)
	{
		_endpoint = new Uri(endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/", UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Ourbit-Connector/1.0");
	}

	public override string Name => nameof(Ourbit) + "_FuturesRest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _hasher is not null;

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<OurbitFuturesProduct[]> GetProductsAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesProduct[]>("contract/detailV2",
			new OurbitFuturesDetailRequest(), false, cancellationToken);

	public ValueTask<OurbitFuturesTicker[]> GetTickersAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesTicker[]>("contract/ticker", new OurbitEmptyRequest(),
			false, cancellationToken);

	public ValueTask<OurbitFuturesDepth> GetDepthAsync(string symbol, string step,
		CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesDepth>($"contract/depth_step/{EscapePath(symbol)}",
			new OurbitFuturesDepthRequest { Step = step }, false, cancellationToken);

	public ValueTask<OurbitFuturesTrade[]> GetTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesTrade[]>($"contract/deals/{EscapePath(symbol)}",
			new OurbitEmptyRequest(), false, cancellationToken);

	public ValueTask<OurbitFuturesCandles> GetCandlesAsync(string symbol, string interval,
		long end, CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesCandles>($"contract/kline/{EscapePath(symbol)}/recent",
			new OurbitFuturesCandleRequest { Interval = interval, End = end }, false,
			cancellationToken);

	public ValueTask<OurbitFuturesBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesBalance[]>("private/account/assets",
			new OurbitEmptyRequest(), true, cancellationToken);

	public ValueTask<OurbitFuturesPosition[]> GetPositionsAsync(
		CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesPosition[]>("private/position/open_positions",
			new OurbitEmptyRequest(), true, cancellationToken);

	public ValueTask<OurbitFuturesOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesOrder[]>("private/order/list/open_orders" +
			(symbol.IsEmpty() ? string.Empty : "/" + EscapePath(symbol)),
			new OurbitEmptyRequest(), true, cancellationToken);

	public ValueTask<OurbitFuturesOrder[]> GetOrderHistoryAsync(OurbitFuturesHistoryRequest request,
		CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesOrder[]>("private/order/list/history_orders", request,
			true, cancellationToken);

	public ValueTask<OurbitFuturesFill[]> GetFillsAsync(OurbitFuturesHistoryRequest request,
		CancellationToken cancellationToken)
		=> SendGetAsync<OurbitFuturesFill[]>("private/order/list/order_deals", request,
			true, cancellationToken);

	public ValueTask<string> PlaceOrderAsync(OurbitFuturesOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<string, OurbitFuturesOrderRequest>("private/order/submit", request,
			cancellationToken);

	public ValueTask<string> CancelOrderAsync(OurbitFuturesCancelRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<string, OurbitFuturesCancelRequest>("private/order/cancel", request,
			cancellationToken);

	public ValueTask<string> CancelAllOrdersAsync(OurbitFuturesCancelAllRequest request,
		CancellationToken cancellationToken)
		=> SendPostAsync<string, OurbitFuturesCancelAllRequest>("private/order/cancel_all", request,
			cancellationToken);

	private async ValueTask<TData> SendGetAsync<TData>(string path, IOurbitParameters requestData,
		bool isSigned, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(requestData);
		if (isSigned)
			EnsureCredentials();
		var query = BuildQuery(requestData.GetParameters());

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, path.TrimStart('/') + (query.IsEmpty() ? string.Empty : "?" + query)));
			if (isSigned)
				ApplySignature(request, query);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TData>(body);
			if (attempt + 1 < _maximumReadAttempts && IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			throw CreateException(response.StatusCode, body);
		}
	}

	private async ValueTask<TData> SendPostAsync<TData, TRequest>(string path, TRequest requestData,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		ArgumentNullException.ThrowIfNull(requestData);
		EnsureCredentials();
		var body = JsonConvert.SerializeObject(requestData, _jsonSettings);
		await WaitRateLimitAsync(cancellationToken);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_endpoint, path.TrimStart('/')))
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		ApplySignature(request, body);
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw CreateException(response.StatusCode, responseBody);
		return Deserialize<TData>(responseBody);
	}

	private void ApplySignature(HttpRequestMessage request, string payload)
	{
		var requestTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			.ToString(CultureInfo.InvariantCulture);
		var signature = Sign(_apiKey + requestTime + payload);
		request.Headers.Add("ApiKey", _apiKey);
		request.Headers.Add("Request-Time", requestTime);
		request.Headers.Add("Signature", signature);
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException("Ourbit API key and secret are required for private futures operations.");
	}

	private string Sign(string value)
	{
		using (_signSync.EnterScope())
			return Convert.ToHexString(_hasher.ComputeHash(value.UTF8())).ToLowerInvariant();
	}

	private TData Deserialize<TData>(string body)
	{
		OurbitFuturesResponse<TData> response;
		try
		{
			response = JsonConvert.DeserializeObject<OurbitFuturesResponse<TData>>(body, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Ourbit futures API returned malformed JSON.", error);
		}
		if (response is null)
			throw new InvalidDataException("Ourbit futures API returned an empty response.");
		if (!response.IsSuccess || response.Code != 0)
			throw new InvalidOperationException(
				$"Ourbit futures API error {response.Code}: {response.Message}".Trim());
		return response.Data;
	}

	private static string BuildQuery(IEnumerable<OurbitParameter> parameters)
		=> (parameters ?? [])
			.Where(static parameter => !parameter.Name.IsEmpty() && parameter.Value is not null)
			.Select(static parameter =>
				$"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}")
			.Join("&");

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).ToUpperInvariant());

	private static Exception CreateException(HttpStatusCode statusCode, string body)
		=> new InvalidOperationException(
			$"Ourbit futures HTTP {(int)statusCode} ({statusCode}): {body}".Trim());

	private async ValueTask WaitRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(50);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));
		if (delay > TimeSpan.FromSeconds(10))
			delay = TimeSpan.FromSeconds(10);
		await Task.Delay(delay, cancellationToken);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}
