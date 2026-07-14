namespace StockSharp.Zaif.Native;

using System.Security.Cryptography;

class HttpClient(SecureString key, SecureString secret) : BaseLogReceiver
{
	private readonly SecureString _key = key;

	private readonly HashAlgorithm _hasher = secret.IsEmpty() ? null : new HMACSHA512(secret.UnSecure().UTF8());

	private const string _baseUrl = "https://api.zaif.jp/api";
	private static readonly Uri _privateUrl = "https://api.zaif.jp/tapi".To<Uri>();

	private readonly UTCIncrementalIdGenerator _nonceGen = new();

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Zaif) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Symbol>>(CreateUrl("currency_pairs/all"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Trade>> GetTradesAsync(string symbol, CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Trade>>(CreateUrl($"trades/{symbol}"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<Account> GetBalancesAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		return MakeRequestAsync<Account>(_privateUrl, ApplySecret(request, "get_info2"), cancellationToken);
	}

	public Task<IDictionary<int, Order>> GetActiveOrdersAsync(string symbol, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		if (!symbol.IsEmpty())
			request.AddParameter("currency_pair", symbol);

		return MakeRequestAsync<IDictionary<int, Order>>(_privateUrl, ApplySecret(request, "active_orders"), cancellationToken);
	}

	public Task<IDictionary<int, OwnTrade>> GetOwnTradesAsync(long? from, long? count,
		long? fromId, long? endId, bool? order, long? since,
		long? end, string symbol, bool? isToken, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		if (from != null)
			request.AddParameter("from", from.Value);

		if (count != null)
			request.AddParameter("count", count.Value);

		if (fromId != null)
			request.AddParameter("from_id", fromId.Value);

		if (endId != null)
			request.AddParameter("end_id", endId.Value);

		if (order != null)
			request.AddParameter("order", order.Value ? "ASC" : "DESC");

		if (since != null)
			request.AddParameter("since", since.Value);

		if (end != null)
			request.AddParameter("end", end.Value);

		if (!symbol.IsEmpty())
			request.AddParameter("currency_pair", symbol);

		if (isToken != null)
			request.AddParameter("is_token", isToken.Value);

		return MakeRequestAsync<IDictionary<int, OwnTrade>>(_privateUrl, ApplySecret(request, "trade_history"), cancellationToken);
	}

	public async Task<long> RegisterOrderAsync(string symbol, string side, decimal? price, decimal volume, decimal? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("currency_pair", symbol)
			.AddParameter("action", side)
			.AddParameter("amount", volume);

		if (price != null)
			request.AddParameter("price", price.Value);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		request.AddParameter("amount", volume);

		dynamic response = await MakeRequestAsync<object>(_privateUrl, ApplySecret(request, "trade"), cancellationToken);

		return (long)response.order_id;
	}

	public Task CancelOrderAsync(long orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request.AddParameter("order_id", orderId);

		return MakeRequestAsync<object>(_privateUrl, ApplySecret(request, "cancel_order"), cancellationToken);
	}

	public Task WithdrawAsync(string symbol, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);

		request
			.AddParameter("currency", symbol)
			.AddParameter("address", info.CryptoAddress);

		if (!info.Comment.IsEmpty())
			request.AddParameter("message", info.Comment);

		request.AddParameter("amount", volume);

		if (info.ChargeFee != null)
			request.AddParameter("opt_fee", info.ChargeFee.Value);

		return MakeRequestAsync<object>(_privateUrl, ApplySecret(request, "withdraw"), cancellationToken);
	}

	private static Uri CreateUrl(string methodName, string version = "1/")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{version}{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request, string method)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		request
			.AddParameter("nonce", _nonceGen.GetNextId())
			.AddParameter("method", method);

		var encodedArgs = request
			.Parameters
			.Where(p => p.Type == ParameterType.GetOrPost && p.Value != null)
			.ToQueryString();

		var signature = _hasher
			.ComputeHash(encodedArgs.UTF8())
			.Digest()
			.ToLowerInvariant();

		request
			.AddHeader("key", _key.UnSecure())
			.AddHeader("sign", signature);

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