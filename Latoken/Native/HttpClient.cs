namespace StockSharp.LATOKEN.Native;

using Currency = StockSharp.LATOKEN.Native.Model.Currency;

class HttpClient(Authenticator authenticator) : BaseLogReceiver
{
	private readonly Authenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

	private const string _baseUrl = "https://api.latoken.com/v2";

	private readonly UTCMlsIncrementalIdGenerator _nonceGen = new();

	// to get readable name after obfuscation
	public override string Name => nameof(LATOKEN) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Currency>> GetCurrenciesAsync(CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Currency>>(CreateUrl("currency"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		return MakeRequestAsync<IEnumerable<Symbol>>(CreateUrl("pair"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Balance>> GetBalancesAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("auth/account");

		return MakeRequestAsync<IEnumerable<Balance>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> GetOrderAsync(string orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("auth/order");

		request.AddParameter("id", orderId);

		return MakeRequestAsync<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOrdersAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("auth/order");

		return MakeRequestAsync<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<string> RegisterOrderAsync(long transId, string baseCurrencyId, string quoteCurrencyId,
		string side, string condition, string type, decimal? price, decimal volume, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);
		var url = CreateUrl("auth/order/place");

		request
			.AddParameter("baseCurrency", baseCurrencyId)
			.AddParameter("quoteCurrency", quoteCurrencyId)
			.AddParameter("side", side)
			.AddParameter("condition", condition)
			.AddParameter("type", type)
			.AddParameter("clientOrderId", transId.To<string>())
			.AddParameter("quantity", volume)
			.AddParameter("timestamp", _nonceGen.GetNextId());

		if (price != null)
			request.AddParameter("price", price.Value);

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return (string)response.id;
	}

	public Task CancelOrderAsync(string orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);
		var url = CreateUrl("auth/order/cancel");

		request.AddParameter("id", orderId);

		return MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<string> WithdrawAsync(string currencyId, decimal amount, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var request = CreateRequest(Method.Post);
		var url = CreateUrl("auth/transaction/withdraw");

		if (!info.PaymentId.IsEmpty())
			request.AddParameter("twoFaCode", info.PaymentId);

		request
			.AddParameter("currencyBinding", currencyId)
			.AddParameter("amount", amount.To<string>())
			.AddParameter("recipientAddress", info.CryptoAddress);

		if (!info.Comment.IsEmpty())
			request.AddParameter("memo", info.Comment);

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return (string)response.withdrawalId;
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

	private RestRequest ApplySecret(RestRequest request, Uri uri)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var bodyStr = request
			.Parameters
			.Where(p => p.Type == ParameterType.GetOrPost && p.Value != null)
			.OrderBy(p => p.Name)
			.ToQueryString();

		request
			.AddHeader("X-LA-APIKEY", _authenticator.Key.UnSecure())
			.AddHeader("X-LA-SIGNATURE", _authenticator.MakeSign($"{request.Method.To<string>().ToUpperInvariant()}{uri.PathAndQuery}{bodyStr}"))
			.AddHeader("X-LA-DIGEST", Authenticator.HashAlgo);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (obj is JObject)
		{
			if (obj.success == 0)
				throw new InvalidOperationException((string)obj.error);
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}