namespace StockSharp.Coincheck.Native;

using System.Security.Cryptography;

class HttpClient(SecureString key, SecureString secret) : BaseLogReceiver
{
	private readonly SecureString _key = key;
	private readonly HashAlgorithm _hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().ASCII());

	private readonly UTCIncrementalIdGenerator _nonceGen = new();

	private const string _baseUrl = "https://coincheck.com/api";

        protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Coincheck) + "_" + nameof(HttpClient);

	//public Task<Ticker> RequestTickerAsync(string ticker, CancellationToken cancellationToken)
	//{
	//	return MakeRequestAsync<Ticker>(CreateUrl($"ticker/{ticker}"), CreateRequest(Method.Get), cancellationToken);
	//}

	public Task<HttpTrade[]> GetTradesAsync(string pair, bool? asc, int? limit, CancellationToken cancellationToken)
	{
		var url = CreateUrl("trades");
		var request = CreateRequest(Method.Get);

		request.AddParameter("pair", pair);

		//if (starting != null)
		//	request.AddParameter("starting_after", starting.Value);

		//if (ending != null)
		//	request.AddParameter("ending_before", ending.Value);

		if (asc != null)
			request.AddParameter("order", asc.Value ? "asc" : "desc");

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		return MakeRequestAsync<HttpTrade[]>(url, request, cancellationToken);
	}

	public Task<Balance> GetBalanceAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("accounts/balance");
		var request = CreateRequest(Method.Get);

		return MakeRequestAsync<Balance>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<IEnumerable<Transaction>> GetTransactionsAsync(int? starting, int? ending, bool? asc, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (starting != null)
			request.AddParameter("starting_after", starting.Value);

		if (ending != null)
			request.AddParameter("ending_before", ending.Value);

		if (asc != null)
			request.AddParameter("order", asc.Value ? "asc" : "desc");

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		var url = CreateUrl("exchange/orders/transactions" + (request.Parameters.Count > 0 ? "_pagination" : string.Empty));

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return ((JToken)response.transactions).DeserializeObject<IEnumerable<Transaction>>();
	}

	public async Task<IEnumerable<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken)
	{
		var url = CreateUrl("exchange/orders/opens");
		var request = CreateRequest(Method.Get);

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return ((JToken)response.orders).DeserializeObject<IEnumerable<Order>>();
	}

	public Task<Order> RegisterOrderAsync(string pair, Sides side, decimal? price, decimal volume, decimal? stopPrice, long transactionId, CancellationToken cancellationToken)
	{
		var url = CreateUrl("exchange/orders");
		var request = CreateRequest(Method.Post);

		var type = side.ToNative();

		if (price == null)
			type = "market_" + type;

		request
			.AddParameter("pair", pair)
			.AddParameter("order_type", type)
			.AddParameter("amount", volume);

		if (price != null)
			request.AddParameter("price", price.Value);

		if (stopPrice != null)
			request.AddParameter("limit_price", stopPrice.Value);

		return MakeRequestAsync<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task CancelOrderAsync(long orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl($"exchange/orders/{orderId}");
		var request = CreateRequest(Method.Delete);

		await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<(long id, decimal? fee)> WithdrawAsync(string currency, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		Uri url;

		var request = CreateRequest(Method.Post);

		request.AddParameter("amount", volume);

		switch (info.Type)
		{
			case WithdrawTypes.BankWire:
			{
				if (!currency.EqualsIgnoreCase("JPY"))
					throw new NotSupportedException(LocalizedStrings.CurrencyNotSupported.Put(currency));

				if (info.BankDetails == null)
					throw new InvalidOperationException(LocalizedStrings.BankDetailsIsMissing);

				url = CreateUrl("withdraws");

				request
					.AddParameter("bank_account_id", info.BankDetails.Account)
					.AddParameter("currency", currency);

				break;
			}
			case WithdrawTypes.Crypto:
			{
				if (!currency.EqualsIgnoreCase("BTC"))
					throw new NotSupportedException(LocalizedStrings.CurrencyNotSupported.Put(currency));

				url = CreateUrl("send_money");

				request.AddParameter("address", info.CryptoAddress);
				break;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));
		}

		dynamic response = await MakeRequestAsync<object>(url, ApplySecret(request, url), cancellationToken);

		return ((long)response.id, (decimal?)response.fee);
	}

	private static Uri CreateUrl(string methodName, string version = "")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{version}{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request, Uri url)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		var nonce = _nonceGen.GetNextId();

		var signature = _hasher
			.ComputeHash((nonce + url.To<string>()).UTF8())
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
			if ((bool?)obj.success == false)
				throw new InvalidOperationException((string)obj.error);

			if (obj.data != null)
				obj = obj.data;
		}

		return ((JToken)obj).DeserializeObject<T>();
	}
}