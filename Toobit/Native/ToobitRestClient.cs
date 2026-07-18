namespace StockSharp.Toobit.Native;

sealed class ToobitApiException : InvalidOperationException
{
	public ToobitApiException(HttpStatusCode statusCode, int? code, string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
}

sealed class ToobitRestClient : BaseLogReceiver
{
	private const int _maxAttempts = 3;
	private const int _receiveWindow = 5000;
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly SecureString _key;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _hashSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private long _serverOffsetMilliseconds;

	public ToobitRestClient(string endpoint, SecureString key, SecureString secret)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		_endpoint = new Uri(endpoint.TrimEnd('/') + "/", UriKind.Absolute);
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_http.Timeout = TimeSpan.FromSeconds(30);
	}

	public override string Name => nameof(Toobit) + "_" + nameof(ToobitRestClient);

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask SynchronizeTimeAsync(CancellationToken cancellationToken)
	{
		var before = DateTime.UtcNow;
		var response = await SendAsync<ToobitServerTime>(HttpMethod.Get, "/api/v1/time",
			ToobitEmptyRequest.Instance, ToobitSecurityTypes.None, true, cancellationToken);
		var after = DateTime.UtcNow;
		var midpoint = before + TimeSpan.FromTicks((after - before).Ticks / 2);
		Interlocked.Exchange(ref _serverOffsetMilliseconds,
			response.ServerTime - midpoint.ToUnixMilliseconds());
	}

	public ValueTask<ToobitExchangeInfo> GetExchangeInfoAsync(CancellationToken cancellationToken)
		=> SendAsync<ToobitExchangeInfo>(HttpMethod.Get, "/api/v1/exchangeInfo",
			ToobitEmptyRequest.Instance, ToobitSecurityTypes.None, true, cancellationToken);

	public ValueTask<ToobitTicker[]> GetTickerAsync(bool isFutures, string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitTicker[]>(HttpMethod.Get,
			isFutures ? "/quote/v1/contract/ticker/24hr" : "/quote/v1/ticker/24hr",
			new ToobitSymbolRequest { Symbol = symbol }, ToobitSecurityTypes.None, true, cancellationToken);

	public ValueTask<ToobitOrderBook> GetDepthAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitOrderBook>(HttpMethod.Get, "/quote/v1/depth",
			new ToobitDepthRequest { Symbol = symbol, Limit = limit },
			ToobitSecurityTypes.None, true, cancellationToken);

	public ValueTask<ToobitPublicTrade[]> GetTradesAsync(string symbol, int limit,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitPublicTrade[]>(HttpMethod.Get, "/quote/v1/trades",
			new ToobitDepthRequest { Symbol = symbol, Limit = limit.Min(60) },
			ToobitSecurityTypes.None, true, cancellationToken);

	public ValueTask<ToobitCandle[]> GetCandlesAsync(string symbol, string interval,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
		=> SendAsync<ToobitCandle[]>(HttpMethod.Get, "/quote/v1/klines",
			new ToobitKlinesRequest
			{
				Symbol = symbol,
				Interval = interval,
				StartTime = from?.ToUnixMilliseconds(),
				EndTime = to?.ToUnixMilliseconds(),
				Limit = limit.Min(1000),
			}, ToobitSecurityTypes.None, true, cancellationToken);

	public ValueTask<ToobitOrder> RegisterSpotOrderAsync(ToobitSpotOrderRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitOrder>(HttpMethod.Post, "/api/v1/spot/order", request,
			ToobitSecurityTypes.Signed, false, cancellationToken);

	public ValueTask<ToobitOrder> RegisterFuturesOrderAsync(ToobitFuturesOrderRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitOrder>(HttpMethod.Post, "/api/v1/futures/order", request,
			ToobitSecurityTypes.Signed, false, cancellationToken);

	public ValueTask<ToobitOrder> CancelSpotOrderAsync(ToobitCancelSpotOrderRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitOrder>(HttpMethod.Delete, "/api/v1/spot/order", request,
			ToobitSecurityTypes.Signed, false, cancellationToken);

	public ValueTask<ToobitOrder> CancelFuturesOrderAsync(ToobitOrderRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitOrder>(HttpMethod.Delete, "/api/v1/futures/order", request,
			ToobitSecurityTypes.Signed, false, cancellationToken);

	public ValueTask<ToobitOrder[]> GetOpenOrdersAsync(bool isFutures, string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitOrder[]>(HttpMethod.Get,
			isFutures ? "/api/v1/futures/openOrders" : "/api/v1/spot/openOrders",
			new ToobitOpenOrdersRequest { Symbol = symbol, Limit = isFutures ? 1000 : null },
			ToobitSecurityTypes.Signed, true, cancellationToken);

	public ValueTask<ToobitOrder[]> GetOrderHistoryAsync(bool isFutures, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
		=> SendAsync<ToobitOrder[]>(HttpMethod.Get,
			isFutures ? "/api/v1/futures/historyOrders" : "/api/v1/spot/tradeOrders",
			new ToobitOrderHistoryRequest
			{
				Symbol = symbol,
				StartTime = from?.ToUnixMilliseconds(),
				EndTime = to?.ToUnixMilliseconds(),
				Limit = limit.Min(1000),
			}, ToobitSecurityTypes.Signed, true, cancellationToken);

	public ValueTask<ToobitUserTrade[]> GetUserTradesAsync(bool isFutures, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
		=> SendAsync<ToobitUserTrade[]>(HttpMethod.Get,
			isFutures ? "/api/v1/futures/userTrades" : "/api/v1/account/trades",
			new ToobitUserTradesRequest
			{
				Symbol = symbol,
				StartTime = from?.ToUnixMilliseconds(),
				EndTime = to?.ToUnixMilliseconds(),
				Limit = limit.Min(1000),
			}, ToobitSecurityTypes.Signed, true, cancellationToken);

	public ValueTask<ToobitSpotAccount> GetSpotAccountAsync(CancellationToken cancellationToken)
		=> SendAsync<ToobitSpotAccount>(HttpMethod.Get, "/api/v1/account",
			ToobitEmptyRequest.Instance, ToobitSecurityTypes.Signed, true, cancellationToken);

	public ValueTask<ToobitFuturesBalance[]> GetFuturesBalancesAsync(CancellationToken cancellationToken)
		=> SendAsync<ToobitFuturesBalance[]>(HttpMethod.Get, "/api/v1/futures/balance",
			ToobitEmptyRequest.Instance, ToobitSecurityTypes.Signed, true, cancellationToken);

	public ValueTask<ToobitPosition[]> GetPositionsAsync(string symbol, CancellationToken cancellationToken)
		=> SendAsync<ToobitPosition[]>(HttpMethod.Get, "/api/v1/futures/positions",
			new ToobitSymbolRequest { Symbol = symbol }, ToobitSecurityTypes.Signed, true, cancellationToken);

	public async ValueTask<string> CreateListenKeyAsync(bool isFutures,
		CancellationToken cancellationToken)
	{
		var response = await SendAsync<ToobitListenKey>(HttpMethod.Post,
			isFutures ? "/api/v1/listenKey" : "/api/v1/userDataStream",
			ToobitEmptyRequest.Instance, ToobitSecurityTypes.ApiKey, true, cancellationToken);

		return response.Value.ThrowIfEmpty(nameof(response.Value));
	}

	public ValueTask<ToobitEmptyResponse> KeepAliveListenKeyAsync(bool isFutures, string listenKey,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitEmptyResponse>(HttpMethod.Put,
			isFutures ? "/api/v1/listenKey" : "/api/v1/userDataStream",
			new ToobitListenKeyRequest { ListenKey = listenKey },
			ToobitSecurityTypes.ApiKey, true, cancellationToken);

	public ValueTask<ToobitEmptyResponse> DeleteListenKeyAsync(bool isFutures, string listenKey,
		CancellationToken cancellationToken)
		=> SendAsync<ToobitEmptyResponse>(HttpMethod.Delete,
			isFutures ? "/api/v1/listenKey" : "/api/v1/userDataStream",
			new ToobitListenKeyRequest { ListenKey = listenKey },
			ToobitSecurityTypes.ApiKey, true, cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method, string path,
		IToobitRequest requestData, ToobitSecurityTypes securityType, bool safe,
		CancellationToken cancellationToken)
	{
		if (securityType != ToobitSecurityTypes.None && _key.IsEmpty())
			throw new InvalidOperationException("Toobit API key is not specified.");
		if (securityType == ToobitSecurityTypes.Signed && _hasher is null)
			throw new InvalidOperationException("Toobit API secret is not specified.");

		for (var attempt = 1; ; attempt++)
		{
			var query = new ToobitQueryBuilder();
			requestData.Write(query);
			if (securityType != ToobitSecurityTypes.None)
			{
				query.Add("recvWindow", _receiveWindow);
				query.Add("timestamp", GetServerTimestamp());
			}

			if (securityType == ToobitSecurityTypes.Signed)
				query.Add("signature", Sign(query.ToString()));

			var queryString = query.ToString();
			var relative = path.TrimStart('/') + (queryString.IsEmpty() ? string.Empty : "?" + queryString);
			using var request = new HttpRequestMessage(method, new Uri(_endpoint, relative));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (securityType != ToobitSecurityTypes.None)
				request.Headers.TryAddWithoutValidation("X-BB-APIKEY", _key.UnSecure());

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (HttpRequestException error) when (safe && attempt < _maxAttempts)
			{
				this.AddWarningLog("Toobit {0} {1} transport error. Retrying safe request: {2}", method, path, error.Message);
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				var outcome = safe ? string.Empty : " Trading operation outcome is unknown; it was not retried.";
				throw new InvalidOperationException($"Toobit {method} {path} failed: {error.Message}.{outcome}", error);
			}

			using (response)
			{
				var payload = await response.Content.ReadAsStringAsync(cancellationToken);
				if (safe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500) && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}

				if (!response.IsSuccessStatusCode)
					throw CreateError(response.StatusCode, payload, method, path, safe);
				if (payload.IsEmpty())
					throw new InvalidDataException($"Toobit {method} {path} returned an empty response.");

				TResponse result;
				try
				{
					result = JsonConvert.DeserializeObject<TResponse>(payload, _jsonSettings);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException($"Toobit {method} {path} returned invalid JSON.", error);
				}

				if (result is null)
					throw new InvalidDataException($"Toobit {method} {path} returned no JSON value.");

				if (result is ToobitResponseStatus status && status.Code is int code && code is not 0 and not 200)
					throw new ToobitApiException(response.StatusCode, code,
						$"Toobit {method} {path} failed ({code}): {status.Message}");

				return result;
			}
		}
	}

	private long GetServerTimestamp()
		=> DateTime.UtcNow.ToUnixMilliseconds() + Interlocked.Read(ref _serverOffsetMilliseconds);

	private string Sign(string value)
	{
		using (_hashSync.EnterScope())
			return _hasher.ComputeHash(value.UTF8()).Digest().ToLowerInvariant();
	}

	private static Exception CreateError(HttpStatusCode statusCode, string payload,
		HttpMethod method, string path, bool safe)
	{
		ToobitApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<ToobitApiError>(payload);
		}
		catch (JsonException)
		{
		}

		var message = error?.Message.IsEmpty(error?.AlternateMessage).IsEmpty(payload);
		var outcome = safe || (int)statusCode < 500
			? string.Empty
			: " Trading operation outcome is unknown; do not submit it again without checking order state.";
		return new ToobitApiException(statusCode, error?.Code,
			$"Toobit {method} {path} failed ({(int)statusCode}, code {error?.Code?.ToString() ?? "n/a"}): {message}.{outcome}");
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.TryGetValues("X-Api-Limit-Reset-Timestamp", out var values) == true)
		{
			var reset = values.FirstOrDefault().ToLongId();
			if (reset is long resetUnix)
			{
				var delay = resetUnix.FromUnix(false) - DateTime.UtcNow;
				if (delay > TimeSpan.Zero)
					return delay.Min(TimeSpan.FromSeconds(60));
			}
		}

		if (response?.Headers.RetryAfter?.Delta is TimeSpan retryAfter && retryAfter > TimeSpan.Zero)
			return retryAfter.Min(TimeSpan.FromSeconds(60));

		return TimeSpan.FromSeconds(1 << attempt.Min(5));
	}
}
