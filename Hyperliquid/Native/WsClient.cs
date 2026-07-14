namespace StockSharp.Hyperliquid.Native;

using StockSharp.Hyperliquid.Native.Common.Model;
using DerivativesModel = StockSharp.Hyperliquid.Native.Derivatives.Model;
using SpotModel = StockSharp.Hyperliquid.Native.Spot.Model;

class WsClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private CancellationTokenSource _pingCts;
	private Task _pingTask;

	public WsClient(string endpoint, int reconnectAttempts, WorkingTime workingTime)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		_client = new WebSocketClient(
			endpoint,
			(state, token) =>
			{
				if (StateChanged is { } handler)
					return handler(state, token);

				return default;
			},
			(error, token) =>
			{
				this.AddErrorLog(error);

				if (Error is { } handler)
					return handler(error, token);

				return default;
			},
			OnProcessAsync,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
	}

	public override string Name => nameof(Hyperliquid) + "_" + nameof(WsClient);

	public event Func<DerivativesModel.WsActiveAssetContext, CancellationToken, ValueTask> ActiveAssetCtxReceived;
	public event Func<SpotModel.WsActiveAssetContext, CancellationToken, ValueTask> ActiveSpotAssetCtxReceived;
	public event Func<WsTrade[], CancellationToken, ValueTask> TradesReceived;
	public event Func<L2BookSnapshot, CancellationToken, ValueTask> L2BookReceived;
	public event Func<WsCandle, CancellationToken, ValueTask> CandleReceived;
	public event Func<OpenOrder[], CancellationToken, ValueTask> OrderUpdatesReceived;
	public event Func<UserFill[], CancellationToken, ValueTask> UserFillsReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		StopPingLoop();
		_client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);

		await _client.ConnectAsync(cancellationToken);
		StartPingLoop();
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		StopPingLoop();
		_client.Disconnect();
	}

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> SendAsync(new { method = "ping" }, cancellationToken);

	public ValueTask SubscribeActiveAssetCtx(string coin, CancellationToken cancellationToken)
		=> Subscribe(new { type = "activeAssetCtx", coin }, cancellationToken);

	public ValueTask UnsubscribeActiveAssetCtx(string coin, CancellationToken cancellationToken)
		=> Unsubscribe(new { type = "activeAssetCtx", coin }, cancellationToken);

	public ValueTask SubscribeTrades(string coin, CancellationToken cancellationToken)
		=> Subscribe(new { type = "trades", coin }, cancellationToken);

	public ValueTask UnsubscribeTrades(string coin, CancellationToken cancellationToken)
		=> Unsubscribe(new { type = "trades", coin }, cancellationToken);

	public ValueTask SubscribeL2Book(string coin, CancellationToken cancellationToken)
		=> Subscribe(new { type = "l2Book", coin }, cancellationToken);

	public ValueTask UnsubscribeL2Book(string coin, CancellationToken cancellationToken)
		=> Unsubscribe(new { type = "l2Book", coin }, cancellationToken);

	public ValueTask SubscribeCandles(string coin, string interval, CancellationToken cancellationToken)
		=> Subscribe(new { type = "candle", coin, interval }, cancellationToken);

	public ValueTask UnsubscribeCandles(string coin, string interval, CancellationToken cancellationToken)
		=> Unsubscribe(new { type = "candle", coin, interval }, cancellationToken);

	public ValueTask SubscribeOrderUpdates(string user, CancellationToken cancellationToken)
		=> Subscribe(new { type = "orderUpdates", user }, cancellationToken);

	public ValueTask UnsubscribeOrderUpdates(string user, CancellationToken cancellationToken)
		=> Unsubscribe(new { type = "orderUpdates", user }, cancellationToken);

	public ValueTask SubscribeUserFills(string user, CancellationToken cancellationToken)
		=> Subscribe(new { type = "userFills", user }, cancellationToken);

	public ValueTask UnsubscribeUserFills(string user, CancellationToken cancellationToken)
		=> Unsubscribe(new { type = "userFills", user }, cancellationToken);

	private ValueTask Subscribe(object subscription, CancellationToken cancellationToken)
		=> SendAsync(new
		{
			method = "subscribe",
			subscription,
		}, cancellationToken);

	private ValueTask Unsubscribe(object subscription, CancellationToken cancellationToken)
		=> SendAsync(new
		{
			method = "unsubscribe",
			subscription,
		}, cancellationToken);

	private ValueTask SendAsync(object body, CancellationToken cancellationToken)
		=> _client.SendAsync(body, cancellationToken);

	private async ValueTask OnProcessAsync(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();

		if (raw.EqualsIgnoreCase("pong"))
			return;

		var obj = message.AsObject() as JObject;

		if (obj is null)
			return;

		var channel = (string)obj["channel"];

		if (channel.IsEmpty())
		{
			var method = (string)obj["method"];

			if (method.EqualsIgnoreCase("pong"))
				return;

			return;
		}

		switch (channel.ToLowerInvariant())
		{
			case "error":
			{
				var ex = new InvalidOperationException(obj.ToString(Formatting.None));

				if (Error is { } handler)
					await handler(ex, cancellationToken);

				break;
			}
			case "subscriptionresponse":
			case "pong":
				break;
			case "activeassetctx":
			{
				var data = obj["data"]?.ToObject<DerivativesModel.WsActiveAssetContext>();

				if (data?.Ctx is not null && ActiveAssetCtxReceived is { } handler)
					await handler(data, cancellationToken);

				break;
			}
			case "activespotassetctx":
			{
				var data = obj["data"]?.ToObject<SpotModel.WsActiveAssetContext>();

				if (data?.Ctx is not null && ActiveSpotAssetCtxReceived is { } handler)
					await handler(data, cancellationToken);

				break;
			}
			case "trades":
			{
				var data = obj["data"]?.ToObject<WsTrade[]>() ?? [];

				if (data.Length > 0 && TradesReceived is { } handler)
					await handler(data, cancellationToken);

				break;
			}
			case "l2book":
			{
				var data = obj["data"]?.ToObject<L2BookSnapshot>();

				if (data is not null && L2BookReceived is { } handler)
					await handler(data, cancellationToken);

				break;
			}
			case "candle":
			{
				var data = obj["data"]?.ToObject<WsCandle>();

				if (data is not null && CandleReceived is { } handler)
					await handler(data, cancellationToken);

				break;
			}
			case "orderupdates":
			{
				var data = obj["data"]?.ToObject<OpenOrder[]>() ?? [];

				if (data.Length > 0 && OrderUpdatesReceived is { } handler)
					await handler(data, cancellationToken);

				break;
			}
			case "userfills":
			{
				var data = obj["data"]?.ToObject<WsUserFills>()?.Fills ?? [];

				if (data.Length > 0 && UserFillsReceived is { } handler)
					await handler(data, cancellationToken);

				break;
			}
			default:
				this.AddVerboseLog("Unknown websocket channel: {0}", channel);
				break;
		}
	}

	private void StartPingLoop()
	{
		if (_pingCts is not null)
			return;

		_pingCts = new CancellationTokenSource();
		_pingTask = PingLoopAsync(_pingCts.Token);
	}

	private void StopPingLoop()
	{
		var cts = _pingCts;
		_pingCts = null;
		_pingTask = null;

		if (cts is null)
			return;

		try
		{
			cts.Cancel();
		}
		finally
		{
			cts.Dispose();
		}
	}

	private async Task PingLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
				await PingAsync(cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			this.AddErrorLog(ex);
		}
	}
}

