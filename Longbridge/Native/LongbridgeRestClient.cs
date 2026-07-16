namespace StockSharp.Longbridge.Native;

sealed class LongbridgeRestClient : BaseLogReceiver
{
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
	private readonly SemaphoreSlim _tradeRateGate = new(1, 1);
	private readonly string _appKey;
	private readonly string _appSecret;
	private readonly string _accessToken;
	private readonly Uri _apiRoot;
	private DateTime _lastTradeRequest;

	public LongbridgeRestClient(string appKey, string appSecret, string accessToken, string apiUrl)
	{
		_appKey = appKey.ThrowIfEmpty(nameof(appKey)).Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase);
		_appSecret = appSecret;
		_accessToken = accessToken.ThrowIfEmpty(nameof(accessToken)).Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase);
		_apiRoot = new Uri(apiUrl.ThrowIfEmpty(nameof(apiUrl)).TrimEnd('/') + "/", UriKind.Absolute);
	}

	public Task<LongbridgeOtp> GetOtp(CancellationToken cancellationToken)
		=> Send<LongbridgeOtp>(HttpMethod.Get, "v2/socket/token", false, cancellationToken);

	public Task<LongbridgeSubmitOrderResponse> SubmitOrder(LongbridgeSubmitOrderRequest request, CancellationToken cancellationToken)
		=> Send<LongbridgeSubmitOrderResponse, LongbridgeSubmitOrderRequest>(HttpMethod.Post, "v1/trade/order", request, true, cancellationToken);

	public Task ReplaceOrder(LongbridgeReplaceOrderRequest request, CancellationToken cancellationToken)
		=> Send<LongbridgeEmpty, LongbridgeReplaceOrderRequest>(HttpMethod.Put, "v1/trade/order", request, true, cancellationToken);

	public Task CancelOrder(string orderId, CancellationToken cancellationToken)
		=> Send<LongbridgeEmpty>(HttpMethod.Delete, $"v1/trade/order?order_id={Escape(orderId)}", true, cancellationToken);

	public Task<LongbridgeOrders> GetTodayOrders(CancellationToken cancellationToken)
		=> Send<LongbridgeOrders>(HttpMethod.Get, "v1/trade/order/today", true, cancellationToken);

	public Task<LongbridgeOrders> GetHistoryOrders(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
	{
		var query = new List<string>();
		if (from != null)
			query.Add($"start_at={from.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}");
		if (to != null)
			query.Add($"end_at={to.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}");
		return Send<LongbridgeOrders>(HttpMethod.Get, "v1/trade/order/history" + ToQuery(query), true, cancellationToken);
	}

	public Task<LongbridgeExecutions> GetTodayExecutions(CancellationToken cancellationToken)
		=> Send<LongbridgeExecutions>(HttpMethod.Get, "v1/trade/execution/today", true, cancellationToken);

	public Task<LongbridgeExecutions> GetHistoryExecutions(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
	{
		var query = new List<string>();
		if (from != null)
			query.Add($"start_at={from.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}");
		if (to != null)
			query.Add($"end_at={to.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}");
		return Send<LongbridgeExecutions>(HttpMethod.Get, "v1/trade/execution/history" + ToQuery(query), true, cancellationToken);
	}

	public Task<LongbridgeAccountBalances> GetBalances(CancellationToken cancellationToken)
		=> Send<LongbridgeAccountBalances>(HttpMethod.Get, "v1/asset/account", true, cancellationToken);

	public Task<LongbridgeStockPositions> GetPositions(CancellationToken cancellationToken)
		=> Send<LongbridgeStockPositions>(HttpMethod.Get, "v1/asset/stock", true, cancellationToken);

	private Task<T> Send<T>(HttpMethod method, string relativePath, bool trading, CancellationToken cancellationToken)
		=> Send<T, LongbridgeEmpty>(method, relativePath, null, trading, cancellationToken);

	private async Task<TResponse> Send<TResponse, TRequest>(HttpMethod method, string relativePath, TRequest body, bool trading,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		if (trading)
			await ThrottleTrade(cancellationToken);
		var rateRetried = false;
		while (true)
		{
			var bodyBytes = body == null ? [] : Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body));
			using var request = new HttpRequestMessage(method, new Uri(_apiRoot, relativePath));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.TryAddWithoutValidation("accept-language", "en");
			request.Headers.TryAddWithoutValidation("x-api-key", _appKey);
			request.Headers.TryAddWithoutValidation("authorization", _appSecret.IsEmpty() ? $"Bearer {_accessToken}" : _accessToken);
			request.Headers.TryAddWithoutValidation("x-dc-region", IsUsCredential() ? "us" : "ap");
			if (bodyBytes.Length > 0)
				request.Content = new ByteArrayContent(bodyBytes) { Headers = { ContentType = new("application/json") { CharSet = "utf-8" } } };
			if (!_appSecret.IsEmpty())
				Sign(request, bodyBytes);
			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.StatusCode == HttpStatusCode.TooManyRequests && !rateRetried)
			{
				rateRetried = true;
				await Task.Delay(GetRetryDelay(response), cancellationToken);
				continue;
			}
			LongbridgeApiResponse<TResponse> envelope = null;
			try
			{
				envelope = JsonConvert.DeserializeObject<LongbridgeApiResponse<TResponse>>(content);
			}
			catch (JsonException) when (!response.IsSuccessStatusCode)
			{
			}
			if (!response.IsSuccessStatusCode || envelope?.Code != 0)
			{
				var message = envelope?.Message.IsEmpty(content);
				if (message?.Length > 1000)
					message = message[..1000];
				throw new HttpRequestException($"Longbridge API error {(int)response.StatusCode}" +
					(envelope == null ? string.Empty : $"/{envelope.Code.ToString(CultureInfo.InvariantCulture)}") +
					(message.IsEmpty() ? string.Empty : $": {message}"), null, response.StatusCode);
			}
			return envelope.Data;
		}
	}

	private async Task ThrottleTrade(CancellationToken cancellationToken)
	{
		await _tradeRateGate.WaitAsync(cancellationToken);
		try
		{
			var delay = TimeSpan.FromMilliseconds(20) - (DateTime.UtcNow - _lastTradeRequest);
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_lastTradeRequest = DateTime.UtcNow;
		}
		finally
		{
			_tradeRateGate.Release();
		}
	}

	private void Sign(HttpRequestMessage request, byte[] body)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
		request.Headers.TryAddWithoutValidation("x-timestamp", timestamp);
		const string signedHeaders = "authorization;x-api-key;x-timestamp";
		var signatureHeader = $"HMAC-SHA256 SignedHeaders={signedHeaders}";
		request.Headers.TryAddWithoutValidation("x-api-signature", signatureHeader);
		var headers = $"authorization:{_accessToken}\n" + $"x-api-key:{_appKey}\n" + $"x-timestamp:{timestamp}\n";
		var plain = $"{request.Method.Method.ToUpperInvariant()}|{request.RequestUri.AbsolutePath}|{request.RequestUri.Query.TrimStart('?')}|" +
			$"{headers}|{signedHeaders}|" + (body.Length == 0 ? string.Empty : Convert.ToHexString(SHA1.HashData(body)).ToLowerInvariant());
		var textToSign = "HMAC-SHA256|" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();
		var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_appSecret), Encoding.UTF8.GetBytes(textToSign))).ToLowerInvariant();
		request.Headers.Remove("x-api-signature");
		request.Headers.TryAddWithoutValidation("x-api-signature", $"{signatureHeader}, Signature={signature}");
	}

	private bool IsUsCredential()
		=> _appKey.StartsWith("us_", StringComparison.OrdinalIgnoreCase) ||
			_accessToken.StartsWith("us_", StringComparison.OrdinalIgnoreCase) ||
			_appSecret.StartsWith("us_", StringComparison.OrdinalIgnoreCase);

	private static TimeSpan GetRetryDelay(HttpResponseMessage response)
	{
		if (response.Headers.RetryAfter?.Delta is { } delta)
			return delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
		if (response.Headers.RetryAfter?.Date is { } date)
			return TimeSpan.FromSeconds(Math.Clamp((date - DateTimeOffset.UtcNow).TotalSeconds, 0.1, 30));
		return TimeSpan.FromSeconds(1);
	}

	private static string ToQuery(List<string> values)
		=> values.Count == 0 ? string.Empty : "?" + string.Join("&", values);

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_tradeRateGate.Dispose();
		base.DisposeManaged();
	}
}
