namespace StockSharp.AscendEx.Native;

sealed class AscendExRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;

	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly AscendExAuthenticator _authenticator;
	private readonly int _accountGroup;
	private readonly AscendExSpotAccountTypes _spotAccountType;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public AscendExRestClient(string endpoint,
		SecureString key, SecureString secret,
		int accountGroup,
		AscendExSpotAccountTypes spotAccountType)
	{
		_endpoint = new Uri(
			endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') + "/",
			UriKind.Absolute);
		_authenticator = new(key, secret);
		_accountGroup = accountGroup.Max(0);
		_spotAccountType = spotAccountType;
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-AscendEX-Connector/1.0");
	}

	public override string Name => "ASCENDEX_REST";

	public bool IsCredentialsAvailable => _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<AscendExSymbol[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
	{
		var spot = await SendAsync<AscendExSpotProduct[]>(
			HttpMethod.Get,
			$"api/pro/v1/{SpotCategory}/products",
			[], null, false, null, cancellationToken);
		var futures = await SendAsync<AscendExFuturesContract[]>(
			HttpMethod.Get, "api/pro/v2/futures/contract",
			[], null, false, null, cancellationToken);
		return
		[
			.. (spot ?? []).Cast<AscendExSymbol>(),
			.. (futures ?? []).Cast<AscendExSymbol>(),
		];
	}

	public async ValueTask<AscendExTicker> GetTickerAsync(
		string symbol, CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var path = IsFutures(symbol)
			? "api/pro/v2/futures/ticker"
			: "api/pro/v1/spot/ticker";
		var raw = await SendRawAsync(
			HttpMethod.Get, path,
			Values(("symbol", symbol)), null, false, null,
			cancellationToken);
		var data = ParseData(raw);
		var ticker = data.Type == JTokenType.Array
			? data.ToObject<AscendExTicker[]>(
				JsonSerializer.Create(_jsonSettings))?
				.FirstOrDefault()
			: data.ToObject<AscendExTicker>(
				JsonSerializer.Create(_jsonSettings));
		if (ticker is not null)
			ticker.Pair = ticker.Pair.IsEmpty(symbol);
		return ticker;
	}

	public async ValueTask<AscendExOrderBook> GetOrderBookAsync(
		string symbol, int depth, CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var envelope = await SendAsync<
			AscendExMarketEnvelope<AscendExOrderBook>>(
			HttpMethod.Get, "api/pro/v1/depth",
			Values(("symbol", symbol)), null, false, null,
			cancellationToken);
		if (envelope?.Data is not { } result)
			return null;
		result.Pair = envelope.Symbol.IsEmpty(symbol);
		result.Limit = depth.Max(1).Min(500);
		return result;
	}

	public async ValueTask<AscendExTrade[]> GetPublicTradesAsync(
		string symbol, CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var envelope = await SendAsync<
			AscendExMarketEnvelope<AscendExTrade[]>>(
			HttpMethod.Get, "api/pro/v1/trades",
			Values(("symbol", symbol), ("n", 100)),
			null, false, null, cancellationToken);
		foreach (var trade in envelope?.Data ?? [])
			trade.Pair = envelope.Symbol.IsEmpty(symbol);
		return envelope?.Data;
	}

	public async ValueTask<AscendExBar[]> GetCandlesAsync(
		string symbol, string resolution, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		_ = resolution.ToAscendExTimeFrame();
		var envelopes = await SendAsync<AscendExBarEnvelope[]>(
			HttpMethod.Get, "api/pro/v1/barhist",
			Values(
				("symbol", symbol),
				("interval", resolution),
				("from", from.ToUtc().ToAscendExMilliseconds()),
				("to", to.ToUtc().ToAscendExMilliseconds()),
				("n", 500)),
			null, false, null, cancellationToken);
		return [.. (envelopes ?? [])
			.Where(item =>
				item?.Data is not null &&
				item.Symbol.EqualsIgnoreCase(symbol))
			.Select(static item => item.Data)];
	}

	public async ValueTask<AscendExBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
	{
		var balances = new List<AscendExBalance>();
		balances.AddRange(await SendAsync<AscendExBalance[]>(
			HttpMethod.Get,
			PrivatePath($"api/pro/v1/{SpotCategory}/balance"),
			[], null, true, "balance", cancellationToken) ?? []);

		var futures = await SendAsync<AscendExFuturesAccount>(
			HttpMethod.Get,
			PrivatePath("api/pro/v2/futures/position"),
			[], null, true, "v2/futures/position",
			cancellationToken);
		balances.AddRange((futures?.Collaterals ?? [])
			.Where(static item => !item.Asset.IsEmpty())
			.Select(static item => new AscendExBalance
			{
				Currency = item.Asset,
				Amount = item.Balance,
				Available = item.Available == 0
					? item.Balance
					: item.Available,
			}));
		balances.AddRange((futures?.Positions ?? [])
			.Where(static item =>
				!item.Symbol.IsEmpty() && item.Position != 0)
			.Select(static item => new AscendExBalance
			{
				Currency = item.Symbol,
				SecurityCode = item.Symbol,
				Amount = item.Position,
				Available = item.Position,
				IsPosition = true,
			}));
		return [.. balances];
	}

	public ValueTask<AscendExOrder[]> GetOpenOrdersAsync(
		string symbol, CancellationToken cancellationToken)
	{
		symbol = NormalizeOptionalSymbol(symbol);
		var futures = IsFutures(symbol);
		var path = futures
			? "api/pro/v2/futures/order/open"
			: $"api/pro/v1/{SpotCategory}/order/open";
		var signaturePath = futures
			? "v2/futures/order/open"
			: "order/open";
		return SendAsync<AscendExOrder[]>(
			HttpMethod.Get, PrivatePath(path),
			Values(("symbol", symbol)), null, true,
			signaturePath, cancellationToken);
	}

	public async ValueTask<AscendExOrder> GetOrderAsync(
		string symbol, string orderId,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var futures = IsFutures(symbol);
		var path = futures
			? "api/pro/v2/futures/order/status"
			: $"api/pro/v1/{SpotCategory}/order/status";
		var signaturePath = futures
			? "v2/futures/order/status"
			: "order/status";
		var raw = await SendRawAsync(
			HttpMethod.Get, PrivatePath(path),
			Values(("orderId",
				orderId.ThrowIfEmpty(nameof(orderId)))),
			null, true, signaturePath, cancellationToken);
		var data = ParseData(raw);
		var order = data.Type == JTokenType.Array
			? data.ToObject<AscendExOrder[]>(
				JsonSerializer.Create(_jsonSettings))?
				.FirstOrDefault()
			: data.ToObject<AscendExOrder>(
				JsonSerializer.Create(_jsonSettings));
		if (order is not null)
			order.Pair = order.Pair.IsEmpty(symbol);
		return order;
	}

	public ValueTask<AscendExOrder[]> GetOrdersAsync(
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		_ = from;
		_ = to;
		symbol = NormalizeSymbol(symbol);
		var futures = IsFutures(symbol);
		var path = futures
			? "api/pro/v2/futures/order/hist/current"
			: $"api/pro/v1/{SpotCategory}/order/hist/current";
		var signaturePath = futures
			? "v2/futures/order/hist/current"
			: "order/hist/current";
		return SendAsync<AscendExOrder[]>(
			HttpMethod.Get, PrivatePath(path),
			Values(
				("symbol", symbol),
				("n", limit.Max(1).Min(500))),
			null, true, signaturePath, cancellationToken);
	}

	public async ValueTask<AscendExPrivateTrade[]>
		GetPrivateTradesAsync(
			string symbol, DateTime? from, DateTime? to, int limit,
			CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var orders = await GetOrdersAsync(
			symbol, from, to, limit, cancellationToken);
		return [.. (orders ?? [])
			.Where(order =>
			{
				var time = order.Timestamp > 0
					? order.Timestamp.FromAscendExMilliseconds()
					: DateTime.MinValue;
				return order.ExecutedAmount > 0 &&
					(from is null || time >= from.Value.ToUtc()) &&
					(to is null || time <= to.Value.ToUtc());
			})
			.Select(static order => new AscendExPrivateTrade
			{
				TradeId = order.Id + "-" +
					order.Timestamp.ToString(
						CultureInfo.InvariantCulture),
				OrderId = order.Id,
				Pair = order.Pair,
				Action = order.Action,
				Price = order.AveragePrice > 0
					? order.AveragePrice
					: order.Price,
				BaseAmount = order.ExecutedAmount,
				QuoteAmount = order.ExecutedAmount *
					(order.AveragePrice > 0
						? order.AveragePrice
						: order.Price),
				Fee = order.Fee,
				FeeSymbol = order.FeeAsset,
				CreatedTimestamp = order.Timestamp,
			})];
	}

	public async ValueTask<AscendExPlaceOrderResult> PlaceOrderAsync(
		string symbol, AscendExPlaceOrderRequest order,
		CancellationToken cancellationToken)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		symbol = NormalizeSymbol(symbol);
		var futures = IsFutures(symbol);
		var path = futures
			? "api/pro/v2/futures/order"
			: $"api/pro/v1/{SpotCategory}/order";
		var signaturePath = futures
			? "v2/futures/order"
			: "order";
		var body = JObject.FromObject(
			order, JsonSerializer.Create(_jsonSettings));
		body["symbol"] = symbol;
		var ack = await SendAsync<AscendExOrderAck>(
			HttpMethod.Post, PrivatePath(path), [],
			body, true, signaturePath, cancellationToken);
		return new() { OrderId = ack?.Info?.OrderId };
	}

	public async ValueTask CancelOrderAsync(
		string symbol, string orderId,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		var futures = IsFutures(symbol);
		var path = futures
			? "api/pro/v2/futures/order"
			: $"api/pro/v1/{SpotCategory}/order";
		var signaturePath = futures
			? "v2/futures/order"
			: "order";
		_ = await SendAsync<AscendExOrderAck>(
			HttpMethod.Delete, PrivatePath(path), [],
			new
			{
				id = AscendExExtensions.CreateClientId(
					DateTime.UtcNow.Ticks),
				orderId = orderId.ThrowIfEmpty(nameof(orderId)),
				symbol,
				time = DateTime.UtcNow.ToAscendExMilliseconds(),
			},
			true, signaturePath, cancellationToken);
	}

	public async ValueTask CancelAllOrdersAsync(
		string symbol, bool isFutures,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeOptionalSymbol(symbol);
		if (!symbol.IsEmpty() && IsFutures(symbol) != isFutures)
			throw new ArgumentException(
				"AscendEX market category does not match the symbol.",
				nameof(isFutures));
		var path = isFutures
			? "api/pro/v2/futures/order/all"
			: $"api/pro/v1/{SpotCategory}/order/all";
		var signaturePath = isFutures
			? "v2/futures/order/all"
			: "order/all";
		_ = await SendAsync<AscendExOrderAck>(
			HttpMethod.Delete, PrivatePath(path), [],
			new
			{
				symbol,
				time = DateTime.UtcNow.ToAscendExMilliseconds(),
			},
			true, signaturePath, cancellationToken);
	}

	internal static int NormalizeDepth(int depth)
		=> depth.Max(1).Min(500);

	internal static TData Deserialize<TData>(string body)
	{
		try
		{
			var response = JsonConvert.DeserializeObject<
				AscendExResponse<TData>>(
				body.ThrowIfEmpty(nameof(body)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}) ?? throw new InvalidDataException(
					"AscendEX returned an empty response.");
			EnsureSuccess(
				response.Code, response.Message, response.Reason);
			return response.Data;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"AscendEX returned malformed JSON.", error);
		}
	}

	internal static string SerializeBody(object value)
		=> JsonConvert.SerializeObject(
			value ?? throw new ArgumentNullException(nameof(value)),
			new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.None,
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None,
				Culture = CultureInfo.InvariantCulture,
			});

	private async ValueTask<TData> SendAsync<TData>(
		HttpMethod method, string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body, bool isPrivate, string signaturePath,
		CancellationToken cancellationToken)
	{
		var raw = await SendRawAsync(
			method, path, query, body, isPrivate,
			signaturePath, cancellationToken);
		return Deserialize<TData>(raw);
	}

	private async ValueTask<string> SendRawAsync(
		HttpMethod method, string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body, bool isPrivate, string signaturePath,
		CancellationToken cancellationToken)
	{
		if (isPrivate && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"AscendEX API key and secret are required " +
					"for private operations.");
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		var queryString = BuildQuery(query);
		var target = path +
			(queryString.IsEmpty() ? string.Empty : "?" + queryString);
		var bodyText = body is null ? null : SerializeBody(body);

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(
				method, new Uri(_endpoint, target));
			if (bodyText is not null)
				request.Content = new StringContent(
					bodyText, Encoding.UTF8, "application/json");
			if (isPrivate)
				AddAuthentication(request,
					signaturePath.ThrowIfEmpty(nameof(signaturePath)));

			using var response = await _http.SendAsync(
				request, HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var responseBody = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (response.IsSuccessStatusCode)
				return responseBody;
			if (attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(
					response.StatusCode, responseBody);
			await DelayRetryAsync(
				response, attempt, cancellationToken);
		}
	}

	private void AddAuthentication(
		HttpRequestMessage request, string apiPath)
	{
		var timestamp = DateTime.UtcNow.ToAscendExMilliseconds();
		request.Headers.TryAddWithoutValidation(
			"x-auth-key", _authenticator.Key);
		request.Headers.TryAddWithoutValidation(
			"x-auth-timestamp",
			timestamp.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation(
			"x-auth-signature",
			_authenticator.Sign(timestamp, apiPath));
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(50);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private string PrivatePath(string path)
		=> _accountGroup.ToString(CultureInfo.InvariantCulture) +
			"/" + path.TrimStart('/');

	private string SpotCategory
		=> _spotAccountType == AscendExSpotAccountTypes.Margin
			? "margin"
			: "cash";

	private static JToken ParseData(string body)
	{
		try
		{
			var root = JObject.Parse(body);
			EnsureSuccess(
				root.Value<int?>("code") ?? 0,
				root.Value<string>("message"),
				root.Value<string>("reason"));
			return root["data"] ?? JValue.CreateNull();
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"AscendEX returned malformed JSON.", error);
		}
	}

	private static (string Name, object Value)[] Values(
		params (string Name, object Value)[] values)
		=> [.. (values ?? [])
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)];

	private static string BuildQuery(
		IEnumerable<(string Name, object Value)> values)
		=> (values ?? [])
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)
			.Select(static value =>
				Uri.EscapeDataString(value.Name) + "=" +
				Uri.EscapeDataString(Convert.ToString(
					value.Value, CultureInfo.InvariantCulture)))
			.Join("&");

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.ToAscendExSecurityCode();

	private static string NormalizeOptionalSymbol(string symbol)
		=> symbol.IsEmpty() ? null : NormalizeSymbol(symbol);

	private static bool IsFutures(string symbol)
		=> symbol?.EndsWith(
			"-PERP", StringComparison.OrdinalIgnoreCase) == true;

	private static void EnsureSuccess(
		int code, string message, string reason)
	{
		if (code == 0)
			return;
		throw new InvalidOperationException(
			$"AscendEX API error {code}: " +
			new[] { reason, message }
				.Where(static value => !value.IsEmpty())
				.Join(": "));
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 ||
			(int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(
		HttpResponseMessage response, int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateHttpError(
		HttpStatusCode statusCode, string body)
	{
		var details = body?.Trim();
		try
		{
			var error = JsonConvert.DeserializeObject<
				AscendExError>(body);
			if (error is not null)
				details = new[]
				{
					error.Code.ToString(
						CultureInfo.InvariantCulture),
					error.Reason,
					error.Message,
				}
				.Where(static value => !value.IsEmpty())
				.Join(": ");
		}
		catch (JsonException)
		{
		}
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"AscendEX HTTP {(int)statusCode} ({statusCode}): " +
				details,
			null, statusCode);
	}
}
