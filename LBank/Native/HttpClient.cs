namespace StockSharp.LBank.Native;

using System.Security.Cryptography;

class HttpClient(string baseUrl, SecureString key, SecureString secret) : BaseLogReceiver
{
	private readonly SecureString _key = key;
	private readonly HashAlgorithm _hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
	private readonly HashAlgorithm _md5 = MD5.Create();
	private readonly Lock _signSync = new();

	private readonly string _baseUrl = baseUrl.ThrowIfEmpty(nameof(baseUrl)).TrimEnd('/');

	private readonly UTCMlsIncrementalIdGenerator _nonceGen = new();
	private long _serverTimeOffset;

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_md5.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(LBank) + "_" + nameof(HttpClient);

	public async Task SyncTimeAsync(CancellationToken cancellationToken)
	{
		var localBefore = (long)TimeHelper.UnixNowMls;
		var serverTime = await MakeRequestAsync<long>(CreateUrl("timestamp.do"), CreateRequest(Method.Get), cancellationToken);
		var localAfter = (long)TimeHelper.UnixNowMls;

		Interlocked.Exchange(ref _serverTimeOffset, serverTime - (localBefore + localAfter) / 2);
	}

	public Task<IEnumerable<Symbol>> GetSymbolsAsync(CancellationToken cancellationToken)
		=> MakeRequestAsync<IEnumerable<Symbol>>(CreateUrl("accuracy.do"), CreateRequest(Method.Get), cancellationToken);

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

		return MakeRequestAsync<IEnumerable<Trade>>(CreateUrl("supplement/trades.do"), request, cancellationToken);
	}

	public Task<LBankAccount> GetUserInfoAsync(CancellationToken cancellationToken)
		=> MakeRequestAsync<LBankAccount>(
			CreateUrl("supplement/user_info_account.do"),
			ApplySecret(CreateRequest(Method.Post)),
			cancellationToken);

	public Task<LBankOrdersPage> GetOrdersAsync(string symbol, int page, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("current_page", page)
			.AddParameter("page_length", 200);

		return MakeRequestAsync<LBankOrdersPage>(
			CreateUrl("supplement/orders_info_no_deal.do"),
			ApplySecret(request),
			cancellationToken);
	}

	public async Task<string> RegisterOrderAsync(long transactionId, string symbol, string type, decimal? price, decimal volume, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("type", type)
			.AddParameter("custom_id", transactionId);

		if (type.EqualsIgnoreCase("buy_market"))
			request.AddParameter("price", volume);
		else
		{
			if (price != null)
				request.AddParameter("price", price.Value);

			request.AddParameter("amount", volume);
		}

		var response = await MakeRequestAsync<LBankCreateOrderReply>(
			CreateUrl("supplement/create_order.do"),
			ApplySecret(request),
			cancellationToken);

		if (response?.OrderId.IsEmpty() != false)
			throw new InvalidOperationException("LBank returned an empty order identifier.");

		return response.OrderId;
	}

	public async Task CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("symbol", symbol)
			.AddParameter("orderId", orderId);

		await MakeRequestAsync<LBankCancelOrderReply>(
			CreateUrl("supplement/cancel_order.do"),
			ApplySecret(request),
			cancellationToken);
	}

	public async Task<(long, decimal?)> WithdrawAsync(string symbol, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		if (info.ChargeFee == null)
			throw new InvalidOperationException("LBank requires the withdrawal fee.");

		var coin = symbol.Split('_')[0];
		var request = CreateRequest(Method.Post);

		request
			.AddParameter("coin", coin)
			.AddParameter("address", info.CryptoAddress)
			.AddParameter("amount", volume)
			.AddParameter("fee", info.ChargeFee.Value);

		if (!info.PaymentId.IsEmpty())
			request.AddParameter("memo", info.PaymentId);

		if (!info.Comment.IsEmpty())
			request.AddParameter("mark", info.Comment);

		var response = await request.InvokeAsync<LBankWithdrawResponse>(
			CreateUrl("spot/wallet/withdraw.do"),
			this,
			this.AddVerboseLog,
			cancellationToken);

		ThrowIfError(response?.Result, response?.ErrorCode ?? 0, response?.Message);

		var data = response?.Data;
		var result = data == null
			? (response?.WithdrawId ?? 0, response?.Fee)
			: (data.WithdrawId, data.Fee);

		if (result.Item1 == 0)
			throw new InvalidOperationException("LBank returned an empty withdrawal identifier.");

		return result;
	}

	public async Task<string> GetAuthKeyAsync(CancellationToken cancellationToken)
	{
		var response = await ApplySecret(CreateRequest(Method.Post)).InvokeAsync<LBankAuthKeyResponse>(
			CreateUrl("subscribe/get_key.do"),
			this,
			this.AddVerboseLog,
			cancellationToken);

		ThrowIfError(response?.Result, response?.ErrorCode ?? 0, response?.Message);
		return response?.Data.IsEmpty() == false ? response.Data : response?.Key;
	}

	public async Task RefreshAuthKeyAsync(string subscribeKey, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request.AddParameter("subscribeKey", subscribeKey);

		await MakeRequestAsync<LBankEmpty>(
			CreateUrl("subscribe/refresh_key.do"),
			ApplySecret(request),
			cancellationToken);
	}

	public async Task DestroyAuthKeyAsync(string subscribeKey, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Post);

		request.AddParameter("subscribeKey", subscribeKey);

		await MakeRequestAsync<LBankEmpty>(
			CreateUrl("subscribe/destroy_key.do"),
			ApplySecret(request),
			cancellationToken);
	}

	private Uri CreateUrl(string methodName)
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
		=> new((string)null, method);

	private RestRequest ApplySecret(RestRequest request)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		request.AddParameter("api_key", _key.UnSecure());

		var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);

		foreach (var parameter in request.Parameters)
			parameters.Add(parameter.Name, parameter.Value?.To<string>() ?? string.Empty);

		const string signMethod = "HmacSHA256";
		var timestamp = (_nonceGen.GetNextId() + Interlocked.Read(ref _serverTimeOffset)).To<string>();
		var echoStr = Guid.NewGuid().ToString("N");

		parameters.Add("signature_method", signMethod);
		parameters.Add("echostr", echoStr);
		parameters.Add("timestamp", timestamp);

		byte[] signature;

		using (_signSync.EnterScope())
		{
			var source = _md5.ComputeHash(parameters.ToQueryString().UTF8()).Digest().ToUpperInvariant();
			signature = _hasher.ComputeHash(source.UTF8());
		}

		request.AddParameter("sign", signature.Digest().ToLowerInvariant());

		request
			.AddHeader("signature_method", signMethod)
			.AddHeader("echostr", echoStr)
			.AddHeader("timestamp", timestamp);

		return request;
	}

	private async Task<T> MakeRequestAsync<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		var response = await request.InvokeAsync<LBankResponse<T>>(url, this, this.AddVerboseLog, cancellationToken);

		if (response == null)
			throw new InvalidOperationException("LBank returned an empty response.");

		ThrowIfError(response.Result, response.ErrorCode, response.Message);
		return response.Data;
	}

	private static void ThrowIfError(bool? result, int errorCode, string message)
	{
		if (result != false)
			return;

		if (message.IsEmpty())
			message = errorCode.ToErrorText();

		throw new InvalidOperationException($"LBank error {errorCode}: {message}");
	}
}
