namespace StockSharp.Poloniex.Native;

sealed class PoloniexRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;

	private readonly Uri _endpoint;
	private readonly System.Net.Http.HttpClient _http;
	private readonly Authenticator _authenticator;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};

	public PoloniexRestClient(string endpoint, Authenticator authenticator)
	{
		var value = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') + "/";

		if (!Uri.TryCreate(value, UriKind.Absolute, out _endpoint) ||
			!_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException("Poloniex REST endpoint must be an absolute HTTPS URI.", nameof(endpoint));

		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		_http = new System.Net.Http.HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Poloniex-Connector/1.0");
	}

	public override string Name => nameof(Poloniex) + "_Rest";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<PoloniexMarket[]> GetMarketsAsync(CancellationToken cancellationToken)
		=> SendAsync<PoloniexMarket[]>(HttpMethod.Get, "markets", [], null, false, true, cancellationToken);

	public async ValueTask<IDictionary<string, PoloniexCurrency>> GetCurrenciesAsync(
		CancellationToken cancellationToken)
	{
		var rows = await SendAsync<Dictionary<string, PoloniexCurrency>[]>(
			HttpMethod.Get, "currencies", [], null, false, true, cancellationToken) ?? [];

		return rows
			.SelectMany(static row => row)
			.GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(static group => group.Key, static group => group.Last().Value,
				StringComparer.OrdinalIgnoreCase);
	}

	public ValueTask<PoloniexTicker[]> GetTickersAsync(CancellationToken cancellationToken)
		=> SendAsync<PoloniexTicker[]>(HttpMethod.Get, "markets/ticker24h", [], null, false, true,
			cancellationToken);

	public async ValueTask<PoloniexPublicTrade[]> GetTradeHistoryAsync(string symbol,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var trades = await SendAsync<PoloniexPublicTrade[]>(HttpMethod.Get,
			$"markets/{EscapePath(symbol)}/trades",
			[new("limit", "1000")], null, false, true, cancellationToken) ?? [];

		var fromMilliseconds = from is DateTime fromValue ? (long?)fromValue.ToUnix(false) : null;
		var toMilliseconds = to is DateTime toValue ? (long?)toValue.ToUnix(false) : null;

		return
		[
			.. trades.Where(trade =>
				(fromMilliseconds is null || trade.CreateTime >= fromMilliseconds) &&
				(toMilliseconds is null || trade.CreateTime <= toMilliseconds)),
		];
	}

	public ValueTask<PoloniexCandle[]> GetCandlesAsync(string symbol, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var query = new List<KeyValuePair<string, string>>
		{
			new("interval", timeFrame.ToPoloniexInterval()),
			new("limit", "500"),
		};

		if (from is DateTime fromTime)
			query.Add(new("startTime",
				((long)fromTime.ToUnix(false)).ToString(CultureInfo.InvariantCulture)));

		if (to is DateTime toTime)
			query.Add(new("endTime",
				((long)toTime.ToUnix(false)).ToString(CultureInfo.InvariantCulture)));

		return SendAsync<PoloniexCandle[]>(HttpMethod.Get,
			$"markets/{EscapePath(symbol)}/candles", query, null, false, true, cancellationToken);
	}

	public ValueTask<PoloniexOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var query = symbol.IsEmpty()
			? Array.Empty<KeyValuePair<string, string>>()
			: new[] { new KeyValuePair<string, string>("symbol", symbol) };

		return SendAsync<PoloniexOrder[]>(HttpMethod.Get, "orders", query, null, true, true,
			cancellationToken);
	}

	public ValueTask<PoloniexOwnTrade[]> GetOrderTradesAsync(long orderId,
		CancellationToken cancellationToken)
		=> SendAsync<PoloniexOwnTrade[]>(HttpMethod.Get,
			$"orders/{orderId.ToString(CultureInfo.InvariantCulture)}/trades", [], null, true, true,
			cancellationToken);

	public async ValueTask<long> NewOrderAsync(long transactionId, string symbol, Sides side,
		OrderTypes orderType, decimal price, decimal volume, TimeInForce? timeInForce, bool? postOnly,
		CancellationToken cancellationToken)
	{
		var isMarket = orderType == OrderTypes.Market;

		if (isMarket && side == Sides.Buy)
			throw new NotSupportedException(
				"Poloniex market buys require a quote-currency amount; StockSharp order volume is expressed in base units.");

		var request = new PoloniexOrderRequest
		{
			Symbol = symbol,
			Side = side.ToNative(),
			Type = postOnly == true ? "LIMIT_MAKER" : isMarket ? "MARKET" : "LIMIT",
			TimeInForce = isMarket || postOnly == true ? null : timeInForce.ToPoloniex(),
			Price = isMarket ? null : price,
			Quantity = volume,
			ClientOrderId = transactionId.ToString(CultureInfo.InvariantCulture),
		};

		var result = await SendAsync<PoloniexOrderResult>(HttpMethod.Post, "orders", [], request,
			true, false, cancellationToken);

		return result?.Id ??
			throw new InvalidDataException("Poloniex returned an empty create-order response.");
	}

	public ValueTask CancelOrderByClientIdAsync(long transactionId,
		CancellationToken cancellationToken)
		=> SendWithoutResultAsync(HttpMethod.Delete,
			$"orders/cid:{transactionId.ToString(CultureInfo.InvariantCulture)}", null,
			cancellationToken);

	public ValueTask CancelOrderByIdAsync(long orderId, CancellationToken cancellationToken)
		=> SendWithoutResultAsync(HttpMethod.Delete,
			$"orders/{orderId.ToString(CultureInfo.InvariantCulture)}", null,
			cancellationToken);

	public ValueTask CancelAllOrdersAsync(string symbol, CancellationToken cancellationToken)
		=> SendWithoutResultAsync(HttpMethod.Delete, "orders", new PoloniexCancelAllRequest
		{
			Symbols = symbol.IsEmpty() ? null : [symbol],
		}, cancellationToken);

	public async ValueTask<long> ReplaceOrderAsync(long transactionId, long orderId, decimal price,
		decimal? volume, TimeInForce? timeInForce, bool? postOnly,
		CancellationToken cancellationToken)
	{
		var result = await SendAsync<PoloniexReplaceOrderResult>(HttpMethod.Put,
			$"orders/{orderId.ToString(CultureInfo.InvariantCulture)}", [],
			new PoloniexReplaceOrderRequest
			{
				ClientOrderId = transactionId.ToString(CultureInfo.InvariantCulture),
				Price = price,
				Quantity = volume,
				Type = postOnly == true ? "LIMIT_MAKER" : "LIMIT",
				TimeInForce = postOnly == true ? "GTC" : timeInForce.ToPoloniex(),
			}, true, false, cancellationToken);

		return result?.Id ??
			throw new InvalidDataException("Poloniex returned an empty replace-order response.");
	}

	public ValueTask<PoloniexAccountBalances[]> GetBalancesAsync(CancellationToken cancellationToken)
		=> SendAsync<PoloniexAccountBalances[]>(HttpMethod.Get, "accounts/balances", [], null, true,
			true, cancellationToken);

	public async ValueTask<long> WithdrawAsync(string currency, decimal amount, WithdrawInfo info,
		CancellationToken cancellationToken)
	{
		if (info is null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var result = await SendAsync<PoloniexWithdrawResult>(HttpMethod.Post, "wallets/withdraw", [],
			new PoloniexWithdrawRequest
			{
				Currency = currency,
				Amount = amount,
				Address = info.CryptoAddress,
				PaymentId = info.PaymentId,
			}, true, false, cancellationToken);

		return result?.Id ??
			throw new InvalidDataException("Poloniex returned an empty withdrawal response.");
	}

	private async ValueTask SendWithoutResultAsync(HttpMethod method, string path, object body,
		CancellationToken cancellationToken)
	{
		_ = await SendAsync<object>(method, path, [], body, true, false, cancellationToken);
	}

	private async ValueTask<T> SendAsync<T>(HttpMethod method, string path,
		IEnumerable<KeyValuePair<string, string>> query, object body, bool authenticated, bool safe,
		CancellationToken cancellationToken)
	{
		path = "/" + path.Trim('/');
		var queryParameters = query.ToArray();
		var queryString = BuildQuery(queryParameters);
		var bodyJson = body is null ? null : JsonConvert.SerializeObject(body, _jsonSettings);
		var target = path.TrimStart('/') + (queryString.IsEmpty() ? string.Empty : "?" + queryString);

		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(method, new Uri(_endpoint, target));

			if (bodyJson is not null)
				request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

			if (authenticated)
				AddAuthentication(request, method, path, queryParameters, bodyJson);

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
			}
			catch (HttpRequestException error) when (safe && attempt < _maximumReadAttempts)
			{
				this.AddWarningLog("Poloniex {0} transport error. Retrying safe request: {1}",
					target, error.Message);
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}

			using (response)
			{
				var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

				if (safe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500) && attempt < _maximumReadAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}

				if (!response.IsSuccessStatusCode)
					throw CreateHttpError(response.StatusCode, target, responseBody, safe);

				if (responseBody.IsEmpty())
					return default;

				ThrowIfApiError(target, responseBody, safe);

				try
				{
					return JsonConvert.DeserializeObject<T>(responseBody, _jsonSettings);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						$"Poloniex {target} returned an unexpected response shape.", error);
				}
			}
		}
	}

	private void AddAuthentication(HttpRequestMessage request, HttpMethod method, string path,
		IEnumerable<KeyValuePair<string, string>> query, string body)
	{
		if (!_authenticator.CanSign)
			throw new InvalidOperationException(
				"Poloniex API key and secret are required for private requests.");

		var timestamp = _authenticator.GetTimestamp();
		var timestampText = timestamp.ToString(CultureInfo.InvariantCulture);
		string parameters;

		if (body is not null)
			parameters = $"requestBody={body}&signTimestamp={timestampText}";
		else
			parameters = BuildQuery(query.Append(new("signTimestamp", timestampText)));

		var payload = $"{method.Method.ToUpperInvariant()}\n{path}\n{parameters}";

		request.Headers.TryAddWithoutValidation("key", _authenticator.Key.UnSecure());
		request.Headers.TryAddWithoutValidation("signatureMethod", "HmacSHA256");
		request.Headers.TryAddWithoutValidation("signatureVersion", "2");
		request.Headers.TryAddWithoutValidation("signTimestamp", timestampText);
		request.Headers.TryAddWithoutValidation("signature", _authenticator.Sign(payload));
	}

	private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> query)
		=> query
			.Where(static pair => !pair.Key.IsEmpty() && pair.Value is not null)
			.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
			.Select(static pair => Escape(pair.Key) + "=" + Escape(pair.Value))
			.Join("&");

	private static void ThrowIfApiError(string target, string responseBody, bool safe)
	{
		if (!responseBody.AsSpan().TrimStart().StartsWith("{"))
			return;

		PoloniexApiError error;
		try
		{
			error = JsonConvert.DeserializeObject<PoloniexApiError>(responseBody);
		}
		catch (JsonException exception)
		{
			throw new InvalidDataException($"Poloniex {target} returned malformed JSON.", exception);
		}

		if (error?.Code is not int code || code is 0 or 200)
			return;

		throw new InvalidOperationException(
			$"Poloniex {target} failed ({code}): {error.Message}. " +
			(safe ? "The request was read-only." :
				"The write was not retried; inspect exchange state before retrying."));
	}

	private static Exception CreateHttpError(HttpStatusCode statusCode, string target, string body,
		bool safe)
	{
		string message;
		try
		{
			message = JsonConvert.DeserializeObject<PoloniexApiError>(body)?.Message;
		}
		catch (JsonException)
		{
			message = null;
		}

		message = message.IsEmpty() ? body?.Trim() : message;
		if (message?.Length > 512)
			message = message[..512];

		return new HttpRequestException(
			$"Poloniex {target} returned HTTP {(int)statusCode}: {message}. " +
			(safe ? "The read request failed." :
				"The write was not retried; inspect exchange state before retrying."),
			null, statusCode);
	}

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
		=> response?.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * (1 << (attempt - 1)));

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));
}
