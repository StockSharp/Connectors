namespace StockSharp.LATOKEN.Native;

using Currency = StockSharp.LATOKEN.Native.Model.Currency;

class HttpClient(string baseUrl, Authenticator authenticator) : BaseLogReceiver
{
	private readonly Authenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

	private readonly string _baseUrl = baseUrl.ThrowIfEmpty(nameof(baseUrl)).TrimEnd('/');

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

		request.AddQueryParameter("zeros", "false");

		return MakeRequestAsync<IEnumerable<Balance>>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<Order> GetOrderAsync(string orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl($"auth/order/getOrder/{orderId.ThrowIfEmpty(nameof(orderId)).EncodeUrl()}");

		return MakeRequestAsync<Order>(url, ApplySecret(request, url), cancellationToken);
	}

	public Task<IEnumerable<Order>> GetOrdersAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("auth/order");

		return MakeRequestAsync<IEnumerable<Order>>(url, ApplySecret(request, url), cancellationToken);
	}

	public async Task<string> GetUserIdAsync(CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);
		var url = CreateUrl("auth/user");
		var user = await MakeRequestAsync<LatokenUser>(url, ApplySecret(request, url), cancellationToken);

		return user.Id.ThrowIfEmpty(nameof(user.Id));
	}

	public async Task<string> RegisterOrderAsync(long transId, string baseCurrencyId, string quoteCurrencyId,
		string side, string condition, string type, decimal? price, decimal volume, CancellationToken cancellationToken)
	{
		var url = CreateUrl("auth/order/place");
		var body = new LatokenOrderRequest
		{
			BaseCurrency = baseCurrencyId,
			QuoteCurrency = quoteCurrencyId,
			Side = side,
			Condition = condition,
			Type = type,
			ClientOrderId = transId.To<string>(),
			Price = price?.To<string>(),
			Quantity = volume.To<string>(),
			Timestamp = _nonceGen.GetNextId(),
		};
		var request = CreateRequest(Method.Post, body);
		var response = await MakeRequestAsync<LatokenOrderReply>(url, ApplySecret(request, url, body), cancellationToken);

		return response.Id.ThrowIfEmpty(nameof(response.Id));
	}

	public Task CancelOrderAsync(string orderId, CancellationToken cancellationToken)
	{
		var url = CreateUrl("auth/order/cancel");
		var body = new LatokenOrderIdRequest
		{
			Id = orderId.ThrowIfEmpty(nameof(orderId)),
		};
		var request = CreateRequest(Method.Post, body);

		return MakeRequestAsync<LatokenOrderReply>(url, ApplySecret(request, url, body), cancellationToken);
	}

	public async Task<string> WithdrawAsync(string currencyId, decimal amount, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		var url = CreateUrl("auth/transaction/withdraw");
		var body = new LatokenWithdrawalRequest
		{
			TwoFaCode = info.PaymentId.IsEmpty() ? null : info.PaymentId,
			CurrencyBinding = currencyId,
			Amount = amount.To<string>(),
			RecipientAddress = info.CryptoAddress,
			Memo = info.Comment.IsEmpty() ? null : info.Comment,
		};
		var request = CreateRequest(Method.Post, body);
		var response = await MakeRequestAsync<LatokenWithdrawalReply>(url, ApplySecret(request, url, body), cancellationToken);

		return response.WithdrawalId.ThrowIfEmpty(nameof(response.WithdrawalId));
	}

	private Uri CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method, object body = null)
	{
		var request = new RestRequest((string)null, method);

		if (body != null)
			request.AddBodyAsStr(JsonConvert.SerializeObject(body, Formatting.None));

		return request;
	}

	private RestRequest ApplySecret(RestRequest request, Uri uri, object body = null)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		IEnumerable<(string key, object value)> parameters;

		if (body == null)
		{
			parameters = request.Parameters
				.Where(p => p.Type is ParameterType.GetOrPost or ParameterType.QueryString && p.Value != null)
				.Select(p => (p.Name, p.Value));
		}
		else
		{
			parameters = body.GetType().GetProperties()
				.Select(property => (
					property,
					attribute: property.GetAttribute<JsonPropertyAttribute>(),
					value: property.GetValue(body)))
				.Where(item => item.attribute != null && item.value != null)
				.OrderBy(item => item.attribute.Order)
				.Select(item => (item.attribute.PropertyName, item.value));
		}

		var paramsStr = parameters.ToQueryString(true);

		request
			.AddHeader("X-LA-APIKEY", _authenticator.Key.UnSecure())
			.AddHeader("X-LA-SIGNATURE", _authenticator.MakeSign($"{request.Method.To<string>().ToUpperInvariant()}{uri.PathAndQuery}{paramsStr}"))
			.AddHeader("X-LA-DIGEST", Authenticator.HashAlgo);

		return request;
	}

	private Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
		=> request.InvokeAsync<T>(url, this, this.AddVerboseLog, cancellationToken);
}
