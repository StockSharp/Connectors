namespace StockSharp.Coinigy.Native;

using System.Dynamic;
using System.Security.Cryptography;

class HttpClient : BaseLogReceiver
{
	private readonly SecureString _key;
	private readonly HashAlgorithm _hasher;

	private const string _baseUrl = "https://api.coinigy.com";

	public HttpClient(SecureString key, SecureString secret)
	{
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().ASCII());
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Coinigy) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
		=> MakeRequestAsync<IEnumerable<Symbol>>(CreateUrl("public/markets"), CreateRequest(Method.Get), cancellationToken);

	public Task<IEnumerable<Ohlc>> GetCandlesAsync(string baseCurr, string quoteCurr, string exchange, string resolution, DateTime start, DateTime end, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"private/exchanges/{exchange}/markets/{baseCurr}/{quoteCurr}/ohlc/{resolution}");
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("StartDate", start)
			.AddParameter("EndDate", end);

		return MakeRequestAsync<IEnumerable<Ohlc>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Account>> GetAccountsAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("private/user/accounts");
		var request = CreateRequest(Method.Get);
		return MakeRequestAsync<IEnumerable<Account>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Balance>> GetBalancesAsync(int authId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"private/user/accounts/{authId}/balances");
		var request = CreateRequest(Method.Get);
		return MakeRequestAsync<IEnumerable<Balance>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("private/user/orders");
		var request = CreateRequest(Method.Get);
		return MakeRequestAsync<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> GetOrderInfoAsync(long orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"private/user/orders/{orderId}");
		var request = CreateRequest(Method.Get);
		return MakeRequestAsync<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> RegisterOrderAsync(int authId, string type, string baseCurr, string quoteCurr, string side, decimal? price, decimal volume, bool? conditionalOperator, decimal? conditionalPrice, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"private/user/accounts/{authId}/orders/{type}");
		var request = CreateRequest(Method.Post);

		var body = (dynamic)new ExpandoObject();

		body.baseCurrCode = baseCurr;
		body.quoteCurrCode = quoteCurr;
		body.side = side;

		if (price != null)
			body.price = price.Value;

		body.quantity = volume;

		if (conditionalOperator != null)
			return body.conditionalOperator = conditionalOperator.Value ? "MoreThan" : "LessThan";

		if (conditionalPrice != null)
			body.conditionalPrice = conditionalPrice.Value;

		body.tradeType = "Exchange";

		return MakeRequestAsync<Order>(url, ApplySecret(request, url, (object)body), cancellationToken);
	}

	public Task CancelOrderAsync(int authId, long orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"private/user/accounts/{authId}/orders/{orderId}");
		var request = CreateRequest(Method.Delete);

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	private static Uri CreateUrl(string methodName, string version = "v2/")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/api/{version}{methodName}".To<Uri>();
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

		var timeStamp = (int)DateTime.UtcNow.ToUnix();

		var encodedArgs = $"{_key.UnSecure()}{timeStamp}{request.Method.ToString().ToUpperInvariant()}{url.ToString().Remove(_baseUrl)}";

		if (body != null)
		{
			var bodyStr = JsonConvert.SerializeObject(body, _serializerSettings);
			encodedArgs += bodyStr;

			request.AddBodyAsStr(bodyStr);
		}

		var signature = _hasher
			.ComputeHash(encodedArgs.ASCII())
			.Digest()
			.ToLowerInvariant();

		request
			.AddHeader("X-API-KEY", _key.UnSecure())
			.AddParameter("X-API-TIMESTAMP", timeStamp, ParameterType.HttpHeader)
			.AddHeader("X-API-SIGN", signature);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject)
		{
			if ((bool?)obj.success == false)
				throw new InvalidOperationException((string)obj.error);

			if (obj.result != null)
				obj = obj.result;
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}