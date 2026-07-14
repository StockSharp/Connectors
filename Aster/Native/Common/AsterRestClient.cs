namespace StockSharp.Aster.Native.Common;

using System.Security.Cryptography;

using StockSharp.Aster.Native.Common.Model;
using DerivativesModel = StockSharp.Aster.Native.Derivatives.Model;
using SpotModel = StockSharp.Aster.Native.Spot.Model;

sealed class AsterRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly SecureString _key;
	private readonly HMACSHA256 _hasher;

	public AsterRestClient(string endpoint, SecureString key, SecureString secret)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		_endpoint = endpoint.To<Uri>();
		_key = key;
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().ASCII());
	}

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		base.DisposeManaged();
	}

	public override string Name => nameof(Aster) + "_" + nameof(AsterRestClient);

	public async ValueTask<ExchangeInfo> GetExchangeInfoAsync(CancellationToken cancellationToken)
		=> await GetAsync<ExchangeInfo>("exchangeInfo", null, cancellationToken) ?? new() { Symbols = [] };

	public Task<JObject> GetBookTickerAsync(string symbol, CancellationToken cancellationToken)
		=> GetAsync<JObject>("ticker/bookTicker", request => request.AddParameter("symbol", symbol), cancellationToken);

	public Task<JObject> GetTicker24HrAsync(string symbol, CancellationToken cancellationToken)
		=> GetAsync<JObject>("ticker/24hr", request => request.AddParameter("symbol", symbol), cancellationToken);

	public Task<OrderBookSnapshot> GetDepthAsync(string symbol, int? limit, CancellationToken cancellationToken)
		=> GetAsync<OrderBookSnapshot>("depth", request =>
		{
			request.AddParameter("symbol", symbol);

			if (limit is int l && l > 0)
				request.AddParameter("limit", l);
		}, cancellationToken);

	public async ValueTask<Trade[]> GetTradesAsync(string symbol, int? limit, CancellationToken cancellationToken)
		=> await GetAsync<Trade[]>("trades", request =>
		{
			request.AddParameter("symbol", symbol);

			if (limit is int l && l > 0)
				request.AddParameter("limit", l);
		}, cancellationToken) ?? [];

	public async ValueTask<Trade[]> GetAggTradesAsync(string symbol, long? fromId, DateTime? from, DateTime? to, int? limit, CancellationToken cancellationToken)
		=> await GetAsync<Trade[]>("aggTrades", request =>
		{
			request.AddParameter("symbol", symbol);

			if (fromId is long fid && fid > 0)
				request.AddParameter("fromId", fid);

			if (from is DateTime fromTime)
				request.AddParameter("startTime", (long)fromTime.ToUnix(false));

			if (to is DateTime toTime)
				request.AddParameter("endTime", (long)toTime.ToUnix(false));

			if (limit is int l && l > 0)
				request.AddParameter("limit", l);
		}, cancellationToken) ?? [];

	public async ValueTask<JArray> GetCandlesAsync(string symbol, string interval, DateTime? from, DateTime? to, int? limit, CancellationToken cancellationToken)
		=> await GetAsync<JArray>("klines", request =>
		{
			request.AddParameter("symbol", symbol);
			request.AddParameter("interval", interval);

			if (from is DateTime fromTime)
				request.AddParameter("startTime", (long)fromTime.ToUnix(false));

			if (to is DateTime toTime)
				request.AddParameter("endTime", (long)toTime.ToUnix(false));

			if (limit is int l && l > 0)
				request.AddParameter("limit", l);
		}, cancellationToken) ?? [];

	public Task<OrderInfo> RegisterOrderAsync(
		string symbol,
		string side,
		string type,
		string tif,
		decimal? price,
		decimal quantity,
		string clientOrderId,
		decimal? stopPrice,
		bool? reduceOnly,
		string positionSide,
		CancellationToken cancellationToken)
	{
		return SendPrivateAsync<OrderInfo>("order", Method.Post, request =>
		{
			request
				.AddParameter("symbol", symbol)
				.AddParameter("side", side)
				.AddParameter("type", type)
				.AddParameter("quantity", quantity)
				.AddParameter("newOrderRespType", "ACK");

			if (!tif.IsEmpty())
				request.AddParameter("timeInForce", tif);

			if (price is decimal p && p > 0)
				request.AddParameter("price", p);

			if (!clientOrderId.IsEmpty())
				request.AddParameter("newClientOrderId", clientOrderId);

			if (stopPrice is decimal sp && sp > 0)
				request.AddParameter("stopPrice", sp);

			if (reduceOnly is bool ro)
				request.AddParameter("reduceOnly", ro.To<string>().ToLowerInvariant());

			if (!positionSide.IsEmpty())
				request.AddParameter("positionSide", positionSide.ToUpperInvariant());
		}, cancellationToken);
	}

	public Task<OrderInfo> CancelOrderAsync(string symbol, long? orderId, string originClientOrderId, string newClientOrderId, CancellationToken cancellationToken)
	{
		return SendPrivateAsync<OrderInfo>("order", Method.Delete, request =>
		{
			request.AddParameter("symbol", symbol);

			if (orderId is long oid)
				request.AddParameter("orderId", oid);

			if (!originClientOrderId.IsEmpty())
				request.AddParameter("origClientOrderId", originClientOrderId);

			if (!newClientOrderId.IsEmpty())
				request.AddParameter("newClientOrderId", newClientOrderId);
		}, cancellationToken);
	}

	public async ValueTask<OrderInfo[]> GetOpenOrdersAsync(string symbol, CancellationToken cancellationToken)
		=> await SendPrivateAsync<OrderInfo[]>("openOrders", Method.Get, request =>
		{
			if (!symbol.IsEmpty())
				request.AddParameter("symbol", symbol);
		}, cancellationToken) ?? [];

	public async ValueTask<UserTradeInfo[]> GetMyTradesAsync(string symbol, DateTime? from, DateTime? to, int? limit, CancellationToken cancellationToken)
		=> await SendPrivateAsync<UserTradeInfo[]>("myTrades", Method.Get, request =>
		{
			if (!symbol.IsEmpty())
				request.AddParameter("symbol", symbol);

			if (from is DateTime fromTime)
				request.AddParameter("startTime", (long)fromTime.ToUnix(false));

			if (to is DateTime toTime)
				request.AddParameter("endTime", (long)toTime.ToUnix(false));

			if (limit is int l && l > 0)
				request.AddParameter("limit", l);
		}, cancellationToken) ?? [];

	public ValueTask<SpotModel.AccountInfo> GetSpotAccountAsync(CancellationToken cancellationToken)
		=> new(SendPrivateAsync<SpotModel.AccountInfo>("account", Method.Get, null, cancellationToken));

	public ValueTask<DerivativesModel.AccountInfo> GetDerivativesAccountAsync(CancellationToken cancellationToken)
		=> new(SendPrivateAsync<DerivativesModel.AccountInfo>("account", Method.Get, null, cancellationToken));

	public async ValueTask<DerivativesModel.PositionRiskInfo[]> GetDerivativesPositionRiskAsync(CancellationToken cancellationToken)
		=> await SendPrivateAsync<DerivativesModel.PositionRiskInfo[]>("positionRisk", Method.Get, null, cancellationToken) ?? [];

	public async ValueTask<string> CreateListenKeyAsync(bool isDerivatives, CancellationToken cancellationToken)
	{
		var resource = isDerivatives ? "listenKey" : "userDataStream";
		var response = await SendPrivateAsync<JObject>(resource, Method.Post, null, cancellationToken);

		return response?["listenKey"]?.Value<string>()
			?? response?["data"]?["listenKey"]?.Value<string>()
			?? throw new InvalidOperationException("Aster did not return listenKey.");
	}

	public ValueTask KeepAliveListenKeyAsync(bool isDerivatives, string listenKey, CancellationToken cancellationToken)
	{
		var resource = isDerivatives ? "listenKey" : "userDataStream";

		return new(SendPrivateAsync<JObject>(resource, Method.Put, request =>
		{
			if (!listenKey.IsEmpty())
				request.AddParameter("listenKey", listenKey);
		}, cancellationToken));
	}

	public ValueTask DeleteListenKeyAsync(bool isDerivatives, string listenKey, CancellationToken cancellationToken)
	{
		var resource = isDerivatives ? "listenKey" : "userDataStream";

		return new(SendPrivateAsync<JObject>(resource, Method.Delete, request =>
		{
			if (!listenKey.IsEmpty())
				request.AddParameter("listenKey", listenKey);
		}, cancellationToken));
	}

	private Task<T> GetAsync<T>(string resource, Action<RestRequest> requestBuilder, CancellationToken cancellationToken)
	{
		var request = new RestRequest(resource, Method.Get);
		requestBuilder?.Invoke(request);
		return request.InvokeAsync<T>(_endpoint, this, this.AddVerboseLog, cancellationToken);
	}

	private Task<T> SendPrivateAsync<T>(string resource, Method method, Action<RestRequest> requestBuilder, CancellationToken cancellationToken)
	{
		EnsurePrivateCredentials();

		var request = new RestRequest(resource, method);
		requestBuilder?.Invoke(request);
		request.AddParameter("timestamp", (long)DateTime.UtcNow.ToUnix(false));

		var encoded = request
			.Parameters
			.Where(static p => p.Type == ParameterType.GetOrPost && p.Value is not null)
			.ToQueryString();

		var signature = _hasher
			.ComputeHash(encoded.UTF8())
			.Digest()
			.ToLowerInvariant();

		request
			.AddHeader("X-MBX-APIKEY", _key.UnSecure())
			.AddParameter("signature", signature);

		return request.InvokeAsync<T>(_endpoint, this, this.AddVerboseLog, cancellationToken);
	}

	private void EnsurePrivateCredentials()
	{
		if (_key.IsEmpty())
			throw new InvalidOperationException("API key is not specified.");

		if (_hasher is null)
			throw new InvalidOperationException("API secret is not specified.");
	}
}
