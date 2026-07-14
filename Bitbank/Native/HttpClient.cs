namespace StockSharp.Bitbank.Native;

using System.Dynamic;
using System.Security;
using System.Security.Cryptography;

class HttpClient : BaseLogReceiver
{
	private readonly SecureString _key;

	private readonly HashAlgorithm _hasher;

	private const string _publicBaseUrl = "https://public.bitbank.cc";
	private const string _privateBaseUrl = "https://api.bitbank.cc/v1";

	private readonly UTCMlsIncrementalIdGenerator _nonceGen;

	public HttpClient(SecureString key, SecureString secret)
	{
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());

		_nonceGen = new();
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Bitbank) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Ticker>> GetTickersAsync(CancellationToken cancellationToken)
		=> MakeRequestAsync<IEnumerable<Ticker>>(CreateUrl(true, "tickers"), CreateRequest(Method.Get), cancellationToken);

	public async Task<IEnumerable<Trade>> GetTransactionsAsync(string symbol, DateTime? date, CancellationToken cancellationToken)
	{
		try
		{
			dynamic response = await MakeRequestAsync<object>(CreateUrl(true, $"{symbol}/transactions/{date:yyyyMMdd}"), CreateRequest(Method.Get), cancellationToken);

			return ((JToken)response.transactions).DeserializeObject<IEnumerable<Trade>>();
		}
		catch (InvalidOperationException e)
		{
			if (e.Message == "{\"success\":0,\"data\":{\"code\":10000}}")
				return [];

			throw;
		}
	}

	public async Task<IEnumerable<Ohlc>> GetCandlesAsync(string symbol, string type, DateTime date, CancellationToken cancellationToken)
	{
		var response = await MakeRequestAsync<OhlcResponse>(CreateUrl(true, $"{symbol}/candlestick/{type}/{date:yyyyMMdd}"), CreateRequest(Method.Get), cancellationToken);

		if (response.Items == null || response.Items.Length == 0)
			return [];

		return response.Items[0].Candles;
	}

	public async Task<IEnumerable<Balance>> GetBalancesAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl(false, "user/assets");

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return ((JToken)response.assets).DeserializeObject<IEnumerable<Balance>>();
	}

	public async Task<IEnumerable<Account>> GetAccountsAsync(string asset, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl(false, "user/withdrawal_account");

		request.AddParameter("asset", asset);

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return ((JToken)response.accounts).DeserializeObject<IEnumerable<Account>>();
	}

	public async Task<IEnumerable<Order>> GetActiveOrdersAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl(false, "user/spot/active_orders");

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return ((JToken)response.orders).DeserializeObject<IEnumerable<Order>>();
	}

	public Task<Order> GetOrderAsync(string symbol, long orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl(false, "user/spot/order");

		request
			.AddParameter("pair", symbol)
			.AddParameter("order_id", orderId);

		return MakeRequestAsync<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<IEnumerable<Order>> GetOrdersAsync(string symbol, IEnumerable<long> orderIds, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);
		var url = CreateUrl(false, "user/spot/orders_info");

		var body = new
		{
			pair = symbol,
			order_ids = orderIds.ToArray(),
		};

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url, body), cancellationToken);

		return ((JToken)response.orders).DeserializeObject<IEnumerable<Order>>();
	}

	public async Task<IEnumerable<OwnTrade>> GetOwnTradesAsync(string symbol = null, int? count = null, long? orderId = null, long? since = null, long? end = null, string order = null, CancellationToken cancellationToken = default)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl(false, "user/spot/trade_history");

		if (!symbol.IsEmpty())
			request.AddParameter("pair", symbol);

		if (count != null)
			request.AddParameter("count", count.Value);

		if (orderId != null)
			request.AddParameter("order_id", orderId.Value);

		if (since != null)
			request.AddParameter("since", since.Value);

		if (end != null)
			request.AddParameter("end", end.Value);

		if (!order.IsEmpty())
			request.AddParameter("order", order);

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return ((JToken)response.trades).DeserializeObject<IEnumerable<OwnTrade>>();
	}

	public Task<Order> RegisterOrderAsync(string symbol, string side, string type, decimal? price, decimal volume, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);
		var url = CreateUrl(false, "user/spot/order");

		var body = new
		{
			pair = symbol,
			amount = volume,
			price,
			side,
			type,
		};

		return MakeRequestAsync<Order>(url, ApplySecret(request, url, body), cancellationToken);
	}

	public Task<Order> CancelOrderAsync(string symbol, long orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);
		var url = CreateUrl(false, "user/spot/cancel_order");

		var body = new
		{
			pair = symbol,
			order_id = orderId,
		};

		return MakeRequestAsync<Order>(url, ApplySecret(request, url, body), cancellationToken);
	}

	public async Task<string> WithdrawAsync(string symbol, string account, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);
		var url = CreateUrl(false, "user/request_withdrawal");

		dynamic body = new ExpandoObject();

		body.asset = symbol;
		body.uuid = account;
		body.amount = volume;

		// sms or two step auth

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url, (object)body), cancellationToken);

		return (string)response.uuid;
	}

	private static Uri CreateUrl(bool isPublic, string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		var baseUrl = isPublic ? _publicBaseUrl : _privateBaseUrl;
		return $"{baseUrl}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request, Uri url, object body = null)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var data = url.ToString().Remove("https://api.bitbank.cc");

		if (request.Method == Method.Get)
		{
			var encodedArgs = request
				.Parameters
				.Where(p => p.Type == ParameterType.GetOrPost && p.Value != null)
				.ToQueryString();

			if (!encodedArgs.IsEmpty())
				data += "?" + encodedArgs;
		}
		else
		{
			if (body == null)
				throw new ArgumentNullException(nameof(body));

			data = JsonConvert.SerializeObject(body);

			request.AddBodyAsStr(data);
		}

		var nonce = _nonceGen.GetNextId();

		var signature = _hasher
		    .ComputeHash($"{nonce}{data}".UTF8())
		    .Digest()
		    .ToLowerInvariant();

		request
			.AddHeader("ACCESS-KEY", _key.UnSecure())
			.AddParameter("ACCESS-NONCE", nonce, ParameterType.HttpHeader)
			.AddHeader("ACCESS-SIGNATURE", signature);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject)
		{
			if (obj.success == 0)
				throw new InvalidOperationException(((string)obj.data?.code).ToErrorText());

			if (obj.data != null)
				obj = obj.data;
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}