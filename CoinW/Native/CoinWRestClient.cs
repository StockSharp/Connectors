namespace StockSharp.CoinW.Native;

sealed class CoinWRestClient : BaseLogReceiver
{
	private const int _maxAttempts = 4;
	private readonly Uri _spotEndpoint;
	private readonly Uri _futuresEndpoint;
	private readonly HttpClient _http;
	private readonly string _apiKey;
	private readonly string _secret;
	private readonly HMACSHA256 _futuresHasher;
	private readonly Lock _signSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};

	public CoinWRestClient(string spotEndpoint, string futuresEndpoint, SecureString key, SecureString secret)
	{
		_spotEndpoint = new Uri(NormalizeEndpoint(spotEndpoint), UriKind.Absolute);
		_futuresEndpoint = new Uri(NormalizeEndpoint(futuresEndpoint), UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_secret = secret.IsEmpty() ? null : secret.UnSecure();
		_futuresHasher = _secret.IsEmpty() ? null : new HMACSHA256(_secret.UTF8());
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-CoinW-Connector/1.0");
	}

	public override string Name => nameof(CoinW) + "_Rest";

	public bool IsCredentialsAvailable => !_apiKey.IsEmpty() && !_secret.IsEmpty() && _futuresHasher is not null;

	protected override void DisposeManaged()
	{
		_futuresHasher?.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<CoinWSpotSymbol[]> GetSpotSymbolsAsync(CancellationToken cancellationToken)
		=> (await SendSpotPublicAsync<CoinWSpotSymbol[]>("returnSymbol", [], cancellationToken)).Data ?? [];

	public async ValueTask<CoinWSpotTicker[]> GetSpotTickersAsync(CancellationToken cancellationToken)
		=> (await SendSpotPublicAsync<CoinWSpotTickers>("returnTicker", [], cancellationToken)).Data?.Items ?? [];

	public async ValueTask<CoinWSpotOrderBook> GetSpotOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> (await SendSpotPublicAsync<CoinWSpotOrderBook[]>("returnOrderBook",
			[
				new("size", (depth > 5 ? 20 : 5).ToString(CultureInfo.InvariantCulture)),
				new("symbol", symbol),
			], cancellationToken)).Data?.FirstOrDefault();

	public async ValueTask<CoinWSpotPublicTrade[]> GetSpotTradesAsync(string symbol, DateTime? from,
		DateTime? to, CancellationToken cancellationToken)
		=> (await SendSpotPublicAsync<CoinWSpotPublicTrade[]>("returnTradeHistory",
			[
				new("symbol", symbol),
				new("start", from?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
				new("end", to?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
			], cancellationToken)).Data ?? [];

	public async ValueTask<CoinWSpotCandle[]> GetSpotCandlesAsync(string symbol, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
		=> (await SendSpotPublicAsync<CoinWSpotCandle[]>("returnChartData",
			[
				new("currencyPair", symbol),
				new("period", ((long)timeFrame.TotalSeconds).ToString(CultureInfo.InvariantCulture)),
				new("start", from?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
				new("end", to?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
			], cancellationToken)).Data ?? [];

	public async ValueTask<CoinWFuturesInstrument[]> GetFuturesInstrumentsAsync(
		CancellationToken cancellationToken)
		=> (await SendFuturesPublicAsync<CoinWFuturesInstrument[]>("/v1/perpum/instruments", [],
			cancellationToken)).Data ?? [];

	public async ValueTask<CoinWFuturesTicker[]> GetFuturesTickersAsync(CancellationToken cancellationToken)
		=> (await SendFuturesPublicAsync<CoinWFuturesTicker[]>("/v1/perpumPublic/tickers", [],
			cancellationToken)).Data ?? [];

	public async ValueTask<CoinWFuturesOrderBook> GetFuturesOrderBookAsync(string nativeSymbol,
		CancellationToken cancellationToken)
		=> (await SendFuturesPublicAsync<CoinWFuturesOrderBook>("/v1/perpumPublic/depth",
			[new("base", nativeSymbol)], cancellationToken)).Data;

	public async ValueTask<CoinWFuturesTrade[]> GetFuturesTradesAsync(string nativeSymbol,
		CancellationToken cancellationToken)
		=> (await SendFuturesPublicAsync<CoinWFuturesTrade[]>("/v1/perpumPublic/trades",
			[new("base", nativeSymbol)], cancellationToken)).Data ?? [];

	public async ValueTask<CoinWFuturesCandle[]> GetFuturesCandlesAsync(string nativeSymbol,
		TimeSpan timeFrame, DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
		=> (await SendFuturesPublicAsync<CoinWFuturesCandle[]>("/v1/perpumPublic/klines",
			[
				new("currencyCode", nativeSymbol),
				new("granularity", timeFrame.ToCoinWFuturesGranularity()),
				new("limit", limit.Min(1500).Max(1).ToString(CultureInfo.InvariantCulture)),
				new("klineType", "0"),
				new("sinceStr", from?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
				new("sinceEndStr", to?.ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)),
			], cancellationToken)).Data ?? [];

	public async ValueTask<CoinWSpotBalance[]> GetSpotBalancesAsync(CancellationToken cancellationToken)
		=> (await SendSpotPrivateAsync<CoinWSpotBalances>("returnCompleteBalances", [], true,
			cancellationToken)).Data?.Items ?? [];

	public async ValueTask<CoinWSpotOrderResult> PlaceSpotOrderAsync(CoinWSpotOrderRequest request,
		CancellationToken cancellationToken)
		=> (await SendSpotPrivateAsync<CoinWSpotOrderResult>("doTrade",
			[
				new("symbol", request.Symbol),
				new("type", request.Side),
				new("amount", request.Amount),
				new("rate", request.Price),
				new("funds", request.Funds),
				new("isMarket", request.IsMarket ? "true" : "false"),
				new("out_trade_no", request.ClientOrderId),
			], false, cancellationToken)).Data;

	public async ValueTask<CoinWSpotOrderResult> CancelSpotOrderAsync(CoinWSpotCancelRequest request,
		CancellationToken cancellationToken)
		=> (await SendSpotPrivateAsync<CoinWSpotOrderResult>("cancelOrder",
			[new("orderNumber", request.OrderId)], false, cancellationToken)).Data;

	public async ValueTask CancelAllSpotOrdersAsync(string symbol, CancellationToken cancellationToken)
	{
		_ = await SendSpotPrivateAsync<CoinWValue>("cancelAllOrder",
			[new("currencyPair", symbol)], false, cancellationToken);
	}

	public async ValueTask<CoinWSpotOrder[]> GetSpotOpenOrdersAsync(CoinWSpotOpenOrdersRequest request,
		CancellationToken cancellationToken)
		=> (await SendSpotPrivateAsync<CoinWSpotOrder[]>("returnOpenOrders",
			[
				new("currencyPair", request.Symbol),
				new("startAt", request.From?.ToString(CultureInfo.InvariantCulture)),
				new("endAt", request.To?.ToString(CultureInfo.InvariantCulture)),
			], true, cancellationToken)).Data ?? [];

	public async ValueTask<CoinWSpotOrder[]> GetSpotOrderHistoryAsync(
		CoinWSpotTradeHistoryRequest request, CancellationToken cancellationToken)
		=> (await SendSpotPrivateAsync<CoinWSpotOrder[]>("returnUTradeHistory",
			[
				new("currencyPair", request.Symbol),
				new("startAt", request.From?.ToString(CultureInfo.InvariantCulture)),
				new("endAt", request.To?.ToString(CultureInfo.InvariantCulture)),
			], true, cancellationToken)).Data ?? [];

	public async ValueTask<CoinWSpotTrade[]> GetSpotUserTradesAsync(CoinWSpotUserTradesRequest request,
		CancellationToken cancellationToken)
		=> (await SendSpotPrivateAsync<CoinWSpotTradesPage>("getUserTrades",
			[
				new("symbol", request.Symbol),
				new("startAt", request.From?.ToString(CultureInfo.InvariantCulture)),
				new("endAt", request.To?.ToString(CultureInfo.InvariantCulture)),
				new("limit", request.Limit.Min(100).Max(1).ToString(CultureInfo.InvariantCulture)),
			], true, cancellationToken)).Data?.Items ?? [];

	public async ValueTask<CoinWValue> PlaceFuturesOrderAsync(CoinWFuturesOrderRequest request,
		CancellationToken cancellationToken)
		=> (await SendFuturesPrivateAsync<CoinWValue, CoinWFuturesOrderRequest>(HttpMethod.Post,
			"/v1/perpum/order", [], request, false, cancellationToken)).Data;

	public async ValueTask CancelFuturesOrderAsync(CoinWFuturesCancelOrderRequest request,
		CancellationToken cancellationToken)
	{
		_ = await SendFuturesPrivateAsync<CoinWValue, CoinWFuturesCancelOrderRequest>(HttpMethod.Delete,
			"/v1/perpum/order", [], request, false, cancellationToken);
	}

	public async ValueTask<CoinWValue> CloseFuturesPositionAsync(CoinWFuturesClosePositionRequest request,
		CancellationToken cancellationToken)
		=> (await SendFuturesPrivateAsync<CoinWValue, CoinWFuturesClosePositionRequest>(HttpMethod.Delete,
			"/v1/perpum/positions", [], request, false, cancellationToken)).Data;

	public async ValueTask CloseAllFuturesPositionsAsync(string nativeSymbol,
		CancellationToken cancellationToken)
	{
		_ = await SendFuturesPrivateAsync<CoinWValue, CoinWFuturesCloseAllRequest>(HttpMethod.Delete,
			"/v1/perpum/allpositions", [], new() { Instrument = nativeSymbol }, false, cancellationToken);
	}

	public async ValueTask<CoinWFuturesOrder[]> GetFuturesOpenOrdersAsync(string nativeSymbol,
		int limit, CancellationToken cancellationToken)
	{
		var orders = new List<CoinWFuturesOrder>();
		foreach (var type in new[] { "plan", "execute", "planTrigger" })
		{
			var request = new CoinWFuturesOpenOrdersRequest
			{
				Instrument = nativeSymbol,
				PositionType = type,
				PageSize = limit.Min(100).Max(1),
			};
			var response = await SendFuturesPrivateAsync<CoinWFuturesOrdersPage>(HttpMethod.Get,
				"/v1/perpum/orders/open",
				[
					new("instrument", request.Instrument),
					new("positionType", request.PositionType),
					new("page", request.Page.ToString(CultureInfo.InvariantCulture)),
					new("pageSize", request.PageSize.ToString(CultureInfo.InvariantCulture)),
				], true, cancellationToken);
			orders.AddRange(response.Data?.Items ?? []);
		}
		return [.. orders.GroupBy(static order => order.OrderId, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())];
	}

	public async ValueTask<CoinWFuturesOrder[]> GetFuturesOrderHistoryAsync(
		CoinWFuturesHistoryRequest request, CancellationToken cancellationToken)
		=> (await SendFuturesPrivateAsync<CoinWFuturesOrdersPage>(HttpMethod.Get,
			"/v1/perpum/orders/history",
			[
				new("instrument", request.Instrument),
				new("originType", request.OriginType),
				new("page", request.Page.ToString(CultureInfo.InvariantCulture)),
				new("pageSize", request.PageSize.Min(100).Max(1).ToString(CultureInfo.InvariantCulture)),
			], true, cancellationToken)).Data?.Items ?? [];

	public async ValueTask<CoinWFuturesAssets> GetFuturesAssetsAsync(string quote,
		CancellationToken cancellationToken)
		=> (await SendFuturesPrivateAsync<CoinWFuturesAssets>(HttpMethod.Get,
			"/v1/perpum/account/getUserAssets", [new("quote", quote)], true, cancellationToken)).Data;

	public async ValueTask<CoinWFuturesPosition[]> GetFuturesPositionsAsync(
		CancellationToken cancellationToken)
		=> (await SendFuturesPrivateAsync<CoinWFuturesPosition[]>(HttpMethod.Get,
			"/v1/perpum/positions/all", [], true, cancellationToken)).Data ?? [];

	public CoinWWebSocketAuthentication CreateWebSocketAuthentication()
	{
		EnsureCredentials();
		return new()
		{
			ApiKey = _apiKey,
			Secret = _secret,
		};
	}

	private ValueTask<CoinWSpotResponse<TData>> SendSpotPublicAsync<TData>(string command,
		CoinWParameter[] parameters, CancellationToken cancellationToken)
	{
		var query = BuildQuery(parameters);
		var relative = "/api/v1/public?command=" + Escape(command) +
			(query.IsEmpty() ? string.Empty : "&" + query);
		return SendAsync<CoinWSpotResponse<TData>>(CoinWSections.Spot, HttpMethod.Get, relative,
			null, true, true, cancellationToken);
	}

	private ValueTask<CoinWSpotResponse<TData>> SendSpotPrivateAsync<TData>(string command,
		CoinWParameter[] parameters, bool safe, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var relative = "/api/v1/private?" + BuildSpotPrivateQuery(command, parameters);
		return SendAsync<CoinWSpotResponse<TData>>(CoinWSections.Spot, HttpMethod.Post, relative,
			string.Empty, false, safe, cancellationToken);
	}

	private ValueTask<CoinWFuturesResponse<TData>> SendFuturesPublicAsync<TData>(string path,
		CoinWParameter[] parameters, CancellationToken cancellationToken)
	{
		var query = BuildQuery(parameters);
		return SendAsync<CoinWFuturesResponse<TData>>(CoinWSections.Futures, HttpMethod.Get,
			path + (query.IsEmpty() ? string.Empty : "?" + query), null, true, true, cancellationToken);
	}

	private ValueTask<CoinWFuturesResponse<TData>> SendFuturesPrivateAsync<TData>(HttpMethod method,
		string path, CoinWParameter[] parameters, bool safe, CancellationToken cancellationToken)
	{
		EnsureCredentials();
		var query = BuildQuery(parameters);
		return SendFuturesSignedAsync<CoinWFuturesResponse<TData>>(method, path, query, null, safe,
			cancellationToken);
	}

	private ValueTask<CoinWFuturesResponse<TData>> SendFuturesPrivateAsync<TData, TRequest>(HttpMethod method,
		string path, CoinWParameter[] parameters, TRequest request, bool safe,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		EnsureCredentials();
		var query = BuildQuery(parameters);
		var body = JsonConvert.SerializeObject(request, _jsonSettings);
		return SendFuturesSignedAsync<CoinWFuturesResponse<TData>>(method, path, query, body, safe,
			cancellationToken);
	}

	private ValueTask<TResult> SendFuturesSignedAsync<TResult>(HttpMethod method, string path,
		string query, string body, bool safe, CancellationToken cancellationToken)
		where TResult : class
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
		var relative = path + (query.IsEmpty() ? string.Empty : "?" + query);
		var payload = timestamp + method.Method.ToUpperInvariant() + path +
			(method == HttpMethod.Get && !query.IsEmpty() ? "?" + query : body ?? string.Empty);
		return SendAsync<TResult>(CoinWSections.Futures, method, relative, body, false, safe,
			cancellationToken, timestamp, SignFutures(payload));
	}

	private async ValueTask<TResult> SendAsync<TResult>(CoinWSections section, HttpMethod method,
		string relative, string body, bool isPublic, bool safe, CancellationToken cancellationToken,
		string timestamp = null, string signature = null)
		where TResult : class
	{
		var endpoint = section == CoinWSections.Spot ? _spotEndpoint : _futuresEndpoint;
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(method, new Uri(endpoint, relative));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (!isPublic && section == CoinWSections.Futures)
			{
				request.Headers.TryAddWithoutValidation("api_key", _apiKey);
				request.Headers.TryAddWithoutValidation("timestamp", timestamp);
				request.Headers.TryAddWithoutValidation("sign", signature);
			}
			if (body is not null)
				request.Content = new StringContent(body, Encoding.UTF8, "application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (HttpRequestException error) when (safe && attempt < _maxAttempts)
			{
				this.AddWarningLog("CoinW {0} transport error. Retrying safe request: {1}",
					relative, error.Message);
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				throw new InvalidOperationException(CreateTransportError(relative, safe, error.Message), error);
			}

			using (response)
			{
				var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
				if (safe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500) && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateError(response.StatusCode, relative, responseBody, safe);

				ThrowIfApiError(section, relative, responseBody, safe);
				try
				{
					return JsonConvert.DeserializeObject<TResult>(responseBody, _jsonSettings)
						?? throw new InvalidDataException($"CoinW {relative} returned no JSON value.");
				}
				catch (JsonException error)
				{
					throw new InvalidDataException($"CoinW {relative} returned invalid JSON.", error);
				}
			}
		}
	}

	private string BuildSpotPrivateQuery(string command, CoinWParameter[] parameters)
	{
		var signed = new List<CoinWParameter>(parameters.Length + 1);
		foreach (var parameter in parameters)
		{
			if (!parameter.Value.IsEmpty())
				signed.Add(parameter);
		}
		signed.Add(new("api_key", _apiKey));
		var ordered = signed.OrderBy(static parameter => parameter.Name, StringComparer.Ordinal).ToArray();
		var preimage = new StringBuilder();
		foreach (var parameter in ordered)
			preimage.Append(parameter.Name).Append('=').Append(parameter.Value).Append('&');
		preimage.Append("secret_key=").Append(_secret);
		var hash = MD5.HashData(preimage.ToString().UTF8());
		var signature = Convert.ToHexString(hash);
		return "command=" + Escape(command) + "&sign=" + signature + "&" + BuildQuery(ordered);
	}

	private static string BuildQuery(IEnumerable<CoinWParameter> parameters)
		=> parameters
			.Where(static parameter => !parameter.Value.IsEmpty())
			.Select(static parameter => Escape(parameter.Name) + "=" + Escape(parameter.Value))
			.Join("&");

	private string SignFutures(string payload)
	{
		byte[] hash;
		using (_signSync.EnterScope())
			hash = _futuresHasher.ComputeHash(payload.UTF8());
		return Convert.ToBase64String(hash);
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException("CoinW API key and secret are required for private requests.");
	}

	private static void ThrowIfApiError(CoinWSections section, string path, string responseBody, bool safe)
	{
		CoinWApiStatus status;
		try
		{
			status = JsonConvert.DeserializeObject<CoinWApiStatus>(responseBody);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException($"CoinW {path} returned invalid JSON.", error);
		}
		if (status is null)
			throw new InvalidDataException($"CoinW {path} returned an empty response.");

		var isError = section == CoinWSections.Spot
			? !status.Code.EqualsIgnoreCase("200") || status.IsSuccess == false || status.IsFailed == true
			: !status.Code.EqualsIgnoreCase("0");
		if (isError)
			throw new InvalidOperationException($"CoinW {path} failed (code {status.Code}): {status.Message}. " +
				(safe ? "The request was read-only." : "The write was not retried; inspect exchange state before retrying."));
	}

	private static Exception CreateError(HttpStatusCode status, string path, string body, bool safe)
		=> new InvalidOperationException($"CoinW {path} returned HTTP {(int)status}: {body}. " +
			(safe ? "The read request failed." : "The write was not retried; inspect exchange state before retrying."));

	private static string CreateTransportError(string path, bool safe, string message)
		=> $"CoinW {path} transport error: {message}. " +
			(safe ? "The read request failed." : "The write may have reached CoinW; inspect exchange state before retrying.");

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is TimeSpan delay && delay > TimeSpan.Zero)
			return delay.Min(TimeSpan.FromSeconds(30));
		return TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
	}

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		return endpoint.TrimEnd('/') + "/";
	}
}
