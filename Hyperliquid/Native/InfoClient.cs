namespace StockSharp.Hyperliquid.Native;

using StockSharp.Hyperliquid.Native.Common.Model;
using DerivativesModel = StockSharp.Hyperliquid.Native.Derivatives.Model;
using SpotModel = StockSharp.Hyperliquid.Native.Spot.Model;

class InfoClient : BaseLogReceiver
{
	private readonly Uri _infoEndpoint;

	public InfoClient(string infoEndpoint)
	{
		if (infoEndpoint.IsEmpty())
			throw new ArgumentNullException(nameof(infoEndpoint));

		_infoEndpoint = infoEndpoint.To<Uri>();
	}

	public override string Name => nameof(Hyperliquid) + "_" + nameof(InfoClient);

	public async ValueTask<(DerivativesModel.Meta Meta, DerivativesModel.AssetCtx[] Ctxs)> GetMetaAndAssetCtxsAsync(CancellationToken cancellationToken)
	{
		var arr = await PostAsync<JArray>(new
		{
			type = "metaAndAssetCtxs",
		}, cancellationToken);

		if (arr is null || arr.Count < 2)
			throw new InvalidOperationException("Invalid response for metaAndAssetCtxs.");

		var meta = arr[0].ToObject<DerivativesModel.Meta>() ?? new() { Universe = [] };
		meta.Universe ??= [];

		var ctxs = arr[1].ToObject<DerivativesModel.AssetCtx[]>() ?? [];
		return (meta, ctxs);
	}

	public async ValueTask<(SpotModel.Meta Meta, SpotModel.AssetCtx[] Ctxs)> GetSpotMetaAndAssetCtxsAsync(CancellationToken cancellationToken)
	{
		var arr = await PostAsync<JArray>(new
		{
			type = "spotMetaAndAssetCtxs",
		}, cancellationToken);

		if (arr is null || arr.Count < 2)
			throw new InvalidOperationException("Invalid response for spotMetaAndAssetCtxs.");

		var meta = arr[0].ToObject<SpotModel.Meta>() ?? new() { Universe = [], Tokens = [] };
		meta.Universe ??= [];
		meta.Tokens ??= [];

		var ctxs = arr[1].ToObject<SpotModel.AssetCtx[]>() ?? [];
		return (meta, ctxs);
	}

	public async ValueTask<L2BookSnapshot> GetL2BookAsync(string coin, CancellationToken cancellationToken)
	{
		if (coin.IsEmpty())
			throw new ArgumentNullException(nameof(coin));

		return await PostAsync<L2BookSnapshot>(new
		{
			type = "l2Book",
			coin,
		}, cancellationToken);
	}

	public async ValueTask<WsTrade[]> GetRecentTradesAsync(string coin, CancellationToken cancellationToken)
	{
		if (coin.IsEmpty())
			throw new ArgumentNullException(nameof(coin));

		return await PostAsync<WsTrade[]>(new
		{
			type = "recentTrades",
			coin,
		}, cancellationToken) ?? [];
	}

	public async ValueTask<WsCandle[]> GetCandleSnapshotAsync(string coin, string interval, DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		if (coin.IsEmpty())
			throw new ArgumentNullException(nameof(coin));

		if (interval.IsEmpty())
			throw new ArgumentNullException(nameof(interval));

		var fromMs = (long)from.ToUnix(false);
		var toMs = (long)to.ToUnix(false);

		return await PostAsync<WsCandle[]>(new
		{
			type = "candleSnapshot",
			req = new
			{
				coin,
				interval,
				startTime = fromMs,
				endTime = toMs,
			}
		}, cancellationToken) ?? [];
	}

	public ValueTask<DerivativesModel.ClearinghouseState> GetClearinghouseStateAsync(string user, CancellationToken cancellationToken)
	{
		if (user.IsEmpty())
			throw new ArgumentNullException(nameof(user));

		return new ValueTask<DerivativesModel.ClearinghouseState>(PostAsync<DerivativesModel.ClearinghouseState>(new
		{
			type = "clearinghouseState",
			user,
		}, cancellationToken));
	}

	public ValueTask<SpotModel.ClearinghouseState> GetSpotClearinghouseStateAsync(string user, CancellationToken cancellationToken)
	{
		if (user.IsEmpty())
			throw new ArgumentNullException(nameof(user));

		return new ValueTask<SpotModel.ClearinghouseState>(PostAsync<SpotModel.ClearinghouseState>(new
		{
			type = "spotClearinghouseState",
			user,
		}, cancellationToken));
	}

	public async ValueTask<OpenOrder[]> GetOpenOrdersAsync(string user, CancellationToken cancellationToken)
	{
		if (user.IsEmpty())
			throw new ArgumentNullException(nameof(user));

		return await PostAsync<OpenOrder[]>(new
		{
			type = "openOrders",
			user,
		}, cancellationToken) ?? [];
	}

	public async ValueTask<UserFill[]> GetUserFillsAsync(string user, CancellationToken cancellationToken)
	{
		if (user.IsEmpty())
			throw new ArgumentNullException(nameof(user));

		return await PostAsync<UserFill[]>(new
		{
			type = "userFills",
			user,
		}, cancellationToken) ?? [];
	}

	private Task<T> PostAsync<T>(object body, CancellationToken cancellationToken)
	{
		var request = new RestRequest((string)null, Method.Post);
		request.AddJsonBody(body);

		return request.InvokeAsync<T>(_infoEndpoint, this, this.AddVerboseLog, cancellationToken);
	}
}

