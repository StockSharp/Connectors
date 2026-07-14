namespace StockSharp.Upbit.Native;

using System.Security.Cryptography;

using JWT.Algorithms;
using JWT.Builder;

class HttpClient(SecureString key, SecureString secret) : BaseLogReceiver
{
	private readonly SecureString _key = key;
	private readonly SecureString _secret = secret;

	private readonly HashAlgorithm _hasher = secret.IsEmpty() ? null : SHA512.Create();

	private const string _baseUrl = "https://api.upbit.com";
	private const string _version = "v1";

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Upbit) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Symbol>>(CreateUrl("market/all"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandlesAsync(string symbol, string unit, string resolution, string to, int count, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		request
			.AddQueryParameter("market", symbol);

		if (!to.IsEmpty())
			request.AddQueryParameter("to", to);

		request.AddQueryParameter("count", count.To<string>());

		return MakeRequestAsync<IEnumerable<Ohlc>>(CreateUrl($"candles/{unit}/{resolution}"), request, cancellationToken);
	}

	public Task<IEnumerable<Balance>> GetBalancesAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<IEnumerable<Balance>>(CreateUrl("accounts"), ApplySecret(request), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOpenOrdersAsync(string state, string[] uuids, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (!state.IsEmpty())
			request.AddQueryParameter("state", state);

		if (uuids.Length > 0)
		{
			foreach (var uuid in uuids)
			{
				request.AddQueryParameter("uuids[]=", uuid);
			}
		}

		return MakeRequestAsync<IEnumerable<Order>>(CreateUrl("orders"), ApplySecret(request), cancellationToken);
	}

	public async Task<string> RegisterOrderAsync(string symbol, string side, decimal? price, decimal volume, string identifier, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("market", symbol)
			.AddParameter("side", side)
			.AddParameter("volume", volume);

		if (price != null)
			request.AddParameter("price", price.Value);

		if (!identifier.IsEmpty())
			request.AddParameter("identifier", identifier);

		dynamic response = await MakeRequestAsync<object>(CreateUrl("orders"), ApplySecret(request), cancellationToken);

		return (string)response.uuid;
	}

	public Task CancelOrderAsync(string orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Delete);

		request.AddParameter("uuid", orderId);

		return MakeRequestAsync<object>(CreateUrl("order"), ApplySecret(request), cancellationToken);
	}

	public async Task<string> WithdrawAsync(string currency, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);

		request
			.AddParameter("currency", currency)
			.AddParameter("amount", volume)
			.AddParameter("address", info.CryptoAddress);

		if (!info.Comment.IsEmpty())
			request.AddParameter("secondary_address", info.Comment);

		dynamic response = await MakeRequestAsync<object>(CreateUrl("withdraws/coin"), ApplySecret(request), cancellationToken);

		return (string)response.uuid;
	}

	private static Uri CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{_version}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var queryHash = request
			.Parameters
			.Where(p => p.Type == ParameterType.QueryString && p.Value != null)
			.OrderBy(p => p.Name)
			.ToQueryString();

		if (!queryHash.IsEmpty())
			queryHash = _hasher.ComputeHash(queryHash.UTF8()).Digest().ToLowerInvariant();

#pragma warning disable CS0618
		var signature = new JwtBuilder()
			.WithAlgorithm(new HMACSHA512Algorithm())
			.WithSecret(_secret.UnSecure())
			.AddClaim("access_key", _key.UnSecure())
			.AddClaim("nonce", Guid.NewGuid().ToString("D"))
			.AddClaim("query_hash", queryHash)
			.Encode();
#pragma warning restore CS0618

		request.SetBearer(signature.Secure());

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject)
		{
			if (obj.error != null)
				throw new InvalidOperationException((string)obj.error.ToString());
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}