namespace StockSharp.Synthetix.Native;

sealed class SynthetixApiClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;

	private readonly Uri _infoEndpoint;
	private readonly Uri _tradeEndpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequest;
	private long _lastNonce;
	private long _serverTimeMilliseconds;

	public SynthetixApiClient(string infoEndpoint, string tradeEndpoint)
	{
		_infoEndpoint = CreateEndpoint(infoEndpoint, nameof(infoEndpoint));
		_tradeEndpoint = CreateEndpoint(tradeEndpoint, nameof(tradeEndpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Synthetix-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Synthetix_HTTP";

	public DateTime ServerTime
	{
		get
		{
			var value = Interlocked.Read(ref _serverTimeMilliseconds);
			return value > 0
				? value.FromSynthetixMilliseconds()
				: DateTime.UtcNow;
		}
	}

	public ValueTask<SynthetixMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> SendInfoAsync<SynthetixMarketsParameters, SynthetixMarket[]>(new()
		{
			IsActiveOnly = false,
		}, cancellationToken);

	public ValueTask<SynthetixMarketPrices> GetMarketPricesAsync(
		CancellationToken cancellationToken)
		=> SendInfoAsync<SynthetixActionParameters, SynthetixMarketPrices>(new()
		{
			Action = "getMarketPrices",
		}, cancellationToken);

	public ValueTask<SynthetixOrderBook> GetOrderBookAsync(string symbol,
		int limit, CancellationToken cancellationToken)
		=> SendInfoAsync<SynthetixSymbolParameters, SynthetixOrderBook>(new()
		{
			Action = "getOrderbook",
			Symbol = symbol.ThrowIfEmpty(nameof(symbol)),
			Limit = limit,
		}, cancellationToken);

	public ValueTask<SynthetixPublicTrades> GetLastTradesAsync(string symbol,
		int limit, CancellationToken cancellationToken)
		=> SendInfoAsync<SynthetixSymbolParameters, SynthetixPublicTrades>(new()
		{
			Action = "getLastTrades",
			Symbol = symbol.ThrowIfEmpty(nameof(symbol)),
			Limit = limit,
		}, cancellationToken);

	public ValueTask<SynthetixCandles> GetCandlesAsync(string symbol,
		string interval, int limit, DateTime? from, DateTime? to,
		CancellationToken cancellationToken)
		=> SendInfoAsync<SynthetixCandleParameters, SynthetixCandles>(new()
		{
			Symbol = symbol.ThrowIfEmpty(nameof(symbol)),
			Interval = interval.ThrowIfEmpty(nameof(interval)),
			Limit = limit,
			StartTime = from?.ToSynthetixMilliseconds(),
			EndTime = to?.ToSynthetixMilliseconds(),
		}, cancellationToken);

	public ValueTask<SynthetixSubAccount> GetSubAccountAsync(
		string subAccountId, SynthetixSigner signer,
		CancellationToken cancellationToken)
	{
		const string action = "getSubAccount";
		return SendQueryAsync<SynthetixSubAccountParameters,
			SynthetixSubAccount>(new()
		{
			Action = action,
			SubAccountId = subAccountId,
		}, subAccountId, action, signer, cancellationToken);
	}

	public ValueTask<SynthetixPosition[]> GetPositionsAsync(
		string subAccountId, string symbol, int limit,
		SynthetixSigner signer, CancellationToken cancellationToken)
	{
		const string action = "getPositions";
		return SendQueryAsync<SynthetixAccountPageParameters,
			SynthetixPosition[]>(new()
		{
			Action = action,
			SubAccountId = subAccountId,
			Symbol = symbol,
			Status = "open",
			Limit = limit,
			Offset = 0,
		}, subAccountId, action, signer, cancellationToken);
	}

	public ValueTask<SynthetixOrder[]> GetOpenOrdersAsync(
		string subAccountId, string symbol, int limit,
		SynthetixSigner signer, CancellationToken cancellationToken)
	{
		const string action = "getOpenOrders";
		return SendQueryAsync<SynthetixAccountPageParameters,
			SynthetixOrder[]>(new()
		{
			Action = action,
			SubAccountId = subAccountId,
			Symbol = symbol,
			Limit = limit,
			Offset = 0,
		}, subAccountId, action, signer, cancellationToken);
	}

	public ValueTask<SynthetixOrder[]> GetOrderHistoryAsync(
		string subAccountId, string symbol, DateTime? from, DateTime? to,
		int limit, SynthetixSigner signer,
		CancellationToken cancellationToken)
	{
		const string action = "getOrderHistory";
		return SendQueryAsync<SynthetixAccountPageParameters,
			SynthetixOrder[]>(new()
		{
			Action = action,
			SubAccountId = subAccountId,
			Symbol = symbol,
			StartTime = from?.ToSynthetixMilliseconds(),
			EndTime = to?.ToSynthetixMilliseconds(),
			Limit = limit,
			Offset = 0,
		}, subAccountId, action, signer, cancellationToken);
	}

	public ValueTask<SynthetixAccountTrades> GetTradesAsync(
		string subAccountId, string symbol, string orderId,
		DateTime? from, DateTime? to, int limit, SynthetixSigner signer,
		CancellationToken cancellationToken)
	{
		const string action = "getTrades";
		return SendQueryAsync<SynthetixAccountPageParameters,
			SynthetixAccountTrades>(new()
		{
			Action = action,
			SubAccountId = subAccountId,
			Symbol = symbol,
			OrderId = orderId,
			StartTime = from?.ToSynthetixMilliseconds(),
			EndTime = to?.ToSynthetixMilliseconds(),
			Limit = limit,
			Offset = 0,
		}, subAccountId, action, signer, cancellationToken);
	}

	public ValueTask<SynthetixOperationResponse> PlaceOrdersAsync(
		SynthetixPlaceOrderParameters parameters, SynthetixSigner signer,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		ArgumentNullException.ThrowIfNull(signer);
		var nonce = NextNonce();
		var expiry = NextExpiry();
		return SendTradeAsync<SynthetixPlaceOrderParameters,
			SynthetixOperationResponse>(new()
		{
			Parameters = parameters,
			Nonce = nonce,
			ExpiresAfter = expiry,
			Signature = signer.SignPlaceOrders(parameters.SubAccountId,
				parameters.Orders, parameters.Grouping, nonce, expiry),
		}, cancellationToken);
	}

	public ValueTask<SynthetixOperationResponse> ModifyOrderAsync(
		SynthetixModifyOrderParameters parameters, SynthetixSigner signer,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		ArgumentNullException.ThrowIfNull(signer);
		var nonce = NextNonce();
		var expiry = NextExpiry();
		return SendTradeAsync<SynthetixModifyOrderParameters,
			SynthetixOperationResponse>(new()
		{
			Parameters = parameters,
			Nonce = nonce,
			ExpiresAfter = expiry,
			Signature = signer.SignModifyOrder(parameters.SubAccountId,
				parameters.OrderId, parameters.Price, parameters.Quantity,
				parameters.TriggerPrice, nonce, expiry),
		}, cancellationToken);
	}

	public ValueTask<SynthetixOperationResponse> CancelOrdersAsync(
		SynthetixCancelOrdersParameters parameters, SynthetixSigner signer,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		ArgumentNullException.ThrowIfNull(signer);
		var nonce = NextNonce();
		var expiry = NextExpiry();
		return SendTradeAsync<SynthetixCancelOrdersParameters,
			SynthetixOperationResponse>(new()
		{
			Parameters = parameters,
			Nonce = nonce,
			ExpiresAfter = expiry,
			Signature = signer.SignCancelOrders(parameters.SubAccountId,
				parameters.OrderIds, nonce, expiry),
		}, cancellationToken);
	}

	public ValueTask<SynthetixCancelAllResult[]> CancelAllOrdersAsync(
		SynthetixCancelAllOrdersParameters parameters, SynthetixSigner signer,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		ArgumentNullException.ThrowIfNull(signer);
		var nonce = NextNonce();
		var expiry = NextExpiry();
		return SendTradeAsync<SynthetixCancelAllOrdersParameters,
			SynthetixCancelAllResult[]>(new()
		{
			Parameters = parameters,
			Nonce = nonce,
			ExpiresAfter = expiry,
			Signature = signer.SignCancelAllOrders(parameters.SubAccountId,
				parameters.Symbols, nonce, expiry),
		}, cancellationToken);
	}

	private ValueTask<TResponse> SendInfoAsync<TParameters, TResponse>(
		TParameters parameters, CancellationToken cancellationToken)
		=> SendAsync<TParameters, TResponse>(_infoEndpoint,
			new SynthetixInfoRequest<TParameters>
			{
				Parameters = parameters,
			}, cancellationToken);

	private ValueTask<TResponse> SendQueryAsync<TParameters, TResponse>(
		TParameters parameters, string subAccountId, string action,
		SynthetixSigner signer, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(signer);
		var expiry = NextExpiry();
		return SendTradeAsync<TParameters, TResponse>(new()
		{
			Parameters = parameters,
			ExpiresAfter = expiry,
			Signature = signer.SignSubAccountAction(subAccountId, action,
				expiry),
		}, cancellationToken);
	}

	private ValueTask<TResponse> SendTradeAsync<TParameters, TResponse>(
		SynthetixTradeRequest<TParameters> request,
		CancellationToken cancellationToken)
		=> SendAsync<TParameters, TResponse>(_tradeEndpoint, request,
			cancellationToken);

	private async ValueTask<TResponse> SendAsync<TParameters, TResponse>(
		Uri endpoint, object request, CancellationToken cancellationToken)
	{
		var content = JsonConvert.SerializeObject(request, _settings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
			{
				Content = new StringContent(content, Encoding.UTF8,
					"application/json"),
			};
			using var response = await _http.SendAsync(message,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (attempt < 3 && (response.StatusCode == (HttpStatusCode)429 ||
				(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)),
					cancellationToken);
				continue;
			}
			SynthetixApiResponse<TResponse> envelope;
			try
			{
				envelope = JsonConvert.DeserializeObject<
					SynthetixApiResponse<TResponse>>(body, _settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Synthetix returned an unexpected response shape.", error);
			}
			if (envelope?.Timestamp > 0)
				Interlocked.Exchange(ref _serverTimeMilliseconds,
					envelope.Timestamp);
			if (!response.IsSuccessStatusCode || envelope is null ||
				!envelope.Status.EqualsIgnoreCase("ok"))
			{
				var reason = envelope?.Error?.Message;
				var code = envelope?.Error?.Code;
				throw new InvalidOperationException("Synthetix HTTP " +
					(int)response.StatusCode + (code.IsEmpty() ? string.Empty :
						" " + code) + ": " + (reason.IsEmpty()
							? Truncate(body)
							: reason));
			}
			return envelope.Response is not null
				? envelope.Response
				: throw new InvalidDataException(
					"Synthetix returned no response payload.");
		}
	}

	private async ValueTask WaitForRequestAsync(
		CancellationToken cancellationToken)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequest - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private long NextNonce()
	{
		var candidate = DateTime.UtcNow.ToSynthetixMilliseconds();
		while (true)
		{
			var previous = Interlocked.Read(ref _lastNonce);
			var next = Math.Max(candidate, checked(previous + 1));
			if (Interlocked.CompareExchange(ref _lastNonce, next, previous) ==
				previous)
				return next;
		}
	}

	private static long NextExpiry()
		=> checked((long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds +
			60);

	private static Uri CreateEndpoint(string endpoint, string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Synthetix REST endpoint must use HTTP or HTTPS.",
				parameterName);
		return uri;
	}

	private static string Truncate(string value)
	{
		value = value?.Trim();
		return value.IsEmpty()
			? "request rejected"
			: value.Truncate(512, string.Empty);
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Synthetix response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var block = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(block, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Synthetix response exceeds the 16 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}
}
