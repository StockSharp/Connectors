namespace StockSharp.Injective.Native;

sealed class InjectiveGrpcClient : BaseLogReceiver
{
	private sealed class StreamState
	{
		public CancellationTokenSource Source { get; init; }
		public Task Task { get; init; }
	}

	private const string _spotService =
		"injective_spot_exchange_rpc.InjectiveSpotExchangeRPC";
	private const string _derivativeService =
		"injective_derivative_exchange_rpc.InjectiveDerivativeExchangeRPC";
	private const string _oracleService =
		"injective_oracle_rpc.InjectiveOracleRPC";
	private const string _portfolioService =
		"injective_portfolio_rpc.InjectivePortfolioRPC";

	private static readonly Marshaller<byte[]> _marshaller =
		Marshallers.Create(static value => value, static value => value);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, StreamState> _streams =
		new(StringComparer.Ordinal);
	private readonly GrpcChannel _channel;
	private readonly CallInvoker _invoker;
	private bool _isDisposed;

	public InjectiveGrpcClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address) ||
			address.Scheme is not ("http" or "https") ||
			(address.Scheme == "http" && !address.IsLoopback))
			throw new ArgumentException(
				"Injective gRPC endpoint must use HTTPS, except locally.",
				nameof(endpoint));
		_channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
		{
			MaxReceiveMessageSize = 32 * 1024 * 1024,
			MaxSendMessageSize = 1024 * 1024,
		});
		_invoker = _channel.CreateCallInvoker();
	}

	public override string Name => "Injective_gRPC";

	public event Func<InjectiveDepthUpdate, CancellationToken, ValueTask>
		DepthReceived;
	public event Func<InjectiveTradeUpdate, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<InjectiveOrderUpdate, CancellationToken, ValueTask>
		OrderReceived;
	public event Func<InjectivePosition, CancellationToken, ValueTask>
		PositionReceived;
	public event Func<InjectiveOraclePrice, CancellationToken, ValueTask>
		OraclePriceReceived;
	public event Func<InjectivePortfolioUpdate, CancellationToken, ValueTask>
		PortfolioReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> new(_channel.ConnectAsync(cancellationToken));

	public ValueTask SubscribeDepthAsync(InjectiveMarket market,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		return StartAsync("depth:" + market.Kind + ':' + market.MarketId,
			market.Kind == InjectiveMarketKinds.Spot
				? _spotService : _derivativeService,
			"StreamOrderbookV2",
			InjectiveProto.MarketStreamRequest(market.MarketId),
			InjectiveProto.ParseDepth, DepthReceived, cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(InjectiveMarket market,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		var kind = market.Kind;
		return StartAsync("trades:" + kind + ':' + market.MarketId,
			kind == InjectiveMarketKinds.Spot
				? _spotService : _derivativeService,
			"StreamTradesV2",
			InjectiveProto.TradeStreamRequest(market.MarketId),
			value => new InjectiveTradeUpdate
			{
				Kind = kind,
				Trade = InjectiveProto.ParseTrade(value,
					kind == InjectiveMarketKinds.Derivative),
			}, TradeReceived, cancellationToken);
	}

	public ValueTask SubscribeAccountTradesAsync(InjectiveMarketKinds kind,
		string subaccountId, CancellationToken cancellationToken)
		=> StartAsync("account-trades:" + kind,
			kind == InjectiveMarketKinds.Spot
				? _spotService : _derivativeService,
			"StreamTradesV2",
			InjectiveProto.TradeStreamRequest(null,
				subaccountId.ThrowIfEmpty(nameof(subaccountId))),
			value => new InjectiveTradeUpdate
			{
				Kind = kind,
				Trade = InjectiveProto.ParseTrade(value,
					kind == InjectiveMarketKinds.Derivative),
			}, TradeReceived, cancellationToken);

	public ValueTask SubscribeOracleAsync(InjectiveMarket market,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		return StartAsync("oracle:" + market.MarketId, _oracleService,
			"StreamPricesByMarkets",
			InjectiveProto.MarketStreamRequest(market.MarketId),
			InjectiveProto.ParseOraclePrice, OraclePriceReceived,
			cancellationToken);
	}

	public ValueTask SubscribeOrdersAsync(InjectiveMarketKinds kind,
		string subaccountId, CancellationToken cancellationToken)
		=> StartAsync("orders:" + kind, kind == InjectiveMarketKinds.Spot
				? _spotService : _derivativeService,
			"StreamOrders", InjectiveProto.OrderStreamRequest(null,
				subaccountId, kind == InjectiveMarketKinds.Derivative),
			value => new InjectiveOrderUpdate
			{
				Kind = kind,
				Order = InjectiveProto.ParseOrder(value,
					kind == InjectiveMarketKinds.Derivative),
			}, OrderReceived, cancellationToken);

	public ValueTask SubscribePositionsAsync(string subaccountId,
		CancellationToken cancellationToken)
		=> StartAsync("positions", _derivativeService, "StreamPositionsV2",
			InjectiveProto.PositionStreamRequest(subaccountId),
			InjectiveProto.ParsePosition, PositionReceived, cancellationToken);

	public ValueTask SubscribePortfolioAsync(string address,
		string subaccountId, CancellationToken cancellationToken)
		=> StartAsync("portfolio", _portfolioService,
			"StreamAccountPortfolio",
			InjectiveProto.PortfolioStreamRequest(address, subaccountId),
			InjectiveProto.ParsePortfolioUpdate, PortfolioReceived,
			cancellationToken);

	public ValueTask UnsubscribeDepthAsync(InjectiveMarket market,
		CancellationToken cancellationToken)
		=> StopAsync("depth:" + market.Kind + ':' + market.MarketId,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(InjectiveMarket market,
		CancellationToken cancellationToken)
		=> StopAsync("trades:" + market.Kind + ':' + market.MarketId,
			cancellationToken);

	public ValueTask UnsubscribeOracleAsync(InjectiveMarket market,
		CancellationToken cancellationToken)
		=> StopAsync("oracle:" + market.MarketId, cancellationToken);

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		StreamState[] states;
		using (_sync.EnterScope())
		{
			states = [.. _streams.Values];
			_streams.Clear();
		}
		foreach (var state in states)
			state.Source.Cancel();
		foreach (var state in states)
		{
			try
			{
				await state.Task.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				state.Source.Dispose();
			}
		}
	}

	private ValueTask StartAsync<T>(string key, string service, string method,
		byte[] request, Func<byte[], T> parse,
		Func<T, CancellationToken, ValueTask> callback,
		CancellationToken cancellationToken)
	{
		if (callback is null)
			throw new InvalidOperationException(
				$"No Injective {key} stream handler is registered.");
		cancellationToken.ThrowIfCancellationRequested();
		CancellationTokenSource source;
		using (_sync.EnterScope())
		{
			ObjectDisposedException.ThrowIf(_isDisposed, this);
			if (_streams.ContainsKey(key))
				return default;
			source = new();
			var task = RunStreamAsync(service, method, request, parse, callback,
				source.Token);
			_streams.Add(key, new StreamState
			{
				Source = source,
				Task = task,
			});
		}
		return default;
	}

	private async ValueTask StopAsync(string key,
		CancellationToken cancellationToken)
	{
		StreamState state;
		using (_sync.EnterScope())
		{
			if (!_streams.Remove(key, out state))
				return;
		}
		state.Source.Cancel();
		try
		{
			await state.Task.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			state.Source.Dispose();
		}
	}

	private async Task RunStreamAsync<T>(string service, string method,
		byte[] request, Func<byte[], T> parse,
		Func<T, CancellationToken, ValueTask> callback,
		CancellationToken cancellationToken)
	{
		var descriptor = new Method<byte[], byte[]>(MethodType.ServerStreaming,
			service, method, _marshaller, _marshaller);
		for (var attempt = 0; !cancellationToken.IsCancellationRequested;
			attempt++)
		{
			try
			{
				using var call = _invoker.AsyncServerStreamingCall(descriptor,
					null, new CallOptions(cancellationToken: cancellationToken),
					request);
				while (await call.ResponseStream.MoveNext(cancellationToken))
				{
					attempt = 0;
					await callback(parse(call.ResponseStream.Current),
						cancellationToken);
				}
			}
			catch (OperationCanceledException)
				when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				if (Error is not null)
					await Error(error, cancellationToken);
			}
			if (!cancellationToken.IsCancellationRequested)
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30,
					1 << Math.Min(attempt, 5))), cancellationToken);
		}
	}

	protected override void DisposeManaged()
	{
		using (_sync.EnterScope())
			_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		_channel.Dispose();
		base.DisposeManaged();
	}
}
