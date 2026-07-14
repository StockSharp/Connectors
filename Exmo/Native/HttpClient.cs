namespace StockSharp.Exmo.Native;

using System.Security.Cryptography;

class HttpClient(SecureString key, SecureString secret) : BaseLogReceiver
{
	private readonly SecureString _key = key;

	private readonly HashAlgorithm _hasher = secret.IsEmpty() ? null : new HMACSHA512(secret.UnSecure().UTF8());

	//private volatile int _nonce;
	private const string _baseUrl = " https://api.exmo.com";

	private readonly UTCIncrementalIdGenerator _nonceGen = new();

    protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Exmo) + "_" + nameof(HttpClient);

	public Task<IDictionary<string, IEnumerable<Trade>>> GetTradesAsync(IEnumerable<string> pairs, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddQueryParameter("pair", pairs.JoinComma());

		return MakeRequestAsync<IDictionary<string, IEnumerable<Trade>>>(CreateUrl("trades"), request, cancellationToken);
	}

	public Task<IDictionary<string, OrderBook>> GetOrderBooksAsync(IEnumerable<string> pairs, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request.AddQueryParameter("pair", pairs.JoinComma());

		if (limit != null)
			request.AddParameter("limit", limit.Value, ParameterType.QueryString);

		return MakeRequestAsync<IDictionary<string, OrderBook>>(CreateUrl("order_book"), request, cancellationToken);
	}

	public Task<IDictionary<string, Ticker>> GetTickersAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<IDictionary<string, Ticker>>(CreateUrl("ticker"), request, cancellationToken);
	}

	public Task<IDictionary<string, Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<IDictionary<string, Symbol>>(CreateUrl("pair_settings"), request, cancellationToken);
	}

	public Task<IEnumerable<string>> GetCurrenciesAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<IEnumerable<string>>(CreateUrl("currency"), request, cancellationToken);
	}

	public async Task<(IDictionary<string, decimal>, IDictionary<string, decimal>)> GetBalancesAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		var info = await MakeRequestAsync<UserInfo>(CreateUrl("user_info"), ApplySecret(request), cancellationToken);

		return (BalanceToDict(info.Balances), BalanceToDict(info.Reserved));
	}

	private static IDictionary<string, decimal> BalanceToDict(JObject token)
	{
		return token.Properties().ToDictionary(p => p.Name, p => (decimal)p.Value);
	}

	public Task<IDictionary<string, IEnumerable<Order>>> GetOpenOrdersAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		return MakeRequestAsync<IDictionary<string, IEnumerable<Order>>>(CreateUrl("user_open_orders"), ApplySecret(request), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetCancelledOrdersAsync(int? offset, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		if (offset != null)
			request.AddParameter("offset", offset.Value);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		return MakeRequestAsync<IEnumerable<Order>>(CreateUrl("user_cancelled_orders"), ApplySecret(request), cancellationToken);
	}

	public Task<IDictionary<string, IEnumerable<Trade>>> GetUserTradesAsync(IEnumerable<string> pairs, int? offset, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request.AddParameter("pair", pairs.JoinComma());

		if (offset != null)
			request.AddParameter("offset", offset.Value);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		return MakeRequestAsync<IDictionary<string, IEnumerable<Trade>>>(CreateUrl("user_trades"), ApplySecret(request), cancellationToken);
	}

	public async Task<IEnumerable<Trade>> GetOrderTradesAsync(long orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request.AddParameter("order_id", orderId);

		dynamic response = await MakeRequestAsync<object>(CreateUrl("order_trades"), ApplySecret(request), cancellationToken);

		return ((JToken)response.trades).DeserializeObject<IEnumerable<Trade>>();
	}

	public async Task<long> RegisterOrderAsync(string pair, string type, decimal price, decimal volume, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("pair", pair)
			.AddParameter("quantity", volume)
			.AddParameter("price", price)
			.AddParameter("type", type);

		dynamic response = await MakeRequestAsync<object>(CreateUrl("order_create"), ApplySecret(request), cancellationToken);

		return (long)response.order_id;
	}

	public Task CancelOrderAsync(long orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request.AddParameter("order_id", orderId);

		return MakeRequestAsync<object>(CreateUrl("order_cancel"), ApplySecret(request), cancellationToken);
	}

	public async Task<long> WithdrawAsync(string currency, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);

		request
			.AddParameter("amount", volume)
			.AddParameter("currency", currency)
			.AddParameter("address", info.CryptoAddress);

		if (!info.PaymentId.IsEmpty())
			request.AddParameter("invoice", info.PaymentId);

		dynamic response = await MakeRequestAsync<object>(CreateUrl("withdraw_crypt"), ApplySecret(request), cancellationToken);
		
		return (long)response.task_id;
	}

	private static Uri CreateUrl(string methodName, string version = "v1/")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{version}{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		request.AddParameter("nonce", _nonceGen.GetNextId());

		var encodedArgs = request
			.Parameters
			.Where(p => p.Type == ParameterType.GetOrPost && p.Value != null)
			.ToQueryString();

		var signature = _hasher
			.ComputeHash(encodedArgs.UTF8())
			.Digest()
			.ToLowerInvariant();

		request
			.AddHeader("Key", _key.UnSecure())
			.AddHeader("Sign", signature);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (((JToken)obj).Type == JTokenType.Object && (obj.result != null && (bool)obj.result == false))
			throw new InvalidOperationException((string)obj.error);

		return ((JToken)obj).DeserializeObject<T>();
	}
}