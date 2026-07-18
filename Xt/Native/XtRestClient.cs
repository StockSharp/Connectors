namespace StockSharp.Xt.Native;

readonly record struct XtParameter(string Name, string Value);

readonly record struct XtQuery(string Canonical, string Encoded);

readonly record struct XtPreparedRequest(string Query, string Body, string ContentType,
	string Timestamp, string Signature);

sealed class XtRestClient : BaseLogReceiver
{
	private const int _maxAttempts = 4;
	private const string _receiveWindow = "5000";
	private readonly Uri _spotEndpoint;
	private readonly Uri _futuresEndpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _signSync = new();
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private DateTime _nextRequestTime;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};

	public XtRestClient(string spotEndpoint, string futuresEndpoint, SecureString key,
		SecureString secret)
	{
		_spotEndpoint = new Uri(NormalizeEndpoint(spotEndpoint), UriKind.Absolute);
		_futuresEndpoint = new Uri(NormalizeEndpoint(futuresEndpoint), UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-XT-Connector/1.0");
	}

	public override string Name => nameof(Xt) + "_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && _hasher is not null;

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<XtSymbol[]> GetSpotSymbolsAsync(CancellationToken cancellationToken)
		=> (await SendSpotPublicAsync<XtSpotSymbolsResult>(HttpMethod.Get, "/v4/public/symbol",
			[], cancellationToken))?.Symbols ?? [];

	public async ValueTask<XtFuturesSymbol[]> GetFuturesSymbolsAsync(
		CancellationToken cancellationToken)
		=> (await SendFuturesPublicAsync<XtFuturesSymbolsResult>(HttpMethod.Get,
			"/future/market/v3/public/symbol/list", [], cancellationToken))?.Symbols ?? [];

	public async ValueTask<XtTicker[]> GetTickersAsync(XtSections section, string symbol,
		CancellationToken cancellationToken)
	{
		var parameters = new[] { new XtParameter("symbol", ToWireSymbol(symbol)) };
		if (section == XtSections.Spot)
			return await SendSpotPublicAsync<XtTicker[]>(HttpMethod.Get, "/v4/public/ticker",
				parameters, cancellationToken) ?? [];

		var ticker = await SendFuturesPublicAsync<XtTicker>(HttpMethod.Get,
			"/future/market/v1/public/q/agg-ticker", parameters, cancellationToken);
		return ticker is null ? [] : [ticker];
	}

	public async ValueTask<XtBookTicker[]> GetBookTickersAsync(XtSections section, string symbol,
		CancellationToken cancellationToken)
	{
		var parameters = new[] { new XtParameter("symbol", ToWireSymbol(symbol)) };
		if (section == XtSections.Spot)
			return await SendSpotPublicAsync<XtBookTicker[]>(HttpMethod.Get,
				"/v4/public/ticker/book", parameters, cancellationToken) ?? [];

		var ticker = await SendFuturesPublicAsync<XtTicker>(HttpMethod.Get,
			"/future/market/v1/public/q/agg-ticker", parameters, cancellationToken);
		return ticker is null
			? []
			: [new XtBookTicker
			{
				Symbol = ticker.Symbol,
				Timestamp = ticker.Time,
				BidPrice = ticker.BidPrice,
				BidSize = ticker.BidSize,
				AskPrice = ticker.AskPrice,
				AskSize = ticker.AskSize,
			}];
	}

	public async ValueTask<XtMarketTrade[]> GetTradesAsync(XtSections section, string symbol,
		int limit, CancellationToken cancellationToken)
	{
		var parameters = new[]
		{
			new XtParameter("symbol", ToWireSymbol(symbol)),
			new XtParameter(section == XtSections.Spot ? "limit" : "num",
				limit.Min(1000).Max(1).ToString(CultureInfo.InvariantCulture)),
		};
		return section == XtSections.Spot
			? await SendSpotPublicAsync<XtMarketTrade[]>(HttpMethod.Get,
				"/v4/public/trade/recent", parameters, cancellationToken) ?? []
			: await SendFuturesPublicAsync<XtMarketTrade[]>(HttpMethod.Get,
				"/future/market/v1/public/q/deal", parameters, cancellationToken) ?? [];
	}

	public async ValueTask<XtDepthData> GetDepthAsync(XtSections section, string symbol, int limit,
		CancellationToken cancellationToken)
	{
		var parameters = new[]
		{
			new XtParameter("symbol", ToWireSymbol(symbol)),
			new XtParameter(section == XtSections.Spot ? "limit" : "level",
				limit.ToString(CultureInfo.InvariantCulture)),
		};
		return section == XtSections.Spot
			? await SendSpotPublicAsync<XtDepthData>(HttpMethod.Get, "/v4/public/depth",
				parameters, cancellationToken)
			: await SendFuturesPublicAsync<XtDepthData>(HttpMethod.Get,
				"/future/market/v1/public/q/depth", parameters, cancellationToken);
	}

	public async ValueTask<XtKline[]> GetKlinesAsync(XtSections section, string symbol,
		TimeSpan timeFrame, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		var parameters = new[]
		{
			new XtParameter("symbol", ToWireSymbol(symbol)),
			new XtParameter("interval", timeFrame.ToXtInterval()),
			new XtParameter("startTime", from?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
			new XtParameter("endTime", to?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
			new XtParameter("limit", limit.Min(section == XtSections.Spot ? 1000 : 1500).Max(1)
				.ToString(CultureInfo.InvariantCulture)),
		};
		return section == XtSections.Spot
			? await SendSpotPublicAsync<XtKline[]>(HttpMethod.Get, "/v4/public/kline",
				parameters, cancellationToken) ?? []
			: await SendFuturesPublicAsync<XtKline[]>(HttpMethod.Get,
				"/future/market/v1/public/q/kline", parameters, cancellationToken) ?? [];
	}

	public async ValueTask<XtBalance[]> GetSpotBalancesAsync(CancellationToken cancellationToken)
		=> (await SendSpotPrivateGetAsync<XtSpotBalancesResult>("/v4/balances", [],
			cancellationToken))?.Balances ?? [];

	public async ValueTask<XtBalance[]> GetFuturesBalancesAsync(CancellationToken cancellationToken)
		=> await SendFuturesPrivateGetAsync<XtBalance[]>(
			"/future/user/v1/compat/balance/list", [], cancellationToken) ?? [];

	public async ValueTask<XtPosition[]> GetFuturesPositionsAsync(string symbol,
		CancellationToken cancellationToken)
		=> await SendFuturesPrivateGetAsync<XtPosition[]>("/future/user/v1/position",
			[new("symbol", symbol.IsEmpty() ? null : ToWireSymbol(symbol))], cancellationToken) ?? [];

	public async ValueTask<string> GetSpotWsTokenAsync(CancellationToken cancellationToken)
		=> (await SendSpotPrivateNoBodyAsync<XtWsTokenResult>(HttpMethod.Post,
			"/v4/ws-token", cancellationToken))?.AccessToken;

	public async ValueTask<string> GetFuturesListenKeyAsync(CancellationToken cancellationToken)
		=> (await SendFuturesPrivateGetAsync<XtListenKeyResult>(
			"/future/user/v1/user/listen-key", [], cancellationToken))?.ListenKey;

	public ValueTask<XtOrderResult> PlaceSpotOrderAsync(XtSpotOrderRequest request,
		CancellationToken cancellationToken)
		=> SendSpotPrivateBodyAsync<XtOrderResult, XtSpotOrderRequest>(HttpMethod.Post,
			"/v4/order", request, cancellationToken);

	public ValueTask<XtOrderResult> PlaceFuturesOrderAsync(XtFuturesOrderRequest request,
		CancellationToken cancellationToken)
		=> SendFuturesPrivateBodyAsync<XtOrderResult, XtFuturesOrderRequest>(HttpMethod.Post,
			"/future/trade/v1/order/create", request, cancellationToken);

	public async ValueTask CancelOrderAsync(XtSections section, long orderId,
		CancellationToken cancellationToken)
	{
		if (section == XtSections.Spot)
		{
			_ = await SendSpotPrivateNoBodyAsync<XtOrderResult>(HttpMethod.Delete,
				$"/v4/order/{orderId.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
			return;
		}

		_ = await SendFuturesPrivateFormAsync<string, XtFuturesCancelRequest>(HttpMethod.Post,
			"/future/trade/v1/order/cancel", new() { OrderId = orderId }, false, cancellationToken);
	}

	public async ValueTask CancelAllOrdersAsync(XtSections section, string symbol,
		CancellationToken cancellationToken)
	{
		if (section == XtSections.Spot)
		{
			_ = await SendSpotPrivateBodyAsync<XtEmptyResult, XtSpotCancelAllRequest>(
				HttpMethod.Delete, "/v4/open-order", new() { Symbol = ToWireSymbol(symbol) },
				cancellationToken);
			return;
		}

		_ = await SendFuturesPrivateFormAsync<bool, XtFuturesSymbolRequest>(HttpMethod.Post,
			"/future/trade/v1/order/cancel-all", new() { Symbol = ToWireSymbol(symbol) },
			false, cancellationToken);
	}

	public async ValueTask<XtOrder[]> GetOpenOrdersAsync(XtSections section, string symbol,
		CancellationToken cancellationToken)
	{
		if (section == XtSections.Spot)
			return await SendSpotPrivateGetAsync<XtOrder[]>("/v4/open-order",
				[new("symbol", symbol.IsEmpty() ? null : ToWireSymbol(symbol)), new("bizType", "SPOT")],
				cancellationToken) ?? [];

		return await SendFuturesPrivateFormAsync<XtOrder[], XtFuturesSymbolRequest>(HttpMethod.Post,
			"/future/trade/v1/order/list-open-order",
			new() { Symbol = symbol.IsEmpty() ? null : ToWireSymbol(symbol) }, true, cancellationToken) ?? [];
	}

	public async ValueTask<XtOrder[]> GetOrderHistoryAsync(XtSections section, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
	{
		var parameters = new[]
		{
			new XtParameter("symbol", symbol.IsEmpty() ? null : ToWireSymbol(symbol)),
			new XtParameter("bizType", section == XtSections.Spot ? "SPOT" : null),
			new XtParameter("direction", "NEXT"),
			new XtParameter("limit", limit.Min(100).Max(1).ToString(CultureInfo.InvariantCulture)),
			new XtParameter("startTime", from?.ToUniversalTime().ToUnixMilliseconds()
				.ToString(CultureInfo.InvariantCulture)),
			new XtParameter("endTime", to?.ToUniversalTime().ToUnixMilliseconds()
				.ToString(CultureInfo.InvariantCulture)),
		};
		var page = section == XtSections.Spot
			? await SendSpotPrivateGetAsync<XtOrderPage>("/v4/history-order", parameters,
				cancellationToken)
			: await SendFuturesPrivateGetAsync<XtOrderPage>("/future/trade/v1/order/list-history",
				parameters, cancellationToken);
		return page?.Items ?? [];
	}

	public async ValueTask<XtFill[]> GetFillsAsync(XtSections section, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
	{
		var parameters = new[]
		{
			new XtParameter("symbol", symbol.IsEmpty() ? null : ToWireSymbol(symbol)),
			new XtParameter("bizType", section == XtSections.Spot ? "SPOT" : null),
			new XtParameter("direction", section == XtSections.Spot ? "NEXT" : null),
			new XtParameter(section == XtSections.Spot ? "limit" : "size",
				limit.Min(100).Max(1).ToString(CultureInfo.InvariantCulture)),
			new XtParameter("page", section == XtSections.Futures ? "1" : null),
			new XtParameter("startTime", from?.ToUniversalTime().ToUnixMilliseconds()
				.ToString(CultureInfo.InvariantCulture)),
			new XtParameter("endTime", to?.ToUniversalTime().ToUnixMilliseconds()
				.ToString(CultureInfo.InvariantCulture)),
		};
		var page = section == XtSections.Spot
			? await SendSpotPrivateGetAsync<XtFillPage>("/v4/trade", parameters, cancellationToken)
			: await SendFuturesPrivateGetAsync<XtFillPage>("/future/trade/v1/order/trade-list",
				parameters, cancellationToken);
		return page?.Items ?? [];
	}

	private ValueTask<TData> SendSpotPublicAsync<TData>(HttpMethod method, string path,
		XtParameter[] parameters, CancellationToken cancellationToken)
	{
		var query = BuildQuery(parameters);
		return SendSpotAsync<TData>(method, path, true, true,
			() => new(query.Encoded, null, null, null, null), cancellationToken);
	}

	private ValueTask<TData> SendFuturesPublicAsync<TData>(HttpMethod method, string path,
		XtParameter[] parameters, CancellationToken cancellationToken)
	{
		var query = BuildQuery(parameters);
		return SendFuturesAsync<TData>(method, path, true, true,
			() => new(query.Encoded, null, null, null, null), cancellationToken);
	}

	private ValueTask<TData> SendSpotPrivateGetAsync<TData>(string path, XtParameter[] parameters,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		return SendSpotAsync<TData>(HttpMethod.Get, path, false, true,
			() => PrepareSpot(HttpMethod.Get, path, BuildQuery(parameters), null, null),
			cancellationToken);
	}

	private ValueTask<TData> SendFuturesPrivateGetAsync<TData>(string path,
		XtParameter[] parameters, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		return SendFuturesAsync<TData>(HttpMethod.Get, path, false, true,
			() => PrepareFutures(path, BuildQuery(parameters), null, null), cancellationToken);
	}

	private ValueTask<TData> SendSpotPrivateNoBodyAsync<TData>(HttpMethod method, string path,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		return SendSpotAsync<TData>(method, path, false, false,
			() => PrepareSpot(method, path, default, null, null), cancellationToken);
	}

	private ValueTask<TData> SendSpotPrivateBodyAsync<TData, TRequest>(HttpMethod method,
		string path, TRequest request, CancellationToken cancellationToken)
		where TRequest : class
	{
		EnsureCredentials();
		ArgumentNullException.ThrowIfNull(request);
		var body = JsonConvert.SerializeObject(request, _jsonSettings);
		return SendSpotAsync<TData>(method, path, false, false,
			() => PrepareSpot(method, path, default, body, "application/json"), cancellationToken);
	}

	private ValueTask<TData> SendFuturesPrivateBodyAsync<TData, TRequest>(HttpMethod method,
		string path, TRequest request, CancellationToken cancellationToken)
		where TRequest : class
	{
		EnsureCredentials();
		ArgumentNullException.ThrowIfNull(request);
		var body = JsonConvert.SerializeObject(request, _jsonSettings);
		return SendFuturesAsync<TData>(method, path, false, false,
			() => PrepareFutures(path, default, body, "application/json"), cancellationToken);
	}

	private ValueTask<TData> SendFuturesPrivateFormAsync<TData, TRequest>(HttpMethod method,
		string path, TRequest request, bool isSafe, CancellationToken cancellationToken)
		where TRequest : class
	{
		EnsureCredentials();
		ArgumentNullException.ThrowIfNull(request);
		var form = request switch
		{
			XtFuturesSymbolRequest symbolRequest => BuildQuery(
				[new("symbol", symbolRequest.Symbol)]),
			XtFuturesCancelRequest cancelRequest => BuildQuery(
				[new("orderId", cancelRequest.OrderId.ToString(CultureInfo.InvariantCulture))]),
			_ => throw new NotSupportedException($"Unsupported XT.COM form DTO {typeof(TRequest).Name}."),
		};
		return SendFuturesAsync<TData>(method, path, false, isSafe,
			() => PrepareFutures(path, default, form.Canonical,
				"application/x-www-form-urlencoded"), cancellationToken, form.Encoded);
	}

	private XtPreparedRequest PrepareSpot(HttpMethod method, string path, XtQuery query,
		string body, string contentType)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
		var header = "validate-algorithms=HmacSHA256&validate-appkey=" + _apiKey +
			"&validate-recvwindow=" + _receiveWindow + "&validate-timestamp=" + timestamp;
		var payload = header + "#" + method.Method.ToUpperInvariant() + "#" + path;
		if (!query.Canonical.IsEmpty())
			payload += "#" + query.Canonical;
		if (!body.IsEmpty())
			payload += "#" + body;
		return new(query.Encoded, body, contentType, timestamp, Sign(payload));
	}

	private XtPreparedRequest PrepareFutures(string path, XtQuery query, string body,
		string contentType)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
		var header = "validate-appkey=" + _apiKey + "&validate-timestamp=" + timestamp;
		var payload = header + "#" + path;
		if (!query.Canonical.IsEmpty())
			payload += "#" + query.Canonical;
		else if (!body.IsEmpty())
			payload += "#" + body;
		return new(query.Encoded, body, contentType, timestamp, Sign(payload));
	}

	private async ValueTask<TData> SendSpotAsync<TData>(HttpMethod method, string path,
		bool isPublic, bool isSafe, Func<XtPreparedRequest> prepare,
		CancellationToken cancellationToken, string encodedBody = null)
	{
		var responseBody = await SendRawAsync(_spotEndpoint, XtSections.Spot, method, path,
			isPublic, isSafe, prepare, cancellationToken, encodedBody);
		XtSpotResponse<TData> response;
		try
		{
			response = JsonConvert.DeserializeObject<XtSpotResponse<TData>>(responseBody, _jsonSettings)
				?? throw new InvalidDataException($"XT.COM Spot {path} returned no JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException($"XT.COM Spot {path} returned invalid JSON.", error);
		}
		if (!response.IsSuccess)
			throw new InvalidOperationException($"XT.COM Spot {path} failed (code {response.Code}): " +
				$"{response.Message}. " + WriteSafetyNote(isSafe));
		return response.Result;
	}

	private async ValueTask<TData> SendFuturesAsync<TData>(HttpMethod method, string path,
		bool isPublic, bool isSafe, Func<XtPreparedRequest> prepare,
		CancellationToken cancellationToken, string encodedBody = null)
	{
		var responseBody = await SendRawAsync(_futuresEndpoint, XtSections.Futures, method, path,
			isPublic, isSafe, prepare, cancellationToken, encodedBody);
		XtFuturesResponse<TData> response;
		try
		{
			response = JsonConvert.DeserializeObject<XtFuturesResponse<TData>>(responseBody, _jsonSettings)
				?? throw new InvalidDataException($"XT.COM Futures {path} returned no JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException($"XT.COM Futures {path} returned invalid JSON.", error);
		}
		if (!response.IsSuccess)
			throw new InvalidOperationException($"XT.COM Futures {path} failed (code {response.Code}, " +
				$"exchange code {response.Error?.Code}): {response.Error?.Message.IsEmpty(response.Message)}. " +
				WriteSafetyNote(isSafe));
		return response.Result;
	}

	private async ValueTask<string> SendRawAsync(Uri endpoint, XtSections section,
		HttpMethod method, string path, bool isPublic, bool isSafe,
		Func<XtPreparedRequest> prepare, CancellationToken cancellationToken,
		string encodedBody)
	{
		for (var attempt = 1; ; attempt++)
		{
			await WaitForRateLimitAsync(cancellationToken);
			var prepared = prepare();
			var relative = path + (prepared.Query.IsEmpty() ? string.Empty : "?" + prepared.Query);
			using var request = new HttpRequestMessage(method, new Uri(endpoint, relative));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (!isPublic)
			{
				request.Headers.TryAddWithoutValidation("validate-appkey", _apiKey);
				request.Headers.TryAddWithoutValidation("validate-timestamp", prepared.Timestamp);
				request.Headers.TryAddWithoutValidation("validate-signature", prepared.Signature);
				if (section == XtSections.Spot)
				{
					request.Headers.TryAddWithoutValidation("validate-algorithms", "HmacSHA256");
					request.Headers.TryAddWithoutValidation("validate-recvwindow", _receiveWindow);
				}
			}
			if (prepared.Body is not null)
				request.Content = new StringContent(encodedBody ?? prepared.Body, Encoding.UTF8,
					prepared.ContentType ?? "application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (HttpRequestException error) when (isSafe && attempt < _maxAttempts)
			{
				this.AddWarningLog("XT.COM {0} transport error. Retrying read request: {1}",
					relative, error.Message);
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				throw new InvalidOperationException(CreateTransportError(relative, isSafe, error.Message),
					error);
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
				return responseBody;
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
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(110);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static XtQuery BuildQuery(IEnumerable<XtParameter> parameters)
	{
		var ordered = parameters
			.Where(static parameter => !parameter.Value.IsEmpty())
			.OrderBy(static parameter => parameter.Name, StringComparer.Ordinal)
			.ToArray();
		return new(
			ordered.Select(static parameter => parameter.Name + "=" + parameter.Value).Join("&"),
			ordered.Select(static parameter => Escape(parameter.Name) + "=" + Escape(parameter.Value)).Join("&"));
	}

	private string Sign(string payload)
	{
		byte[] hash;
		using (_signSync.EnterScope())
			hash = _hasher.ComputeHash(payload.UTF8());
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"XT.COM API key and secret are required for private requests.");
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is TimeSpan delay && delay > TimeSpan.Zero)
			return delay.Min(TimeSpan.FromSeconds(30));
		return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
	}

	private static Exception CreateHttpError(HttpStatusCode status, string path, string body,
		bool isSafe)
		=> new InvalidOperationException($"XT.COM {path} returned HTTP {(int)status}: {body}. " +
			WriteSafetyNote(isSafe));

	private static string CreateTransportError(string path, bool isSafe, string message)
		=> $"XT.COM {path} transport error: {message}. " + WriteSafetyNote(isSafe);

	private static string WriteSafetyNote(bool isSafe)
		=> isSafe ? "The read request failed." :
			"The write was not retried; inspect exchange state before retrying.";

	private static string ToWireSymbol(string symbol) => symbol?.ToLowerInvariant();
	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
