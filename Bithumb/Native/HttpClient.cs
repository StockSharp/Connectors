namespace StockSharp.Bithumb.Native;

using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

class HttpClient : BaseLogReceiver
{
	private static readonly KeyValuePair<string, string>[] _noParameters = [];

	private readonly SecureString _key;
	private readonly SecureString _secret;
	private readonly Uri _baseUri;
	private readonly global::System.Net.Http.HttpClient _http = new()
	{
		Timeout = TimeSpan.FromSeconds(60),
	};
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTimeOffset,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};

	public HttpClient(string baseUrl, SecureString key, SecureString secret)
	{
		_key = key;
		_secret = secret;
		_baseUri = new Uri(baseUrl.ThrowIfEmpty(nameof(baseUrl)).TrimEnd('/') + "/", UriKind.Absolute);

		if (!_baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("Bithumb REST endpoint must use HTTPS.", nameof(baseUrl));

		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Bithumb");
	}

	public override string Name => nameof(Bithumb) + "_" + nameof(HttpClient);

	public Task<Symbol[]> GetSymbolsAsync(CancellationToken cancellationToken)
		=> SendPublicAsync<Symbol[]>(HttpMethod.Get, "v1/market/all",
			[Pair("isDetails", "true")], cancellationToken);

	public Task<Transaction[]> GetTransactionsAsync(string symbol, CancellationToken cancellationToken)
		=> SendPublicAsync<Transaction[]>(HttpMethod.Get, "v1/trades/ticks",
			[Pair("market", symbol), Pair("count", "200")], cancellationToken);

	public Task<Balance[]> GetBalancesAsync(CancellationToken cancellationToken)
		=> SendPrivateAsync<Balance[]>(HttpMethod.Get, "v1/accounts", _noParameters, null,
			cancellationToken);

	public async Task<Order[]> GetPendingOrdersAsync(CancellationToken cancellationToken)
	{
		var result = new List<Order>();
		string nextKey = null;

		do
		{
			var parameters = new List<KeyValuePair<string, string>>
			{
				Pair("state", "wait"),
				Pair("limit", "100"),
				Pair("order_by", "asc"),
			};

			if (!nextKey.IsEmpty())
				parameters.Add(Pair("next_key", nextKey));

			var page = await SendPrivateAsync<OrdersPage>(HttpMethod.Get, "v2/orders/pending",
				parameters, null, cancellationToken);

			if (page.Data != null)
				result.AddRange(page.Data);

			nextKey = page.HasNext ? page.NextKey : null;
		}
		while (!nextKey.IsEmpty());

		return [.. result];
	}

	public Task<Order[]> GetOrdersAsync(string[] orderIds, CancellationToken cancellationToken)
	{
		if (orderIds is null)
			throw new ArgumentNullException(nameof(orderIds));
		if (orderIds.Length == 0)
			return Task.FromResult(Array.Empty<Order>());
		if (orderIds.Length > 100)
			throw new ArgumentOutOfRangeException(nameof(orderIds), orderIds.Length,
				"Bithumb accepts at most 100 order identifiers per request.");

		var request = new SearchOrdersRequest { OrderIds = orderIds };
		var parameters = orderIds.Select(id => Pair("order_ids[]", id)).ToArray();

		return SendPrivateAsync<Order[]>(HttpMethod.Post, "v2/orders/search", parameters, request,
			cancellationToken);
	}

	public async Task<string> RegisterOrderAsync(string symbol, Sides side, decimal? price,
		decimal volume, string clientOrderId, CancellationToken cancellationToken)
	{
		if (price is null && side == Sides.Buy)
			throw new NotSupportedException(
				"Bithumb market buy orders require a quote-currency amount.");

		var request = new RegisterOrderRequest
		{
			Market = symbol,
			Side = side.ToNative(),
			OrderType = price is null ? "market" : "limit",
			Price = price?.ToString(CultureInfo.InvariantCulture),
			Volume = volume.ToString(CultureInfo.InvariantCulture),
			ClientOrderId = clientOrderId,
		};

		var parameters = new List<KeyValuePair<string, string>>
		{
			Pair("market", request.Market),
			Pair("side", request.Side),
			Pair("order_type", request.OrderType),
		};

		if (!request.Price.IsEmpty())
			parameters.Add(Pair("price", request.Price));

		parameters.Add(Pair("volume", request.Volume));

		if (!request.ClientOrderId.IsEmpty())
			parameters.Add(Pair("client_order_id", request.ClientOrderId));

		var response = await SendPrivateAsync<RegisterOrderResponse>(HttpMethod.Post, "v2/orders",
			parameters, request, cancellationToken);

		return response.OrderId.ThrowIfEmpty(nameof(response.OrderId));
	}

	public Task CancelOrderAsync(string orderId, CancellationToken cancellationToken)
	{
		orderId.ThrowIfEmpty(nameof(orderId));

		return SendPrivateAsync<CancelOrderResponse>(HttpMethod.Delete, "v2/order",
			[Pair("order_id", orderId)], null, cancellationToken);
	}

	public async Task<string> WithdrawAsync(string currency, decimal volume, WithdrawInfo info,
		CancellationToken cancellationToken)
	{
		if (info is null)
			throw new ArgumentNullException(nameof(info));
		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = new WithdrawRequest
		{
			Currency = currency.ToUpperInvariant(),
			Network = currency.ToUpperInvariant(),
			Amount = volume.ToString(CultureInfo.InvariantCulture),
			Address = info.CryptoAddress.ThrowIfEmpty(nameof(info.CryptoAddress)),
			SecondaryAddress = info.PaymentId,
		};

		var parameters = new List<KeyValuePair<string, string>>
		{
			Pair("currency", request.Currency),
			Pair("net_type", request.Network),
			Pair("amount", request.Amount),
			Pair("address", request.Address),
		};

		if (!request.SecondaryAddress.IsEmpty())
			parameters.Add(Pair("secondary_address", request.SecondaryAddress));

		var response = await SendPrivateAsync<WithdrawResponse>(HttpMethod.Post, "v1/withdraws/coin",
			parameters, request, cancellationToken);

		return response.Id.ThrowIfEmpty(nameof(response.Id));
	}

	private Task<T> SendPublicAsync<T>(HttpMethod method, string path,
		IReadOnlyCollection<KeyValuePair<string, string>> parameters,
		CancellationToken cancellationToken)
		=> SendAsync<T>(method, path, parameters, null, false, cancellationToken);

	private Task<T> SendPrivateAsync<T>(HttpMethod method, string path,
		IReadOnlyCollection<KeyValuePair<string, string>> parameters, object body,
		CancellationToken cancellationToken)
		=> SendAsync<T>(method, path, parameters, body, true, cancellationToken);

	private async Task<T> SendAsync<T>(HttpMethod method, string path,
		IReadOnlyCollection<KeyValuePair<string, string>> parameters, object body,
		bool authenticated, CancellationToken cancellationToken)
	{
		var query = body is null ? BuildParameters(parameters, true) : string.Empty;
		var uri = new Uri(_baseUri, path + (query.IsEmpty() ? string.Empty : "?" + query));

		using var request = new HttpRequestMessage(method, uri);

		if (body != null)
		{
			request.Content = new StringContent(
				JsonConvert.SerializeObject(body, Formatting.None, _jsonSettings),
				Encoding.UTF8, "application/json");
		}

		if (authenticated)
		{
			if (_key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);
			if (_secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);

			var token = CreateToken(BuildParameters(parameters, false));
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);

		this.AddVerboseLog("Bithumb {0} {1} -> {2}.", method, uri,
			(int)response.StatusCode);

		if (!response.IsSuccessStatusCode)
		{
			ApiError error = null;

			try
			{
				error = JsonConvert.DeserializeObject<ApiErrorResponse>(content, _jsonSettings)?.Error;
			}
			catch (JsonException)
			{
			}

			var message = error is null
				? content.IsEmpty(response.ReasonPhrase)
				: $"{error.Name}: {error.Message}";

			throw new HttpRequestException(
				$"Bithumb HTTP {(int)response.StatusCode}: {message}.",
				null, response.StatusCode);
		}

		return JsonConvert.DeserializeObject<T>(content, _jsonSettings)
			?? throw new InvalidOperationException("Bithumb returned an empty JSON response.");
	}

	private string CreateToken(string parameters)
	{
		var payload = new JwtPayload
		{
			AccessKey = _key.UnSecure(),
			Nonce = Guid.NewGuid().ToString("D"),
			Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
		};

		if (!parameters.IsEmpty())
		{
			payload.QueryHash = Convert.ToHexString(
				SHA512.HashData(Encoding.UTF8.GetBytes(parameters))).ToLowerInvariant();
			payload.QueryHashAlgorithm = "SHA512";
		}

		var header = Base64UrlEncode(JsonConvert.SerializeObject(new JwtHeader(),
			Formatting.None, _jsonSettings));
		var claims = Base64UrlEncode(JsonConvert.SerializeObject(payload,
			Formatting.None, _jsonSettings));
		var unsignedToken = header + "." + claims;

		using var signer = new HMACSHA256(Encoding.UTF8.GetBytes(_secret.UnSecure()));
		var signature = signer.ComputeHash(Encoding.ASCII.GetBytes(unsignedToken));

		return unsignedToken + "." + Base64UrlEncode(signature);
	}

	private static string BuildParameters(
		IReadOnlyCollection<KeyValuePair<string, string>> parameters, bool encode)
		=> parameters.Count == 0
			? string.Empty
			: string.Join("&", parameters.Select(pair =>
				(encode ? Uri.EscapeDataString(pair.Key) : pair.Key) + "=" +
				(encode ? Uri.EscapeDataString(pair.Value) : pair.Value)));

	private static KeyValuePair<string, string> Pair(string name, string value)
		=> new(name, value);

	private static string Base64UrlEncode(string value)
		=> Base64UrlEncode(Encoding.UTF8.GetBytes(value));

	private static string Base64UrlEncode(byte[] value)
		=> Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
