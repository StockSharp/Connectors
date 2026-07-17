namespace StockSharp.SnapTrade.Native;

sealed class SnapTradeClient : BaseLogReceiver
{
	private sealed class SortedContractResolver : DefaultContractResolver
	{
		protected override IList<JsonProperty> CreateProperties(Type type,
			MemberSerialization memberSerialization)
			=> [.. base.CreateProperties(type, memberSerialization)
				.OrderBy(property => property.PropertyName, StringComparer.Ordinal)];
	}

	private static readonly Uri _origin = new("https://api.snaptrade.com/api/v1/");
	private readonly string _clientId;
	private readonly string _consumerKey;
	private readonly string _userId;
	private readonly string _userSecret;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new SortedContractResolver(),
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};

	public SnapTradeClient(string clientId, string consumerKey, string userId,
		string userSecret, int maxAttempts)
	{
		_clientId = clientId.ThrowIfEmpty(nameof(clientId));
		_consumerKey = consumerKey.ThrowIfEmpty(nameof(consumerKey));
		if (userId.IsEmpty() != userSecret.IsEmpty())
			throw new ArgumentException("SnapTrade UserId and UserSecret must either both be set or both be empty.");
		_userId = userId;
		_userSecret = userSecret;
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.Timeout = TimeSpan.FromSeconds(60);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-SnapTrade");
	}

	public Task<SnapTradeAccount[]> GetAccounts(CancellationToken cancellationToken)
		=> Get<SnapTradeAccount[]>("accounts", [], cancellationToken);

	public Task<SnapTradeAccount> GetAccount(string accountId,
		CancellationToken cancellationToken)
		=> Get<SnapTradeAccount>($"accounts/{Escape(accountId)}", [], cancellationToken);

	public Task<SnapTradeBalance[]> GetBalances(string accountId,
		CancellationToken cancellationToken)
		=> Get<SnapTradeBalance[]>($"accounts/{Escape(accountId)}/balances", [], cancellationToken);

	public Task<SnapTradePositionResponse> GetPositions(string accountId,
		CancellationToken cancellationToken)
		=> Get<SnapTradePositionResponse>($"accounts/{Escape(accountId)}/positions/all", [],
			cancellationToken);

	public Task<SnapTradeOrder[]> GetOrders(string accountId, string state, int days,
		CancellationToken cancellationToken)
		=> Get<SnapTradeOrder[]>($"accounts/{Escape(accountId)}/orders",
			[new("state", state.IsEmpty("all")),
				new("days", Math.Clamp(days, 1, 90).ToString(CultureInfo.InvariantCulture))],
			cancellationToken);

	public Task<SnapTradeRecentOrders> GetRecentOrders(string accountId,
		CancellationToken cancellationToken)
		=> Get<SnapTradeRecentOrders>($"accounts/{Escape(accountId)}/recentOrders",
			[new("only_executed", "false")], cancellationToken);

	public Task<SnapTradeQuote[]> GetQuotes(string accountId, string[] symbols,
		CancellationToken cancellationToken)
		=> Get<SnapTradeQuote[]>($"accounts/{Escape(accountId)}/quotes",
			[new("symbols", string.Join(",", symbols)), new("use_ticker", "true")],
			cancellationToken);

	public Task<SnapTradeUniversalSymbol[]> SearchSymbols(string accountId, string substring,
		CancellationToken cancellationToken)
		=> Post<SnapTradeSymbolSearchRequest, SnapTradeUniversalSymbol[]>(
			$"accounts/{Escape(accountId)}/symbols",
			new() { Substring = substring }, true, cancellationToken);

	public Task<SnapTradeOrder> PlaceOrder(SnapTradePlaceOrderRequest request,
		CancellationToken cancellationToken)
		=> Post<SnapTradePlaceOrderRequest, SnapTradeOrder>("trade/place", request, false,
			cancellationToken);

	public Task<SnapTradeCancelOrderResponse> CancelOrder(string accountId,
		SnapTradeCancelOrderRequest request, CancellationToken cancellationToken)
		=> Post<SnapTradeCancelOrderRequest, SnapTradeCancelOrderResponse>(
			$"accounts/{Escape(accountId)}/trading/cancel", request, false, cancellationToken);

	public Task<SnapTradeOrder> ReplaceOrder(string accountId,
		SnapTradeReplaceOrderRequest request, CancellationToken cancellationToken)
		=> Post<SnapTradeReplaceOrderRequest, SnapTradeOrder>(
			$"accounts/{Escape(accountId)}/trading/replace", request, false, cancellationToken);

	private Task<TResponse> Get<TResponse>(string path, SnapTradeQueryParameter[] parameters,
		CancellationToken cancellationToken)
		where TResponse : class
		=> Send<SnapTradeNoContent, TResponse>(HttpMethod.Get, path, null, parameters, true,
			cancellationToken);

	private Task<TResponse> Post<TRequest, TResponse>(string path, TRequest request,
		bool isRetryable, CancellationToken cancellationToken)
		where TRequest : class
		where TResponse : class
		=> Send<TRequest, TResponse>(HttpMethod.Post, path, request, [], isRetryable,
			cancellationToken);

	private async Task<TResponse> Send<TRequest, TResponse>(HttpMethod method, string relativePath,
		TRequest requestBody, SnapTradeQueryParameter[] parameters, bool isRetryable,
		CancellationToken cancellationToken)
		where TRequest : class
		where TResponse : class
	{
		relativePath = relativePath.TrimStart('/');
		var path = $"/api/v1/{relativePath}";
		var body = requestBody == null ? null : JsonConvert.SerializeObject(requestBody, _jsonSettings);
		for (var attempt = 1; ; attempt++)
		{
			var query = BuildQuery(parameters);
			var signaturePayload = new SnapTradeSignaturePayload<TRequest>
			{
				Content = requestBody,
				Path = path,
				Query = query,
			};
			var canonical = JsonConvert.SerializeObject(signaturePayload, _jsonSettings);
			var signature = ComputeSignature(canonical);

			using var message = new HttpRequestMessage(method,
				new Uri(_origin, $"{relativePath}?{query}"));
			message.Headers.TryAddWithoutValidation("Signature", signature);
			if (body != null)
				message.Content = new StringContent(body, Encoding.UTF8, "application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (Exception error) when (isRetryable && attempt < _maxAttempts &&
				error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
				await Delay(attempt, null, cancellationToken);
				continue;
			}

			using (response)
			{
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.IsSuccessStatusCode)
				{
					if (content.IsEmpty())
						return null;
					return JsonConvert.DeserializeObject<TResponse>(content, _jsonSettings);
				}

				if (isRetryable && attempt < _maxAttempts && IsTransient(response.StatusCode))
				{
					await Delay(attempt, GetResetDelay(response), cancellationToken);
					continue;
				}

				throw CreateException(response, content);
			}
		}
	}

	private string BuildQuery(SnapTradeQueryParameter[] parameters)
	{
		var timestamp = ((long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds)
			.ToString(CultureInfo.InvariantCulture);
		var result = new StringBuilder()
			.Append("clientId=").Append(Encode(_clientId))
			.Append("&timestamp=").Append(timestamp);
		if (!_userId.IsEmpty())
		{
			result.Append("&userId=").Append(Encode(_userId))
				.Append("&userSecret=").Append(Encode(_userSecret));
		}
		foreach (var parameter in parameters ?? [])
		{
			if (parameter == null || parameter.Name.IsEmpty() || parameter.Value == null)
				continue;
			result.Append('&').Append(Encode(parameter.Name)).Append('=').Append(Encode(parameter.Value));
		}
		return result.ToString();
	}

	private string ComputeSignature(string payload)
	{
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_consumerKey));
		return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.RequestTimeout ||
			(int)statusCode == 429 || (int)statusCode >= 500;

	private static TimeSpan? GetResetDelay(HttpResponseMessage response)
	{
		foreach (var header in new[]
		{
			"X-RateLimit-Account-Reset",
			"X-RateLimit-Reset",
			"Retry-After",
		})
		{
			if (!response.Headers.TryGetValues(header, out var values))
				continue;
			var value = values.FirstOrDefault();
			if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
				out var seconds) && seconds >= 0)
				return TimeSpan.FromSeconds((double)Math.Min(seconds, 30));
		}
		return null;
	}

	private static Task Delay(int attempt, TimeSpan? requested,
		CancellationToken cancellationToken)
	{
		var seconds = requested?.TotalSeconds ?? Math.Min(8, Math.Pow(2, attempt - 1));
		seconds = Math.Min(30, Math.Max(0.25, seconds + Random.Shared.NextDouble()));
		return Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
	}

	private static Exception CreateException(HttpResponseMessage response, string content)
	{
		SnapTradeErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<SnapTradeErrorResponse>(content);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Detail.IsEmpty(error?.DefaultDetail).IsEmpty(response.ReasonPhrase);
		var code = error?.Code.IsEmpty(error?.DefaultCode);
		var requestId = response.Headers.TryGetValues("X-Request-Id", out var ids)
			? ids.FirstOrDefault() : null;
		var suffix = code.IsEmpty() ? string.Empty : $" Code: {code}.";
		if (!requestId.IsEmpty())
			suffix += $" Request ID: {requestId}.";
		return new InvalidOperationException(
			$"SnapTrade HTTP {(int)response.StatusCode}: {detail}.{suffix}");
	}

	private static string Escape(string value)
		=> Encode(value.ThrowIfEmpty(nameof(value)));

	private static string Encode(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
