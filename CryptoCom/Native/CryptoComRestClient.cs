namespace StockSharp.CryptoCom.Native;

sealed class CryptoComApiException : InvalidOperationException
{
	public CryptoComApiException(HttpStatusCode statusCode, int? code, string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
}

sealed class CryptoComRestClient : BaseLogReceiver
{
	private const int _maxAttempts = 3;
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly string _apiKey;
	private readonly CryptoComSigner _signer;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private long _nextRequestId;

	public CryptoComRestClient(string endpoint, SecureString key, SecureString secret)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		_endpoint = new Uri(endpoint.TrimEnd('/') + "/", UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_signer = secret.IsEmpty() ? null : new CryptoComSigner(secret);
		_http.Timeout = TimeSpan.FromSeconds(30);
	}

	public override string Name => nameof(CryptoCom) + "_" + nameof(CryptoComRestClient);

	protected override void DisposeManaged()
	{
		_signer?.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<CryptoComInstrument[]> GetInstrumentsAsync(CancellationToken cancellationToken)
		=> (await SendPublicAsync<CryptoComDataResult<CryptoComInstrument>>("public/get-instruments",
			CryptoComEmptyParams.Instance, cancellationToken)).Data ?? [];

	public async ValueTask<CryptoComTicker[]> GetTickersAsync(string instrumentName,
		CancellationToken cancellationToken)
		=> (await SendPublicAsync<CryptoComDataResult<CryptoComTicker>>("public/get-tickers",
			new CryptoComInstrumentQuery { InstrumentName = instrumentName }, cancellationToken)).Data ?? [];

	public async ValueTask<CryptoComBookSnapshot> GetBookAsync(string instrumentName, int depth,
		CancellationToken cancellationToken)
		=> (await SendPublicAsync<CryptoComDataResult<CryptoComBookSnapshot>>("public/get-book",
			new CryptoComBookQuery { InstrumentName = instrumentName, Depth = depth }, cancellationToken))
			.Data?.FirstOrDefault() ?? throw new InvalidDataException("Crypto.com Exchange returned an empty order book.");

	public async ValueTask<CryptoComPublicTrade[]> GetTradesAsync(string instrumentName, int count,
		long? startTime, long? endTime, CancellationToken cancellationToken)
		=> (await SendPublicAsync<CryptoComDataResult<CryptoComPublicTrade>>("public/get-trades",
			new CryptoComTradesQuery
			{
				InstrumentName = instrumentName,
				Count = count.Min(150),
				StartTime = startTime,
				EndTime = endTime,
			}, cancellationToken)).Data ?? [];

	public async ValueTask<CryptoComCandle[]> GetCandlesAsync(string instrumentName, string timeFrame,
		int count, long? startTime, long? endTime, CancellationToken cancellationToken)
		=> (await SendPublicAsync<CryptoComDataResult<CryptoComCandle>>("public/get-candlestick",
			new CryptoComCandlesQuery
			{
				InstrumentName = instrumentName,
				TimeFrame = timeFrame,
				Count = count.Min(300),
				StartTime = startTime,
				EndTime = endTime,
			}, cancellationToken)).Data ?? [];

	public ValueTask<CryptoComOrderAck> CreateOrderAsync(CryptoComCreateOrderParams parameters,
		bool isAdvanced, CancellationToken cancellationToken)
		=> SendPrivateAsync<CryptoComOrderAck>(isAdvanced
			? "private/advanced/create-order"
			: "private/create-order", parameters, false, cancellationToken);

	public ValueTask<CryptoComOrderAck> AmendOrderAsync(CryptoComAmendOrderParams parameters,
		bool isAdvanced, CancellationToken cancellationToken)
		=> SendPrivateAsync<CryptoComOrderAck>(isAdvanced
			? "private/advanced/amend-order"
			: "private/amend-order", parameters, false, cancellationToken);

	public ValueTask<CryptoComOrderAck> CancelOrderAsync(CryptoComOrderIdentityParams parameters,
		bool isAdvanced, CancellationToken cancellationToken)
		=> SendPrivateAsync<CryptoComOrderAck>(isAdvanced
			? "private/advanced/cancel-order"
			: "private/cancel-order", parameters, false, cancellationToken);

	public ValueTask<CryptoComEmptyResult> CancelAllOrdersAsync(CryptoComCancelAllParams parameters,
		bool isAdvanced, CancellationToken cancellationToken)
		=> SendPrivateAsync<CryptoComEmptyResult>(isAdvanced
			? "private/advanced/cancel-all-orders"
			: "private/cancel-all-orders", parameters, false, cancellationToken);

	public async ValueTask<CryptoComOrder[]> GetOpenOrdersAsync(string instrumentName,
		bool isAdvanced, CancellationToken cancellationToken)
		=> (await SendPrivateAsync<CryptoComDataResult<CryptoComOrder>>(isAdvanced
			? "private/advanced/get-open-orders"
			: "private/get-open-orders",
			new CryptoComInstrumentParams { InstrumentName = instrumentName }, true, cancellationToken)).Data ?? [];

	public ValueTask<CryptoComOrder> GetOrderDetailAsync(CryptoComOrderIdentityParams parameters,
		bool isAdvanced, CancellationToken cancellationToken)
		=> SendPrivateAsync<CryptoComOrder>(isAdvanced
			? "private/advanced/get-order-detail"
			: "private/get-order-detail", parameters, true, cancellationToken);

	public async ValueTask<CryptoComOrder[]> GetOrderHistoryAsync(CryptoComHistoryParams parameters,
		bool isAdvanced, CancellationToken cancellationToken)
		=> (await SendPrivateAsync<CryptoComDataResult<CryptoComOrder>>(isAdvanced
			? "private/advanced/get-order-history"
			: "private/get-order-history", parameters, true, cancellationToken)).Data ?? [];

	public async ValueTask<CryptoComUserTrade[]> GetUserTradesAsync(CryptoComHistoryParams parameters,
		CancellationToken cancellationToken)
		=> (await SendPrivateAsync<CryptoComDataResult<CryptoComUserTrade>>("private/get-trades",
			parameters, true, cancellationToken)).Data ?? [];

	public async ValueTask<CryptoComBalance[]> GetBalancesAsync(CancellationToken cancellationToken)
		=> (await SendPrivateAsync<CryptoComDataResult<CryptoComBalance>>("private/user-balance",
			CryptoComEmptyParams.Instance, true, cancellationToken)).Data ?? [];

	public async ValueTask<CryptoComPosition[]> GetPositionsAsync(string instrumentName,
		CancellationToken cancellationToken)
		=> (await SendPrivateAsync<CryptoComDataResult<CryptoComPosition>>("private/get-positions",
			new CryptoComInstrumentParams { InstrumentName = instrumentName }, true, cancellationToken)).Data ?? [];

	private async ValueTask<TResult> SendPublicAsync<TResult>(string method,
		ICryptoComQueryParams parameters, CancellationToken cancellationToken)
	{
		var query = new CryptoComQueryBuilder();
		parameters.Write(query);
		var queryString = query.ToString();
		var relative = method + (queryString.IsEmpty() ? string.Empty : "?" + queryString);
		return await SendAsync<TResult>(HttpMethod.Get, method, new Uri(_endpoint, relative), null,
			true, cancellationToken);
	}

	private ValueTask<TResult> SendPrivateAsync<TResult>(string method, ICryptoComPrivateParams parameters,
		bool safe, CancellationToken cancellationToken)
	{
		if (_apiKey.IsEmpty())
			throw new InvalidOperationException("Crypto.com Exchange API key is not specified.");
		if (_signer is null)
			throw new InvalidOperationException("Crypto.com Exchange API secret is not specified.");

		var id = Interlocked.Increment(ref _nextRequestId);
		var nonce = DateTime.UtcNow.ToUnixMilliseconds();
		var request = new CryptoComPrivateRequest
		{
			Id = id,
			Method = method,
			ApiKey = _apiKey,
			Nonce = nonce,
			Parameters = parameters,
		};
		request.Signature = _signer.Sign(method, id, _apiKey, parameters, nonce);
		var payload = JsonConvert.SerializeObject(request, _jsonSettings);
		return SendAsync<TResult>(HttpMethod.Post, method, new Uri(_endpoint, method), payload,
			safe, cancellationToken);
	}

	private async ValueTask<TResult> SendAsync<TResult>(HttpMethod httpMethod, string apiMethod,
		Uri uri, string requestPayload, bool safe, CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(httpMethod, uri);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (requestPayload is not null)
				request.Content = new StringContent(requestPayload, Encoding.UTF8, "application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (HttpRequestException error) when (safe && attempt < _maxAttempts)
			{
				this.AddWarningLog("Crypto.com {0} transport error. Retrying safe request: {1}", apiMethod, error.Message);
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				var outcome = safe ? string.Empty : " Trading operation outcome is unknown; it was not retried.";
				throw new InvalidOperationException($"Crypto.com {apiMethod} failed: {error.Message}.{outcome}", error);
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

				CryptoComResponse<TResult> envelope;
				try
				{
					envelope = JsonConvert.DeserializeObject<CryptoComResponse<TResult>>(payload, _jsonSettings);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException($"Crypto.com {apiMethod} returned invalid JSON.", error);
				}

				if (envelope is null)
					throw new InvalidDataException($"Crypto.com {apiMethod} returned no JSON value.");

				if (safe && envelope.Code == 42901 && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}

				if (!response.IsSuccessStatusCode || envelope.Code != 0)
					throw CreateError(response.StatusCode, envelope, apiMethod, payload, safe);

				return envelope.Result;
			}
		}
	}

	private static Exception CreateError<TResult>(HttpStatusCode statusCode,
		CryptoComResponse<TResult> envelope, string method, string payload, bool safe)
	{
		var message = envelope.Message.IsEmpty(envelope.Original).IsEmpty(payload);
		var outcome = safe || (int)statusCode < 500
			? string.Empty
			: " Trading operation outcome is unknown; do not submit it again without checking order state.";
		return new CryptoComApiException(statusCode, envelope.Code,
			$"Crypto.com {method} failed ({(int)statusCode}, code {envelope.Code}): {message}.{outcome}");
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is TimeSpan retryAfter && retryAfter > TimeSpan.Zero)
			return retryAfter.Min(TimeSpan.FromSeconds(60));
		return TimeSpan.FromSeconds(1 << attempt.Min(5));
	}
}
