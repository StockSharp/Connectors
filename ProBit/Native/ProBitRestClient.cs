namespace StockSharp.ProBit.Native;

readonly record struct ProBitParameter(string Name, string Value);

sealed class ProBitRestClient : BaseLogReceiver
{
	private const int _maxAttempts = 4;
	private readonly Uri _endpoint;
	private readonly Uri _authEndpoint;
	private readonly HttpClient _http;
	private readonly string _clientId;
	private readonly string _clientSecret;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly SemaphoreSlim _tokenSync = new(1, 1);
	private DateTime _nextRequestTime;
	private string _accessToken;
	private DateTime _accessTokenExpiry;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	public ProBitRestClient(string endpoint, string authEndpoint, SecureString key, SecureString secret)
	{
		_endpoint = new Uri(NormalizeEndpoint(endpoint), UriKind.Absolute);
		_authEndpoint = new Uri(NormalizeEndpoint(authEndpoint), UriKind.Absolute);
		_clientId = key.IsEmpty() ? null : key.UnSecure();
		_clientSecret = secret.IsEmpty() ? null : secret.UnSecure();
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-ProBit-Connector/1.0");
	}

	public override string Name => nameof(ProBit) + "_Rest";

	public bool IsCredentialsAvailable => !_clientId.IsEmpty() && !_clientSecret.IsEmpty();

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_tokenSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<ProBitMarket[]> GetMarketsAsync(CancellationToken cancellationToken)
		=> (await SendGetAsync<ProBitMarket[]>("/api/exchange/v1/market", [], false,
			cancellationToken)).Data ?? [];

	public async ValueTask<ProBitTicker[]> GetTickersAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendGetAsync<ProBitTicker[]>("/api/exchange/v1/ticker",
			[new("market_ids", symbol)], false, cancellationToken)).Data ?? [];

	public async ValueTask<ProBitBookLevel[]> GetOrderBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendGetAsync<ProBitBookLevel[]>("/api/exchange/v1/order_book",
			[new("market_id", symbol)], false, cancellationToken)).Data ?? [];

	public async ValueTask<ProBitTrade[]> GetTradesAsync(string symbol, DateTime? from,
		DateTime? to, int limit, CancellationToken cancellationToken)
		=> (await SendGetAsync<ProBitTrade[]>("/api/exchange/v1/trade",
			[
				new("market_id", symbol),
				new("start_time", from?.ToWireTime()),
				new("end_time", to?.ToWireTime()),
				new("limit", limit.Min(1000).Max(1).ToString(CultureInfo.InvariantCulture)),
			], false, cancellationToken)).Data ?? [];

	public async ValueTask<ProBitCandle[]> GetCandlesAsync(string symbol, TimeSpan timeFrame,
		DateTime from, DateTime to, int limit, CancellationToken cancellationToken)
		=> (await SendGetAsync<ProBitCandle[]>("/api/exchange/v1/candle",
			[
				new("market_ids", symbol),
				new("interval", timeFrame.ToProBitInterval()),
				new("start_time", from.ToWireTime()),
				new("end_time", to.ToWireTime()),
				new("sort", "asc"),
				new("limit", limit.Min(1000).Max(1).ToString(CultureInfo.InvariantCulture)),
			], false, cancellationToken)).Data ?? [];

	public async ValueTask<ProBitBalance[]> GetBalancesAsync(CancellationToken cancellationToken)
		=> (await SendGetAsync<ProBitBalance[]>("/api/exchange/v1/balance", [], true,
			cancellationToken)).Data ?? [];

	public async ValueTask<ProBitOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendGetAsync<ProBitOrder[]>("/api/exchange/v1/open_order",
			[new("market_id", symbol)], true, cancellationToken)).Data ?? [];

	public async ValueTask<ProBitOrder[]> GetOrdersAsync(string symbol, string orderId,
		string clientOrderId, CancellationToken cancellationToken)
		=> (await SendGetAsync<ProBitOrder[]>("/api/exchange/v1/order",
			[
				new("market_id", symbol),
				new("order_id", orderId),
				new("client_order_id", clientOrderId),
			], true, cancellationToken)).Data ?? [];

	public async ValueTask<ProBitOrder[]> GetOrderHistoryAsync(string symbol, DateTime? from,
		DateTime? to, int limit, CancellationToken cancellationToken)
	{
		var end = (to ?? DateTime.UtcNow).ToUniversalTime();
		var start = (from ?? end.AddYears(-1)).ToUniversalTime();
		return (await SendGetAsync<ProBitOrder[]>("/api/exchange/v1/order_history",
			[
				new("market_id", symbol),
				new("start_time", start.ToWireTime()),
				new("end_time", end.ToWireTime()),
				new("limit", limit.Min(1000).Max(1).ToString(CultureInfo.InvariantCulture)),
			], true, cancellationToken)).Data ?? [];
	}

	public async ValueTask<ProBitTrade[]> GetTradeHistoryAsync(string symbol, DateTime? from,
		DateTime? to, int limit, CancellationToken cancellationToken)
	{
		var end = (to ?? DateTime.UtcNow).ToUniversalTime();
		var start = (from ?? end.AddYears(-1)).ToUniversalTime();
		return (await SendGetAsync<ProBitTrade[]>("/api/exchange/v1/trade_history",
			[
				new("market_id", symbol),
				new("start_time", start.ToWireTime()),
				new("end_time", end.ToWireTime()),
				new("limit", limit.Min(1000).Max(1).ToString(CultureInfo.InvariantCulture)),
			], true, cancellationToken)).Data ?? [];
	}

	public async ValueTask<ProBitOrder> PlaceOrderAsync(ProBitOrderRequest request,
		CancellationToken cancellationToken)
		=> (await SendBodyAsync<ProBitOrder, ProBitOrderRequest>(HttpMethod.Post,
			"/api/exchange/v1/new_order", request, cancellationToken)).Data;

	public async ValueTask<ProBitOrder> CancelOrderAsync(ProBitCancelOrderRequest request,
		CancellationToken cancellationToken)
		=> (await SendBodyAsync<ProBitOrder, ProBitCancelOrderRequest>(HttpMethod.Post,
			"/api/exchange/v1/cancel_order", request, cancellationToken)).Data;

	public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken)
	{
		EnsureCredentials();
		if (!_accessToken.IsEmpty() && _accessTokenExpiry > DateTime.UtcNow.AddSeconds(30))
			return _accessToken;

		await _tokenSync.WaitAsync(cancellationToken);
		try
		{
			if (!_accessToken.IsEmpty() && _accessTokenExpiry > DateTime.UtcNow.AddSeconds(30))
				return _accessToken;

			var body = JsonConvert.SerializeObject(new ProBitTokenRequest(), _jsonSettings);
			for (var attempt = 1; ; attempt++)
			{
				await WaitForRateLimitAsync(cancellationToken);
				using var request = new HttpRequestMessage(HttpMethod.Post, _authEndpoint);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				var credentials = Convert.ToBase64String(
					Encoding.UTF8.GetBytes(_clientId + ":" + _clientSecret));
				request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
				request.Content = new StringContent(body, Encoding.UTF8, "application/json");

				HttpResponseMessage response;
				try
				{
					response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
						cancellationToken);
				}
				catch (HttpRequestException) when (attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
					continue;
				}

				using (response)
				{
					var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
					if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
						(int)response.StatusCode >= 500) && attempt < _maxAttempts)
					{
						await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
						continue;
					}
					if (!response.IsSuccessStatusCode)
						throw CreateHttpError(response.StatusCode, _authEndpoint.AbsolutePath,
							responseBody, true);

					var token = Deserialize<ProBitTokenResponse>(responseBody,
						_authEndpoint.AbsolutePath);
					if (token.AccessToken.IsEmpty())
						throw new InvalidOperationException($"ProBit OAuth failed: " +
							$"{token.Error}: {token.ErrorDescription}.");
					_accessToken = token.AccessToken;
					_accessTokenExpiry = DateTime.UtcNow.AddSeconds(token.ExpiresIn.Max(60));
					return _accessToken;
				}
			}
		}
		finally
		{
			_tokenSync.Release();
		}
	}

	private ValueTask<ProBitResponse<TData>> SendGetAsync<TData>(string path,
		ProBitParameter[] parameters, bool isPrivate, CancellationToken cancellationToken)
		where TData : class
		=> SendAsync<TData>(HttpMethod.Get, path, parameters, isPrivate, true, null,
			cancellationToken);

	private ValueTask<ProBitResponse<TData>> SendBodyAsync<TData, TRequest>(HttpMethod method,
		string path, TRequest request, CancellationToken cancellationToken)
		where TData : class
		where TRequest : class
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendAsync<TData>(method, path, [], true, false,
			JsonConvert.SerializeObject(request, _jsonSettings), cancellationToken);
	}

	private async ValueTask<ProBitResponse<TData>> SendAsync<TData>(HttpMethod method, string path,
		ProBitParameter[] parameters, bool isPrivate, bool isSafe, string body,
		CancellationToken cancellationToken)
		where TData : class
	{
		if (isPrivate)
			EnsureCredentials();
		var query = BuildQuery(parameters);
		var relative = path + (query.IsEmpty() ? string.Empty : "?" + query);

		for (var attempt = 1; ; attempt++)
		{
			await WaitForRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method, new Uri(_endpoint, relative));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (isPrivate)
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
					await GetAccessTokenAsync(cancellationToken));
			if (body is not null)
				request.Content = new StringContent(body, Encoding.UTF8, "application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (HttpRequestException error) when (isSafe && attempt < _maxAttempts)
			{
				this.AddWarningLog("ProBit {0} transport error. Retrying read request: {1}",
					relative, error.Message);
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				throw new InvalidOperationException(CreateTransportError(relative, isSafe,
					error.Message), error);
			}

			using (response)
			{
				var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
				if (isSafe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500) && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateHttpError(response.StatusCode, relative, responseBody, isSafe);

				var result = Deserialize<ProBitResponse<TData>>(responseBody, relative);
				if (!result.ErrorCode.IsEmpty() || !result.Error.IsEmpty())
					throw new InvalidOperationException($"ProBit {relative} failed " +
						$"({result.ErrorCode.IsEmpty(result.Error)}): {result.Message}.");
				return result;
			}
		}
	}

	private async ValueTask WaitForRateLimitAsync(CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(55);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"ProBit client ID and client secret are required for private requests.");
	}

	private static string BuildQuery(IEnumerable<ProBitParameter> parameters)
		=> parameters
			.Where(static parameter => !parameter.Value.IsEmpty())
			.Select(static parameter => Uri.EscapeDataString(parameter.Name) + "=" +
				Uri.EscapeDataString(parameter.Value))
			.Join("&");

	private T Deserialize<T>(string responseBody, string path)
		where T : class
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(responseBody, _jsonSettings)
				?? throw new InvalidDataException($"ProBit {path} returned no JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException($"ProBit {path} returned invalid JSON.", error);
		}
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is TimeSpan delay && delay > TimeSpan.Zero)
			return delay.Min(TimeSpan.FromSeconds(30));
		return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
	}

	private Exception CreateHttpError(HttpStatusCode status, string path, string body, bool isSafe)
	{
		ProBitErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<ProBitErrorResponse>(body, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var detail = error is null
			? body
			: $"{error.ErrorCode.IsEmpty(error.Error)}: " +
				$"{error.Message.IsEmpty(error.ErrorDescription)}";
		return new InvalidOperationException($"ProBit {path} returned HTTP {(int)status}: {detail}. " +
			(isSafe ? "The read request failed." :
			"The write was not retried; inspect exchange state before retrying."));
	}

	private static string CreateTransportError(string path, bool isSafe, string message)
		=> $"ProBit {path} transport error: {message}. " +
			(isSafe ? "The read request failed." :
			"The write may have reached ProBit; inspect exchange state before retrying.");

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
