namespace StockSharp.Gopax.Native;

using System.Dynamic;
using System.Security.Cryptography;

class HttpClient : BaseLogReceiver
{
	private readonly SecureString _key;
	private readonly HashAlgorithm _hasher;

	private readonly string _baseUrl = "https://api.{0}";

	private readonly UTCMlsIncrementalIdGenerator _nonceGen;

	public HttpClient(SecureString key, SecureString secret, string domain)
	{
		_key = key;
		_baseUrl = _baseUrl.Put(domain);

		_hasher = secret.IsEmpty() ? null : new HMACSHA512(secret.UnSecure().Base64());

		_nonceGen = new();
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Gopax) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Symbol>>(CreateUrl("trading-pairs"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken)
	{
		return MakeRequestAsync<Ticker>(CreateUrl($"trading-pairs/{symbol}/stats"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Ticker>> GetTickersAsync(CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Ticker>>(CreateUrl($"trading-pairs/stats"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<OrderBook> GetOrderBookAsync(string symbol, CancellationToken cancellationToken)
	{
		return MakeRequestAsync<OrderBook>(CreateUrl($"trading-pairs/{symbol}/book"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Trade>> GetTradesAsync(string symbol, int limit, long after, CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Trade>>(CreateUrl($"trading-pairs/{symbol}/trades?limit={limit}&after={after}"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandlesAsync(string symbol, int interval, long from, long to, CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Ohlc>>(CreateUrl($"trading-pairs/{symbol}/candles?start={from}&end={to}&interval={interval}"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Balance>> GetBalancesAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("balances");

		return MakeRequestAsync<IEnumerable<Balance>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOrdersAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("orders");

		return MakeRequestAsync<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> GetOrderInfoAsync(long id, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl($"orders/{id}");

		return MakeRequestAsync<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<Order> RegisterOrderAsync(string symbol, string type, string side, decimal? price, decimal volume, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		dynamic body = new ExpandoObject();

		body.tradingPairName = symbol;
		body.type = type;
		body.side = side;
		body.amount = volume;

		if (price != null)
			body.price = price.Value;

		var url = CreateUrl("orders");

		return await MakeRequestAsync<Order>(url, ApplySecret(request, url, (object)body), cancellationToken);
	}

	public async Task CancelOrderAsync(long orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Delete);
		var url = CreateUrl($"orders/{orderId}");

		await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<OwnTrade>> GetOwnTradesAsync(int? limit, long? pastMax, long? latestMin, long? after, long? before, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		if (pastMax != null)
			request.AddParameter("pastmax", pastMax.Value);

		if (latestMin != null)
			request.AddParameter("latestmin", latestMin.Value);

		if (after != null)
			request.AddParameter("after", after.Value);

		if (before != null)
			request.AddParameter("before", before.Value);

		var url = CreateUrl("trades");

		return MakeRequestAsync<IEnumerable<OwnTrade>>(url, ApplySecret(request, url), cancellationToken);
	}

	private Uri CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private static readonly JsonSerializerSettings _serializerSettings = JsonHelper.CreateJsonSerializerSettings();

	private RestRequest ApplySecret(RestRequest request, Uri url, object body = null)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var nonce = _nonceGen.GetNextId().To<string>();
		var bodyStr = string.Empty;

		if (body != null)
		{
			bodyStr = JsonConvert.SerializeObject(body, _serializerSettings);
			request.AddBodyAsStr(bodyStr);
		}

		var signature = nonce + request.Method.ToString().ToUpperInvariant() + url.PathAndQuery + bodyStr;

		signature = _hasher
			.ComputeHash(signature.UTF8())
			.Base64();

		request
			.AddHeader("API-KEY", _key.UnSecure())
			.AddHeader("SIGNATURE", signature)
			.AddHeader("NONCE", nonce);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject)
		{
			if (obj.success == 0)
				throw new InvalidOperationException((string)obj.error);

			if (obj.@return != null)
				obj = obj.@return;
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}