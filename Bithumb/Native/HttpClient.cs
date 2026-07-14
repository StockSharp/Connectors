namespace StockSharp.Bithumb.Native;

using System.Security.Cryptography;

class TradePlaceData
{
	[JsonProperty("cont_id")]
	public long ContId { get; set; }

	[JsonProperty("units")]
	public decimal Units { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("total")]
	public decimal Total { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }
}

class HttpClient : BaseLogReceiver
{
	private readonly SecureString _key;
	private readonly SecureString _secret;
	private readonly HashAlgorithm _hasher;

	//private readonly UTCMlsIncrementalIdGenerator _nonceGen;

	private readonly string _baseUrl;

	public HttpClient(bool isPrime, SecureString key, SecureString secret)
	{
		_key = key;
		_secret = secret;
		_hasher = secret.IsEmpty() ? null : new HMACSHA512(secret.UnSecure().UTF8());

		//_nonceGen = new UTCMlsIncrementalIdGenerator();

		_baseUrl = "https://{0}.bithumb.com".Put(isPrime ? "prime" : "api");
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Bithumb) + "_" + nameof(HttpClient);

	public async Task<IDictionary<string, Ticker>> GetAllTickersAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("public/ticker/all");
		var request = CreateRequest(Method.Get);

		var token = await MakeRequestAsync<JObject>(url, request, cancellationToken);

		return token
		       .Properties()
		       .Where(p => p.Name != "date" && p.Value.Type != JTokenType.Array)
		       .ToDictionary(p => p.Name, p => p.Value.DeserializeObject<Ticker>());
	}

	public Task<Ticker> GetTickerAsync(string currency, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"public/ticker/{currency}");
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<Ticker>(url, request, cancellationToken);
	}

	public Task<OrderBook> GetOrderBookAsync(string currency, bool? groupOrders, int? count, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"public/orderbook/{currency}");
		var request = CreateRequest(Method.Get);

		if (groupOrders != null)
			request.AddParameter("group_orders", groupOrders.Value ? 1 : 0);

		if (count != null)
			request.AddParameter("count", count.Value);

		return MakeRequestAsync<OrderBook>(url, request, cancellationToken);
	}

	public async Task<IEnumerable<Transaction>> GetTransactionsAsync(string symbol, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"public/transaction_history/{symbol}");
		var request = CreateRequest(Method.Get);

		//if (fromId != null)
		//	request.AddParameter("cont_no", fromId.Value);

		//if (count != null)
		//	request.AddParameter("count", count.Value);

		return await MakeRequestAsync<IEnumerable<Transaction>>(url, request, cancellationToken) ?? [];
	}

	public Task<Account> GetAccountAsync(string currency, CancellationToken cancellationToken)
	{
		var url = CreateUrl("info/account");
		var request = CreateRequest(Method.Post);
		
		if (!currency.IsEmpty())
			request.AddParameter("currency", currency);

		return MakeRequestAsync<Account>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IDictionary<string, decimal>> GetBalanceAsync(string currency, CancellationToken cancellationToken)
	{
		var url = CreateUrl("info/balance");
		var request = CreateRequest(Method.Post).AddParameter("currency", currency);

		return MakeRequestAsync<IDictionary<string, decimal>>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<IEnumerable<UserTransaction>> GetUserTransactionsAsync(string currency, int searchGb, int? offset, int? count, CancellationToken cancellationToken)
	{
		var url = CreateUrl("info/user_transactions");

		var request = CreateRequest(Method.Post);

		if (offset != null)
			request.AddParameter("offset", offset.Value);

		if (count != null)
			request.AddParameter("count", count.Value);

		if (!currency.IsEmpty())
			request.AddParameter("currency", currency);

		request.AddParameter("searchGb", searchGb);

		return await MakeRequestAsync<IEnumerable<UserTransaction>>(url, ApplySecret(request, url), cancellationToken) ?? [];
	}

	public async Task<IEnumerable<Order>> GetOrdersAsync(string currency = default, Sides? side = default, int? count = default, long? after = default, long? orderId = default, CancellationToken cancellationToken = default)
	{
		var url = CreateUrl("info/orders");

		var request = CreateRequest(Method.Post);

		if (after != null)
			request.AddParameter("after", after.Value);

		if (side != null)
			request.AddParameter("type", side.Value.ToNative());

		if (!currency.IsEmpty())
			request.AddParameter("currency", currency);

		if (count != null)
			request.AddParameter("count", count.Value);

		if (orderId != null)
			request.AddParameter("order_id", orderId.Value);

		return await MakeRequestAsync<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken) ?? [];
	}

	public async Task<Tuple<long, IEnumerable<TradePlaceData>>> RegisterOrderAsync(string currency, Sides side, decimal? price, decimal volume, CancellationToken cancellationToken)
	{
		var url = CreateUrl(price == null ? (side == Sides.Buy ? "trade/market_buy" : "trade/market_sell") : "trade/place");
		
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("order_currency", currency)
			.AddParameter("units", volume)
			.AddParameter("type", side.ToNative());

		if (price != null)
			request.AddParameter("price", price.Value);

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken, false);

		return Tuple.Create((long)response.order_id, ((JToken)response.data).DeserializeObject<IEnumerable<TradePlaceData>>() ?? Enumerable.Empty<TradePlaceData>());
	}

	public Task CancelOrderAsync(Sides side, string currency, long orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl("trade/cancel");

		var request = CreateRequest(Method.Post);

		request
			.AddParameter("type", side.ToNative())
			.AddParameter("order_id", orderId)
			.AddParameter("currency", currency);
		
		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task WithdrawAsync(string currency, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		var name = currency.EqualsIgnoreCase("krw") ? "krw" : "btc";
		var url = CreateUrl($"trade/{name}_withdrawal".ToLowerInvariant());

		var request = CreateRequest(Method.Post);

		switch (info.Type)
		{
			case WithdrawTypes.BankWire:
			{
				request
					.AddParameter("bank", info.BankDetails.Name)
					.AddParameter("account", info.BankDetails.Account)
					.AddParameter("price", (int)volume);

				break;
			}

			case WithdrawTypes.Crypto:
			{
				request
					.AddParameter("units", volume)
					.AddParameter("address", info.CryptoAddress)
					.AddParameter("destination", info.PaymentId)
					.AddParameter("currency", currency);

				break;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));
		}
		
		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	private Uri CreateUrl(string methodName, string version = "")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{version}{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request, Uri uri)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var nonce = (long)DateTime.UtcNow.ToUnix(false);

		var endpoint = uri.ToString().Remove(_baseUrl);

		//request.AddParameter("endpoint", endpoint);

		//var encodedArgs = request
		//	.Parameters
		//	.Where(p => p.Type == ParameterType.GetOrPost && p.Value != null)
		//	.Select(p => $"{p.Name}={p.Value}")
		//	.ToQueryString();

		//encodedArgs = WebUtility.UrlEncode(encodedArgs)
		//			.Replace("+", "%20").Replace("%21", "!")
		//			.Replace("%27", "'").Replace("%28", "(")
		//			.Replace("%29", ")").Replace("%26", "&")
		//			.Replace("%3D", "=").Replace("%7E", "~");

		//var signature = _hasher
		//	.ComputeHash($"{endpoint};{encodedArgs};{nonce}".UTF8())
		//	.Digest()
		//	.ToLowerInvariant()
		//	.UTF8()
		//	.Base64();

		//request
		//	.AddHeader("api-client-type", "2")
		//	.AddHeader("Api-Key", _key.UnSecure())
		//	.AddHeader("Api-Sign", signature)
		//	.AddHeader("Api-Nonce", nonce.To<string>());

		request
			.AddParameter("apiKey", _key.UnSecure())
			.AddParameter("secretKey", _secret.UnSecure());

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken, bool onlyData = true)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (((JToken)obj).Type == JTokenType.Object && obj.status != null)
		{
			if ((int)obj.status != 0)
			{
				if ((int)obj.status == 5600 /* empty orders treats as error */ && typeof(T) == typeof(IEnumerable<Order>))
					return default;

				throw new InvalidOperationException((string)obj.message);
			}

			if (onlyData)
				return ((JToken)obj.data).DeserializeObject<T>();
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}