namespace StockSharp.Nado.Native;

sealed class NadoRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 32 * 1024 * 1024;
	private const int _maximumReadAttempts = 4;
	private readonly Uri _gatewayV1;
	private readonly Uri _gatewayV2;
	private readonly Uri _archiveV1;
	private readonly Uri _archiveV2;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public NadoRestClient(string gatewayV1, string gatewayV2,
		string archiveV1, string archiveV2)
	{
		_gatewayV1 = CreateEndpoint(gatewayV1, nameof(gatewayV1));
		_gatewayV2 = CreateEndpoint(gatewayV2, nameof(gatewayV2));
		_archiveV1 = CreateEndpoint(archiveV1, nameof(archiveV1));
		_archiveV2 = CreateEndpoint(archiveV2, nameof(archiveV2));
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Nado-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
	}

	public override string Name => "Nado_REST";

	public ValueTask<string> GetStatusAsync(CancellationToken cancellationToken)
		=> QueryAsync<string, NadoStatusRequest>(new(), cancellationToken);

	public ValueTask<NadoContracts> GetContractsAsync(
		CancellationToken cancellationToken)
		=> QueryAsync<NadoContracts, NadoContractsRequest>(new(),
			cancellationToken);

	public async ValueTask<NadoPair[]> GetPairsAsync(
		CancellationToken cancellationToken)
		=> await SendAsync<NadoPair[], NadoEmptyRequest>(HttpMethod.Get,
			_gatewayV2, "pairs", null, true, cancellationToken) ?? [];

	public ValueTask<NadoAllProducts> GetProductsAsync(
		CancellationToken cancellationToken)
		=> QueryAsync<NadoAllProducts, NadoAllProductsRequest>(new(),
			cancellationToken);

	public ValueTask<NadoMarketPrices> GetMarketPricesAsync(int[] productIds,
		CancellationToken cancellationToken)
		=> QueryAsync<NadoMarketPrices, NadoMarketPricesRequest>(new()
		{
			ProductIds = productIds ?? [],
		}, cancellationToken);

	public ValueTask<NadoMarketLiquidity> GetMarketLiquidityAsync(int productId,
		int depth, CancellationToken cancellationToken)
		=> QueryAsync<NadoMarketLiquidity, NadoMarketLiquidityRequest>(new()
		{
			ProductId = productId,
			Depth = depth,
		}, cancellationToken);

	public ValueTask<NadoOrderBook> GetOrderBookAsync(string tickerId, int depth,
		CancellationToken cancellationToken)
	{
		if (depth is < 1 or > 500)
			throw new ArgumentOutOfRangeException(nameof(depth), depth,
				"Nado order-book depth must be between 1 and 500.");
		var path = "orderbook?ticker_id=" + Escape(tickerId) + "&depth=" +
			depth.ToString(CultureInfo.InvariantCulture);
		return SendAsync<NadoOrderBook, NadoEmptyRequest>(HttpMethod.Get,
			_gatewayV2, path, null, true, cancellationToken);
	}

	public async ValueTask<NadoPublicTrade[]> GetPublicTradesAsync(
		string tickerId, int limit, string maximumTradeId,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 500)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Nado trade limit must be between 1 and 500.");
		var path = new StringBuilder("trades?ticker_id=")
			.Append(Escape(tickerId)).Append("&limit=")
			.Append(limit.ToString(CultureInfo.InvariantCulture));
		if (!maximumTradeId.IsEmpty())
			path.Append("&max_trade_id=").Append(Escape(maximumTradeId));
		return await SendAsync<NadoPublicTrade[], NadoEmptyRequest>(HttpMethod.Get,
			_archiveV2, path.ToString(), null, true, cancellationToken) ?? [];
	}

	public ValueTask<NadoSubaccountInfo> GetSubaccountAsync(string subaccount,
		CancellationToken cancellationToken)
		=> QueryAsync<NadoSubaccountInfo, NadoSubaccountInfoRequest>(new()
		{
			Subaccount = subaccount.ThrowIfEmpty(nameof(subaccount)),
		}, cancellationToken);

	public ValueTask<NadoProductOrders> GetOrdersAsync(string subaccount,
		int[] productIds, CancellationToken cancellationToken)
		=> QueryAsync<NadoProductOrders, NadoOrdersRequest>(new()
		{
			Sender = subaccount.ThrowIfEmpty(nameof(subaccount)),
			ProductIds = productIds ?? [],
		}, cancellationToken);

	public ValueTask<NadoOrder> GetOrderAsync(int productId, string digest,
		CancellationToken cancellationToken)
		=> QueryAsync<NadoOrder, NadoOrderRequest>(new()
		{
			ProductId = productId,
			Digest = digest.ThrowIfEmpty(nameof(digest)),
		}, cancellationToken);

	public ValueTask<NadoFeeRates> GetFeeRatesAsync(string subaccount,
		CancellationToken cancellationToken)
		=> QueryAsync<NadoFeeRates, NadoFeeRatesRequest>(new()
		{
			Sender = subaccount.ThrowIfEmpty(nameof(subaccount)),
		}, cancellationToken);

	public ValueTask<NadoNonces> GetNoncesAsync(string address,
		CancellationToken cancellationToken)
		=> QueryAsync<NadoNonces, NadoNoncesRequest>(new()
		{
			Address = address.ThrowIfEmpty(nameof(address)),
		}, cancellationToken);

	public async ValueTask<NadoCandle[]> GetCandlesAsync(int productId,
		int granularity, int limit, DateTime? maximumTime,
		CancellationToken cancellationToken)
	{
		ValidateHistoryLimit(limit);
		var response = await SendAsync<NadoCandlesResponse, NadoCandlesRequest>(
			HttpMethod.Post, _archiveV1, "", new()
			{
				Candlesticks = new()
				{
					ProductId = productId,
					Granularity = granularity,
					Limit = limit,
					MaximumTime = maximumTime is DateTime time
						? (long)(time.EnsureNadoUtc() - DateTime.UnixEpoch)
							.TotalSeconds
						: null,
				},
			}, true, cancellationToken);
		return response?.Candlesticks ?? [];
	}

	public async ValueTask<NadoArchiveOrder[]> GetOrderHistoryAsync(
		string subaccount, int[] productIds, int limit, DateTime? maximumTime,
		string index, CancellationToken cancellationToken)
	{
		ValidateHistoryLimit(limit);
		var response = await SendAsync<NadoArchiveOrdersResponse,
			NadoArchiveOrdersRequest>(HttpMethod.Post, _archiveV1, "", new()
			{
				Orders = new()
				{
					Subaccounts = [subaccount.ThrowIfEmpty(nameof(subaccount))],
					ProductIds = productIds?.Length > 0 ? productIds : null,
					Limit = limit,
					MaximumTime = maximumTime is DateTime time
						? (long)(time.EnsureNadoUtc() - DateTime.UnixEpoch)
							.TotalSeconds
						: null,
					Index = index,
				},
			}, true, cancellationToken);
		return response?.Orders ?? [];
	}

	public async ValueTask<NadoArchiveMatchesResponse> GetMatchesAsync(
		string subaccount, int[] productIds, int limit, DateTime? maximumTime,
		string index, CancellationToken cancellationToken)
	{
		ValidateHistoryLimit(limit);
		return await SendAsync<NadoArchiveMatchesResponse,
			NadoArchiveMatchesRequest>(HttpMethod.Post, _archiveV1, "", new()
			{
				Matches = new()
				{
					Subaccounts = [subaccount.ThrowIfEmpty(nameof(subaccount))],
					ProductIds = productIds?.Length > 0 ? productIds : null,
					Limit = limit,
					MaximumTime = maximumTime is DateTime time
						? (long)(time.EnsureNadoUtc() - DateTime.UnixEpoch)
							.TotalSeconds
						: null,
					Index = index,
				},
			}, true, cancellationToken) ?? new();
	}

	public ValueTask<NadoPlacedOrder> PlaceOrderAsync(
		NadoPlaceOrderPayload payload, CancellationToken cancellationToken)
		=> ExecuteAsync<NadoPlacedOrder, NadoPlaceOrderRequest>(new()
		{
			PlaceOrder = payload ?? throw new ArgumentNullException(nameof(payload)),
		}, cancellationToken);

	public ValueTask<NadoCancelledOrders> CancelOrdersAsync(
		NadoCancelOrdersPayload payload, CancellationToken cancellationToken)
		=> ExecuteAsync<NadoCancelledOrders, NadoCancelOrdersRequest>(new()
		{
			CancelOrders = payload ?? throw new ArgumentNullException(nameof(payload)),
		}, cancellationToken);

	public ValueTask<NadoCancelledOrders> CancelProductOrdersAsync(
		NadoCancelProductOrdersPayload payload,
		CancellationToken cancellationToken)
		=> ExecuteAsync<NadoCancelledOrders, NadoCancelProductOrdersRequest>(new()
		{
			CancelProductOrders = payload ?? throw new ArgumentNullException(
				nameof(payload)),
		}, cancellationToken);

	public ValueTask<NadoPlacedOrder> CancelAndPlaceAsync(
		NadoCancelAndPlacePayload payload, CancellationToken cancellationToken)
		=> ExecuteAsync<NadoPlacedOrder, NadoCancelAndPlaceRequest>(new()
		{
			CancelAndPlace = payload ?? throw new ArgumentNullException(nameof(payload)),
		}, cancellationToken);

	private async ValueTask<TResponse> QueryAsync<TResponse, TRequest>(
		TRequest request, CancellationToken cancellationToken)
		where TRequest : class
	{
		var response = await SendAsync<NadoQueryResponse<TResponse>, TRequest>(
			HttpMethod.Post, _gatewayV1, "query", request, true,
			cancellationToken) ?? throw new InvalidDataException(
				"Nado returned an empty query response.");
		if (!response.Status.EqualsIgnoreCase("success") || response.Data is null)
			throw ApiError(response.Error, response.ErrorCode, "query");
		return response.Data;
	}

	private async ValueTask<TResponse> ExecuteAsync<TResponse, TRequest>(
		TRequest request, CancellationToken cancellationToken)
		where TRequest : class
	{
		var response = await SendAsync<NadoExecuteResponse<TResponse>, TRequest>(
			HttpMethod.Post, _gatewayV1, "execute", request, false,
			cancellationToken) ?? throw new InvalidDataException(
				"Nado returned an empty execute response.");
		if (!response.Status.EqualsIgnoreCase("success") || response.Data is null)
			throw ApiError(response.Error, response.ErrorCode, "execute");
		return response.Data;
	}

	private async ValueTask<TResponse> SendAsync<TResponse, TRequest>(
		HttpMethod method, Uri endpoint, string path, TRequest body, bool canRetry,
		CancellationToken cancellationToken)
		where TRequest : class
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			var requestUri = path.IsEmpty()
				? new Uri(endpoint.AbsoluteUri.TrimEnd('/'))
				: new Uri(endpoint, path.TrimStart('/'));
			using var request = new HttpRequestMessage(method, requestUri);
			if (body is not null)
			{
				var json = JsonConvert.SerializeObject(body, _jsonSettings);
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			}
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await ReadBodyAsync(response.Content,
				cancellationToken);
			if (canRetry && attempt + 1 < _maximumReadAttempts &&
				IsTransient(response.StatusCode))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromMilliseconds(250 * (1 << attempt));
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(5)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException(
					"Nado HTTP " + (int)response.StatusCode + " (" +
					response.StatusCode + "): " + Limit(responseBody, 2048), null,
					response.StatusCode);
			if (responseBody.IsEmpty())
				throw new InvalidDataException("Nado returned an empty HTTP response.");
			try
			{
				return JsonConvert.DeserializeObject<TResponse>(responseBody,
					_jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Nado returned malformed REST JSON.", error);
			}
		}
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(35);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static Uri CreateEndpoint(string endpoint, string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Nado REST endpoint must use HTTP or HTTPS.", parameterName);
		return uri;
	}

	private static Exception ApiError(string error, int? errorCode,
		string operation)
		=> new InvalidOperationException("Nado " + operation + " failed" +
			(errorCode is int code
				? " (" + code.ToString(CultureInfo.InvariantCulture) + ")"
				: string.Empty) + ": " + (error.IsEmpty() ? "unknown error" : error));

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static void ValidateHistoryLimit(int limit)
	{
		if (limit is < 1 or > 500)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Nado history limit must be between 1 and 500.");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Nado response exceeds the 32 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Nado response exceeds the 32 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}
}

sealed class NadoEmptyRequest
{
}

sealed class NadoStatusRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "status";
}
