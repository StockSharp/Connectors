namespace StockSharp.Bitfinex.Native;

using System.Dynamic;
using System.Security.Cryptography;

class HttpClient : BaseLogReceiver
{
	private readonly SecureString _key;
	private readonly HashAlgorithm _hasher;

	private const string _baseUrl = "https://api.bitfinex.com";

	private readonly IdGenerator _nonceGen;

	public HttpClient(SecureString key, SecureString secret)
	{
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA384(secret.UnSecure().ASCII());

		_nonceGen = new UTCIncrementalIdGenerator();
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Bitfinex) + "_" + nameof(HttpClient);

	public Task<IEnumerable<Symbol>> GetSymbolDetails(CancellationToken cancellationToken)
	{
		return MakeRequest<IEnumerable<Symbol>>(CreateUrl("symbols_details", "v1/"), CreateRequest(Method.Get), cancellationToken);
	}

	public Task<IEnumerable<Ohlc>> GetCandles(string symbol, string interval, long? from, long? end, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		if (from != null)
			request.AddParameter("start", from.Value);

		if (end != null)
			request.AddParameter("end", end.Value);

		request.AddParameter("sort", 1);

		return MakeRequest<IEnumerable<Ohlc>>(CreateUrl($"candles/trade:{interval}:{symbol}/hist"), request, cancellationToken);
	}

	public Task<IEnumerable<Trade>> GetTrades(string symbol, long? from, long? end, int? limit, CancellationToken cancellationToken)
	{
		var request = CreateRequest(Method.Get);

		if (limit != null)
			request.AddParameter("limit", limit.Value);

		if (from != null)
			request.AddParameter("start", from.Value);

		if (end != null)
			request.AddParameter("end", end.Value);

		request.AddParameter("sort", 1);

		return MakeRequest<IEnumerable<Trade>>(CreateUrl($"trades/{symbol}/hist"), request, cancellationToken);
	}

	//public Order RegisterOrder(string pair, string side, string type, decimal price, decimal volume, bool isHidden, long transactionId)
	//{
	//	var request = CreateRequest(Method.Post);
	//	var url = CreateUrl("order/new", "v1/");

	//	var obj = (dynamic)new ExpandoObject();

	//	obj.symbol = pair;
	//	obj.amount = volume.To<string>();
	//	obj.price = price.To<string>();
	//	obj.side = side;
	//	obj.type = type;

	//	if (isHidden)
	//		obj.is_hidden = true;

	//	return MakeRequest<Order>(url, (RestRequest)ApplySecret(request, url, obj));
	//}

	//public Order ReplaceOrder(long orderId, string pair, decimal volume, decimal price, string side, string type, bool isHidden)
	//{
	//	var request = CreateRequest(Method.Post);
	//	var url = CreateUrl("order/cancel/replace", "v1/");

	//	var obj = (dynamic)new ExpandoObject();

	//	obj.order_id = orderId;
	//	obj.symbol = pair;
	//	obj.amount = volume.To<string>();
	//	obj.price = price.To<string>();
	//	obj.side = side;
	//	obj.type = type;
		
	//	if (isHidden)
	//		obj.is_hidden = true;

	//	return MakeRequest<Order>(url, (RestRequest)ApplySecret(request, url, obj));
	//}

	//public Order CancelOrder(long orderId)
	//{
	//	var url = CreateUrl("order/cancel", "v1/");

	//	var obj = (dynamic)new ExpandoObject();
	//	obj.order_id = orderId;

	//	return MakeRequest<Order>(url, (RestRequest)ApplySecret(CreateRequest(Method.Post), url, obj));
	//}

	//public void CancelAllOrders()
	//{
	//	var url = CreateUrl("order/cancel/all", "v1/");

	//	var response = MakeRequest<BaseResponse>(url, (RestRequest)ApplySecret(CreateRequest(Method.Post), url, (dynamic)new ExpandoObject()));
		
	//	if (response.Result != "All orders cancelled" && response.Result != "None to cancel")
	//		throw new InvalidOperationException(response.Result);
	//}

	private static readonly Dictionary<string, string> _currencyNames = new(StringComparer.InvariantCultureIgnoreCase)
	{
		{ "BTC", "bitcoin" },
		{ "LTC", "litecoin" },
		{ "ETH", "ethereum" },
		{ "ETC", "ethereumc" },
		{ "USDT", "tetheruso" },
		{ "ZEC", "zcash" },
		{ "XMR", "monero" },
		{ "MIOTA", "iota" },
		{ "XRP", "ripple" },
		{ "DASH", "dash" },
		//{ "BTC", "adjustment" },
		//{ "BTC", "wire" },
		{ "EOS", "eos" },
		{ "SAN", "santiment" },
		{ "OMG", "omisego" },
		{ "BCH", "bcash" },
		{ "NEO", "neo" },
		{ "ETP", "metaverse" },
		{ "QTUM", "qtum" },
		{ "AVT", "aventus" },
		{ "EDO", "eidoo" },
		{ "DTC", "datacoin" },
		//{ "BTC", "tetheruse" },
		{ "BTG", "bgold" },
		{ "QASH", "qash" },
		{ "YOYOW", "yoyow" },
		{ "GNT", "golem" },
		{ "SNT", "status" },
		{ "EURT", "tethereue" },
		{ "BAT", "bat" },
		{ "MNA", "mna" },
		{ "FUN", "fun" },
		{ "ZRX", "zrx" },
		{ "TNB", "tnb" },
		{ "SPK", "spk" },
		{ "TRX", "trx" },
		{ "RCN", "rcn" },
		{ "RLC", "rlc" },
		{ "AID", "aid" },
		{ "SNG", "sng" },
		{ "REP", "rep" },
		{ "ELF", "elf" },
	};

	public async Task<long> Withdraw(string currency, string wallet, decimal volume, WithdrawInfo info, CancellationToken cancellationToken)
	{
		if (info == null)
			throw new ArgumentNullException(nameof(info));

		if (!_currencyNames.TryGetValue(currency, out var currName))
			throw new NotSupportedException(LocalizedStrings.CurrencyNotSupported.Put(currency));

		var url = CreateUrl("withdraw", "v1/");

		var obj = (dynamic)new ExpandoObject();

		obj.withdraw_type = currName;
		obj.walletselected = wallet;
		obj.amount = volume.To<string>();

		if (info.Express)
			obj.expressWire = 1;

		switch (info.Type)
		{
			case WithdrawTypes.Crypto:
				obj.address = info.CryptoAddress;

				if (!info.PaymentId.IsEmpty())
					obj.payment_id = info.PaymentId;

				break;
			case WithdrawTypes.BankWire:
				if (info.BankDetails == null)
					throw new InvalidOperationException(LocalizedStrings.BankDetailsIsMissing);

				obj.account_name = info.BankDetails.Account;
				obj.account_number = info.BankDetails.AccountName;
				obj.swift = info.BankDetails.Swift;
				obj.bank_name = info.BankDetails.Name;
				obj.bank_address = info.BankDetails.Address;
				obj.bank_city = info.BankDetails.City;
				obj.bank_country = info.BankDetails.Country;

				if (!info.Comment.IsEmpty())
					obj.detail_payment = info.Comment;

				if (info.IntermediaryBankDetails != null)
				{
					obj.intermediary_bank_account = info.IntermediaryBankDetails.Account;
					obj.intermediary_bank_swift = info.IntermediaryBankDetails.Swift;
					obj.intermediary_bank_name = info.IntermediaryBankDetails.Name;
					obj.intermediary_bank_address = info.IntermediaryBankDetails.Address;
					obj.intermediary_bank_city = info.IntermediaryBankDetails.City;
					obj.intermediary_bank_country = info.IntermediaryBankDetails.Country;
				}
				break;
			default:
				throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));
		}

		dynamic response = await MakeRequest<object>(url, (RestRequest)ApplySecret(CreateRequest(Method.Post), url, obj), cancellationToken);
		
		if (((JToken)response).Type == JTokenType.Array)
		{
			var item = (dynamic)((JArray)response)[0];

			if ((string)item.status != "success")
				throw new InvalidOperationException((string)item.message);

			return (long)item.withdrawal_id;
		}
		
		throw new InvalidOperationException((string)response.message);
	}

	private static Uri CreateUrl(string methodName, string version = "v2/")
	{
		if (methodName.IsEmpty())
			throw new ArgumentNullException(nameof(methodName));

		return $"{_baseUrl}/{version}{methodName}".To<Uri>();
	}

	private static RestRequest CreateRequest(Method method)
	{
		return new RestRequest((string)null, method);
	}

	private RestRequest ApplySecret(RestRequest request, Uri url, dynamic obj)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		obj.nonce = _nonceGen.GetNextId().To<string>();
		obj.request = url.ToString().Remove(_baseUrl);

		var json = (string)JsonConvert.SerializeObject(obj);
		var payload = json.UTF8().Base64();

		var signature = _hasher
			.ComputeHash(payload.UTF8())
			.Digest()
			.ToLowerInvariant();
	
		request
			.AddHeader("X-BFX-APIKEY", _key.UnSecure())
			.AddHeader("X-BFX-PAYLOAD", payload)
			.AddHeader("X-BFX-SIGNATURE", signature);

		return request;
	}

	private async Task<T> MakeRequest<T>(Uri url, RestRequest request, CancellationToken cancellationToken)
	{
		dynamic obj = await request.InvokeAsync(url, this, this.AddVerboseLog, cancellationToken);

		if (((JToken)obj).Type == JTokenType.Object && obj.message != null)
			throw new InvalidOperationException((string)obj.message);

		return ((JToken)obj).DeserializeObject<T>();
	}
}