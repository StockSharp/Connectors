namespace StockSharp.LBank.Native;

using System.Security.Cryptography;

class HttpClient(SecureString key, SecureString secret) : BaseLogReceiver
{
	private readonly SecureString _key = key;
	private readonly HashAlgorithm _hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
	private readonly HashAlgorithm _md5 = MD5.Create();

	private const string _baseUrl = "https://api.lbkex.com/v2";

	private readonly UTCMlsIncrementalIdGenerator _nonceGen = new();

    protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_md5.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(LBank) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Symbol>>(CreateUrl("accuracy.do"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandlesAsync(string symbol, string type, int size, long from, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("type", type)
			.AddParameter("size", size)
			.AddParameter("time", from.To<string>());

		return MakeRequestAsync<IEnumerable<Ohlc>>(CreateUrl("kline.do"), request, cancellationToken);
	}

	public Task<IEnumerable<Trade>> GetTradesAsync(string symbol, int size, long? from, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("size", size);

		if (from != null)
			request.AddParameter("time", from.Value);

		return MakeRequestAsync<IEnumerable<Trade>>(CreateUrl("trades.do"), request, cancellationToken);
	}

	public async Task<Tuple<IDictionary<string, double>, IDictionary<string, double>>> GetUserInfoAsync(CancellationToken cancellationToken)
	{
		dynamic response = await MakeRequestAsync<object>(CreateUrl("user_info.do"), ApplySecret(CreateRequest(Method.Post)), cancellationToken);

		var freeze = ((JToken)response.freeze).DeserializeObject<IDictionary<string, double>>();
		var free = ((JToken)response.free).DeserializeObject<IDictionary<string, double>>();

		return Tuple.Create(freeze, free);
	}

	public async Task<IEnumerable<Order>> GetOrdersAsync(string symbol, int page, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("current_page", page)
			.AddParameter("page_length", 200);

		dynamic response = await MakeRequestAsync<object>(CreateUrl("orders_info_no_deal.do"), ApplySecret(request), cancellationToken);

		return ((JToken)response.orders).DeserializeObject<IEnumerable<Order>>();
	}

	public async Task<string> RegisterOrderAsync(long transactionId, string symbol, string type, decimal? price, decimal volume, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("type", type);

		if (price != null)
			request.AddParameter("price", price.Value);

		request
			.AddParameter("amount", volume);

		dynamic response = await MakeRequestAsync<object>(CreateUrl("create_order.do"), ApplySecret(request), cancellationToken);

		return (string)response.order_id;
	}

	public Task CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("order_id", orderId);

		return MakeRequestAsync<object>(CreateUrl("cancel_order.do"), ApplySecret(request), cancellationToken);
	}

	public async Task<(long, decimal?)> WithdrawAsync(string symbol, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);

		request
			.AddParameter("assetCode", symbol)
			.AddParameter("account", info.CryptoAddress)
			.AddParameter("amount", volume)
			.AddParameter("memo", info.PaymentId)
			.AddParameter("mark", info.Comment);

		dynamic response = await MakeRequestAsync<object>(CreateUrl("withdraw.do"), ApplySecret(request), cancellationToken);

		return ((long)response.withdrawId, (decimal?)response.fee);
	}

	public Task<string> GetAuthKeyAsync(CancellationToken cancellationToken)
	{
		return MakeRequestAsync<string>(CreateUrl("subscribe/get_key.do"), ApplySecret(CreateRequest(Method.Post)), cancellationToken);
	}

	public Task RefreshAuthKeyAsync(string subscribeKey, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("subscribeKey", subscribeKey);

		return MakeRequestAsync<object>(CreateUrl("subscribe/refresh_key.do"), ApplySecret(request), cancellationToken);
	}

	public Task DestroyAuthKeyAsync(string subscribeKey, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("subscribeKey", subscribeKey);

		return MakeRequestAsync<object>(CreateUrl("subscribe/destroy_key.do"), ApplySecret(request), cancellationToken);
	}

	private static Uri CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		request.AddParameter("api_key", _key.UnSecure());

		var dict = new SortedDictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

		foreach (var parameter in request.Parameters)
		{
			dict.Add(parameter.Name, parameter.Value.ToString());
		}

		const string signMethod = "HmacSHA256";
		var timestamp = _nonceGen.GetNextId().To<string>();
		var echoStr = Guid.NewGuid().ToString().Remove("-");

		dict.Add("signature_method", signMethod);
		dict.Add("echostr", echoStr);
		dict.Add("timestamp", timestamp);

		var signature = _hasher
			.ComputeHash(_md5.ComputeHash(dict.ToQueryString().UTF8()).Digest().ToUpperInvariant().UTF8());

		request.AddParameter("sign", signature.Digest().ToLowerInvariant());

		request
			.AddHeader("signature_method", signMethod)
			.AddHeader("echostr", echoStr)
			.AddHeader("timestamp", timestamp);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject)
		{
			if (obj.result != null && (bool)obj.result == false)
				throw new InvalidOperationException(((int)obj.error_code).ToErrorText());

			if (obj.data != null)
				obj = obj.data;
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}