namespace StockSharp.CoinW.Native;

sealed class CoinWWsClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly CoinWSections _section;
	private readonly bool _isPrivate;
	private readonly Func<CoinWWebSocketAuthentication> _authenticationProvider;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _restoreSync = new(1, 1);
	private readonly HashSet<CoinWWsChannel> _channels = [];
	private TaskCompletionSource<bool> _loginCompletion;
	private bool _isReady;

	public CoinWWsClient(string endpoint, CoinWSections section, bool isPrivate,
		Func<CoinWWebSocketAuthentication> authenticationProvider, WorkingTime workingTime)
	{
		_section = section;
		_isPrivate = isPrivate;
		_authenticationProvider = authenticationProvider;
		if (isPrivate && authenticationProvider is null)
			throw new ArgumentNullException(nameof(authenticationProvider));

		_client = new WebSocketClient(
			NormalizeEndpoint(endpoint),
			OnStateChangedAsync,
			(error, token) => RaiseErrorAsync(error, token),
			OnProcessAsync,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = 5,
			WorkingTime = workingTime,
			DisableAutoResend = true,
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
		_client.Init += OnInit;

		if (isPrivate)
		{
			var business = ToBusiness(section);
			_channels.Add(new(business, "order", null, null));
			_channels.Add(new(business, "assets", null, null));
			if (section == CoinWSections.Futures)
			{
				_channels.Add(new(business, "position", null, null));
				_channels.Add(new(business, "position_change", null, null));
			}
		}
	}

	public override string Name => nameof(CoinW) + $"_{_section}_{(_isPrivate ? "UserWs" : "MarketWs")}";

	public event Func<CoinWSections, CoinWWsTickerUpdate, CancellationToken, ValueTask> TickerReceived;
	public event Func<CoinWSections, CoinWWsDepthUpdate, CancellationToken, ValueTask> DepthReceived;
	public event Func<CoinWSections, CoinWWsTradeUpdate[], CancellationToken, ValueTask> TradesReceived;
	public event Func<CoinWSections, CoinWWsCandleUpdate, CancellationToken, ValueTask> CandleReceived;
	public event Func<CoinWSections, CoinWWsBalanceUpdate[], CancellationToken, ValueTask> BalanceReceived;
	public event Func<CoinWSections, CoinWWsOrderUpdate[], CancellationToken, ValueTask> OrderReceived;
	public event Func<CoinWWsPositionUpdate[], CancellationToken, ValueTask> PositionReceived;
	public event Func<CoinWWsFillUpdate[], CancellationToken, ValueTask> FillReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Init -= OnInit;
		_restoreSync.Dispose();
		_client.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		await _client.ConnectAsync(cancellationToken);
		await RestoreSessionAsync(cancellationToken);
		_isReady = true;
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		_isReady = false;
		await _client.DisconnectAsync(cancellationToken);
	}

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client.SendAsync(new CoinWWsPingCommand(), cancellationToken);

	public ValueTask SubscribeTickerAsync(string pairCode, CancellationToken cancellationToken)
		=> SubscribeAsync(new(ToBusiness(_section),
			_section == CoinWSections.Spot ? "ticker" : "ticker_swap", pairCode, null), cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string pairCode, CancellationToken cancellationToken)
		=> UnsubscribeAsync(new(ToBusiness(_section),
			_section == CoinWSections.Spot ? "ticker" : "ticker_swap", pairCode, null), cancellationToken);

	public async ValueTask SubscribeDepthAsync(string pairCode, int depth,
		CancellationToken cancellationToken)
	{
		_ = depth;
		if (_section == CoinWSections.Spot)
			await SubscribeAsync(new(ToBusiness(_section), "depth_snapshot", pairCode, null), cancellationToken);
		await SubscribeAsync(new(ToBusiness(_section), "depth", pairCode, null), cancellationToken);
	}

	public async ValueTask UnsubscribeDepthAsync(string pairCode, int depth,
		CancellationToken cancellationToken)
	{
		_ = depth;
		if (_section == CoinWSections.Spot)
			await UnsubscribeAsync(new(ToBusiness(_section), "depth_snapshot", pairCode, null), cancellationToken);
		await UnsubscribeAsync(new(ToBusiness(_section), "depth", pairCode, null), cancellationToken);
	}

	public async ValueTask ResubscribeDepthAsync(string pairCode, int depth,
		CancellationToken cancellationToken)
	{
		await UnsubscribeDepthAsync(pairCode, depth, cancellationToken);
		await SubscribeDepthAsync(pairCode, depth, cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(string pairCode, CancellationToken cancellationToken)
		=> SubscribeAsync(new(ToBusiness(_section), "fills", pairCode, null), cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string pairCode, CancellationToken cancellationToken)
		=> UnsubscribeAsync(new(ToBusiness(_section), "fills", pairCode, null), cancellationToken);

	public ValueTask SubscribeCandlesAsync(string pairCode, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> SubscribeAsync(new(ToBusiness(_section),
			_section == CoinWSections.Spot ? "candles" : "candles_swap_utc",
			pairCode, _section == CoinWSections.Spot
				? timeFrame.ToCoinWInterval()
				: timeFrame.ToCoinWFuturesWebSocketInterval()), cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string pairCode, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> UnsubscribeAsync(new(ToBusiness(_section),
			_section == CoinWSections.Spot ? "candles" : "candles_swap_utc",
			pairCode, _section == CoinWSections.Spot
				? timeFrame.ToCoinWInterval()
				: timeFrame.ToCoinWFuturesWebSocketInterval()), cancellationToken);

	private ValueTask SubscribeAsync(CoinWWsChannel channel, CancellationToken cancellationToken)
	{
		bool isAdded;
		using (_sync.EnterScope())
		{
			if (!_channels.Contains(channel) && _channels.Count >= 100)
				throw new InvalidOperationException("CoinW allows at most 100 channels per WebSocket connection.");
			isAdded = _channels.Add(channel);
		}
		return isAdded ? SendSubscriptionAsync(channel, true, cancellationToken) : default;
	}

	private ValueTask UnsubscribeAsync(CoinWWsChannel channel, CancellationToken cancellationToken)
	{
		bool isRemoved;
		using (_sync.EnterScope())
			isRemoved = _channels.Remove(channel);
		return isRemoved ? SendSubscriptionAsync(channel, false, cancellationToken) : default;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Connected && _isReady)
		{
			try
			{
				await RestoreSessionAsync(cancellationToken);
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask RestoreSessionAsync(CancellationToken cancellationToken)
	{
		await _restoreSync.WaitAsync(cancellationToken);
		try
		{
			await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
			if (_isPrivate)
				await LoginAsync(cancellationToken);

			CoinWWsChannel[] channels;
			using (_sync.EnterScope())
				channels = [.. _channels];
			foreach (var channel in channels)
				await SendSubscriptionAsync(channel, true, cancellationToken);
		}
		finally
		{
			_restoreSync.Release();
		}
	}

	private async ValueTask LoginAsync(CancellationToken cancellationToken)
	{
		var authentication = _authenticationProvider();
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_loginCompletion = completion;
		await _client.SendAsync(new CoinWWsLoginCommand
		{
			Parameters = new()
			{
				ApiKey = authentication.ApiKey,
				Passphrase = authentication.Secret,
			},
		}, cancellationToken);
		try
		{
			await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
			{
				if (ReferenceEquals(_loginCompletion, completion))
					_loginCompletion = null;
			}
		}
	}

	private ValueTask SendSubscriptionAsync(CoinWWsChannel channel, bool isSubscribe,
		CancellationToken cancellationToken)
		=> _client.SendAsync(new CoinWWsSubscriptionCommand
		{
			Event = isSubscribe ? "sub" : "unsub",
			Parameters = new()
			{
				Business = channel.Business,
				Type = channel.Type,
				PairCode = channel.PairCode,
				Interval = channel.Interval,
			},
		}, cancellationToken);

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var header = Deserialize<CoinWWsHeader>(payload);
			if (header.Event.EqualsIgnoreCase("pong"))
				return;
			if (header.Event.EqualsIgnoreCase("ping"))
			{
				await PingAsync(cancellationToken);
				return;
			}
			if (header.Event.EqualsIgnoreCase("login") || header.Channel.EqualsIgnoreCase("login"))
			{
				var acknowledgement = Deserialize<CoinWWsEnvelope<CoinWWsAcknowledgement>>(payload).Data;
				CompleteLogin(acknowledgement?.IsSuccess != false,
					acknowledgement?.Message.IsEmpty(header.Message));
				return;
			}
			if (header.Event.EqualsIgnoreCase("sub") || header.Event.EqualsIgnoreCase("unsub") ||
				header.Channel.EqualsIgnoreCase("subscribe") || header.Channel.EqualsIgnoreCase("unsubscribe"))
			{
				var acknowledgement = Deserialize<CoinWWsEnvelope<CoinWWsAcknowledgement>>(payload).Data;
				if (header.IsSuccess == false || acknowledgement?.IsSuccess == false)
					throw new InvalidOperationException($"CoinW WebSocket subscription failed: " +
						acknowledgement?.Message.IsEmpty(header.Message));
				return;
			}

			switch (header.Type?.ToLowerInvariant())
			{
				case "ticker":
					await ProcessSpotTickerAsync(payload, header, cancellationToken);
					break;
				case "ticker_swap":
					await ProcessFuturesTickerAsync(payload, header, cancellationToken);
					break;
				case "depth_snapshot":
				case "depth":
					await ProcessDepthAsync(payload, header, cancellationToken);
					break;
				case "fills":
					await ProcessTradesAsync(payload, header, cancellationToken);
					break;
				case "candles":
				case "candles_swap_utc":
					await ProcessCandleAsync(payload, header, cancellationToken);
					break;
				case "assets":
					await ProcessBalancesAsync(payload, cancellationToken);
					break;
				case "order":
					await ProcessOrdersAsync(payload, cancellationToken);
					break;
				case "position":
					await ProcessPositionsAsync(payload, cancellationToken);
					break;
				case "position_change":
					await ProcessFillsAsync(payload, cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or TimeoutException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessSpotTickerAsync(string payload, CoinWWsHeader header,
		CancellationToken cancellationToken)
	{
		if (TickerReceived is not { } handler)
			return;
		var item = DeserializeSpotData<CoinWSpotWsTicker>(payload);
		if (item is null)
			return;
		await handler(_section, new()
		{
			PairCode = header.PairCode,
			LastPrice = item.LastPrice,
			BidPrice = item.BidPrice,
			AskPrice = item.AskPrice,
			OpenPrice = item.OpenPrice,
			HighPrice = item.HighPrice,
			LowPrice = item.LowPrice,
			PriceChangePercent = item.PriceChangePercent,
			Volume = item.Volume,
			QuoteVolume = item.QuoteVolume,
		}, cancellationToken);
	}

	private async ValueTask ProcessFuturesTickerAsync(string payload, CoinWWsHeader header,
		CancellationToken cancellationToken)
	{
		if (TickerReceived is not { } handler)
			return;
		var item = Deserialize<CoinWWsEnvelope<CoinWFuturesWsTicker>>(payload).Data;
		if (item is null)
			return;
		await handler(_section, new()
		{
			PairCode = header.PairCode,
			LastPrice = item.LastPrice,
			OpenPrice = item.OpenPrice,
			HighPrice = item.HighPrice,
			LowPrice = item.LowPrice,
			PriceChangePercent = item.PriceChangePercent,
			Volume = item.Volume,
			QuoteVolume = item.QuoteVolume,
		}, cancellationToken);
	}

	private async ValueTask ProcessDepthAsync(string payload, CoinWWsHeader header,
		CancellationToken cancellationToken)
	{
		if (DepthReceived is not { } handler)
			return;
		if (_section == CoinWSections.Spot)
		{
			var item = DeserializeSpotData<CoinWSpotWsDepth>(payload);
			if (item is null)
				return;
			await handler(_section, new()
			{
				PairCode = header.PairCode,
				Bids = item.Bids ?? [],
				Asks = item.Asks ?? [],
				Time = item.Time > 0 ? item.Time : header.Timestamp,
				FirstSequence = item.FirstSequence,
				LastSequence = item.LastSequence > 0 ? item.LastSequence : item.Sequence,
				IsSnapshot = header.Type.EqualsIgnoreCase("depth_snapshot"),
			}, cancellationToken);
		}
		else
		{
			var item = Deserialize<CoinWWsEnvelope<CoinWFuturesWsDepth>>(payload).Data;
			if (item is null)
				return;
			await handler(_section, new()
			{
				PairCode = item.NativeSymbol.IsEmpty(header.PairCode),
				Bids = item.Bids ?? [],
				Asks = item.Asks ?? [],
				Time = item.Time > 0 ? item.Time : header.Timestamp,
				IsSnapshot = true,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessTradesAsync(string payload, CoinWWsHeader header,
		CancellationToken cancellationToken)
	{
		if (TradesReceived is not { } handler)
			return;
		if (_section == CoinWSections.Spot)
		{
			var items = DeserializeSpotData<CoinWSpotWsTrade[]>(payload) ?? [];
			await handler(_section, [.. items.Select(item => new CoinWWsTradeUpdate
			{
				PairCode = header.PairCode,
				Id = item.Id,
				Price = item.Price,
				Volume = item.Volume,
				Side = item.Side,
				Time = item.Time,
			})], cancellationToken);
		}
		else
		{
			var items = Deserialize<CoinWWsEnvelope<CoinWFuturesWsTrade[]>>(payload).Data ?? [];
			await handler(_section, [.. items.Select(item => new CoinWWsTradeUpdate
			{
				PairCode = header.PairCode,
				Id = item.Id,
				Price = item.Price,
				Volume = item.Volume,
				Side = item.Direction,
				Time = item.Time,
			})], cancellationToken);
		}
	}

	private async ValueTask ProcessCandleAsync(string payload, CoinWWsHeader header,
		CancellationToken cancellationToken)
	{
		if (CandleReceived is not { } handler)
			return;
		CoinWWsCandleUpdate update;
		if (_section == CoinWSections.Spot)
		{
			var item = DeserializeSpotData<CoinWSpotWsCandle>(payload);
			if (item is null)
				return;
			update = new()
			{
				PairCode = header.PairCode,
				Interval = header.Interval,
				OpenTime = item.OpenTime,
				OpenPrice = item.OpenPrice,
				HighPrice = item.HighPrice,
				LowPrice = item.LowPrice,
				ClosePrice = item.ClosePrice,
				Volume = item.Volume,
				QuoteVolume = item.QuoteVolume,
			};
		}
		else
		{
			var item = Deserialize<CoinWWsEnvelope<CoinWFuturesWsCandle>>(payload).Data;
			if (item is null)
				return;
			update = new()
			{
				PairCode = header.PairCode,
				Interval = header.Interval,
				OpenTime = item.OpenTime,
				OpenPrice = item.OpenPrice,
				HighPrice = item.HighPrice,
				LowPrice = item.LowPrice,
				ClosePrice = item.ClosePrice,
				Volume = item.Volume,
			};
		}
		await handler(_section, update, cancellationToken);
	}

	private async ValueTask ProcessBalancesAsync(string payload, CancellationToken cancellationToken)
	{
		if (BalanceReceived is not { } handler)
			return;
		if (_section == CoinWSections.Spot)
		{
			var item = Deserialize<CoinWWsEnvelope<CoinWSpotWsBalance>>(payload).Data;
			var items = item is null ? [] : new[] { item };
			await handler(_section, [.. items.Select(item => new CoinWWsBalanceUpdate
			{
				Asset = item.Asset,
				Available = item.Available,
				Held = item.Held,
				Time = item.Time,
			})], cancellationToken);
		}
		else
		{
			var items = Deserialize<CoinWWsEnvelope<CoinWFuturesWsBalance[]>>(payload).Data ?? [];
			await handler(_section, [.. items.Select(item => new CoinWWsBalanceUpdate
			{
				Asset = item.Asset,
				Available = item.Available,
				Held = item.Frozen.IsEmpty(item.Held),
				Margin = item.Margin,
				UnrealizedPnl = item.UnrealizedPnl,
			})], cancellationToken);
		}
	}

	private async ValueTask ProcessOrdersAsync(string payload, CancellationToken cancellationToken)
	{
		if (OrderReceived is not { } handler)
			return;
		if (_section == CoinWSections.Spot)
		{
			var item = Deserialize<CoinWWsEnvelope<CoinWSpotWsOrder>>(payload).Data;
			var items = item is null ? [] : new[] { item };
			await handler(_section, [.. items.Select(item => new CoinWWsOrderUpdate
			{
				Symbol = item.Symbol,
				OrderId = item.OrderId,
				ClientOrderId = item.ClientOrderId,
				Side = item.Side,
				Volume = item.Volume,
				RemainingVolume = item.RemainingVolume,
				Price = item.Price,
				AveragePrice = item.AveragePrice,
				OrderType = item.OrderType,
				Status = item.Status,
				Fee = item.Fee,
				Time = item.Time,
			})], cancellationToken);
		}
		else
		{
			var items = Deserialize<CoinWWsEnvelope<CoinWFuturesWsOrder[]>>(payload).Data ?? [];
			await handler(_section, [.. items.Select(item => new CoinWWsOrderUpdate
			{
				Symbol = item.NativeSymbol,
				OrderId = item.OrderId,
				ClientOrderId = item.ClientOrderId,
				Side = item.Direction,
				Volume = item.Quantity,
				ExecutedVolume = item.ExecutedContracts,
				ContractSize = item.ContractSize,
				Contracts = item.TotalContracts,
				ExecutedContracts = item.ExecutedContracts,
				QuantityUnit = item.QuantityUnit,
				Price = item.Price,
				OrderType = item.OriginalType,
				Status = item.OrderStatus.IsEmpty(item.Status),
				Fee = item.Fee,
				PositionType = item.PositionType,
				Time = item.UpdatedTime > 0 ? item.UpdatedTime : item.CreatedTime,
			})], cancellationToken);
		}
	}

	private async ValueTask ProcessPositionsAsync(string payload, CancellationToken cancellationToken)
	{
		if (PositionReceived is not { } handler)
			return;
		var items = Deserialize<CoinWWsEnvelope<CoinWFuturesWsPosition[]>>(payload).Data ?? [];
		await handler([.. items.Select(item => new CoinWWsPositionUpdate
		{
			Symbol = item.NativeSymbol,
			PositionId = item.PositionId,
			Side = item.Direction,
			Volume = item.Quantity,
			ContractSize = item.ContractSize,
			Contracts = item.CurrentContracts,
			OpenPrice = item.OpenPrice,
			IndexPrice = item.IndexPrice,
			Margin = item.Margin,
			UnrealizedPnl = item.UnrealizedPnl,
			Leverage = item.Leverage,
			Status = item.Status,
			Time = item.UpdatedTime > 0 ? item.UpdatedTime : item.CreatedTime,
		})], cancellationToken);
	}

	private async ValueTask ProcessFillsAsync(string payload, CancellationToken cancellationToken)
	{
		if (FillReceived is not { } handler)
			return;
		var items = Deserialize<CoinWWsEnvelope<CoinWFuturesWsFill[]>>(payload).Data ?? [];
		await handler([.. items.Select(item => new CoinWWsFillUpdate
		{
			Symbol = item.NativeSymbol,
			TradeId = item.TradeId,
			OrderId = item.OrderId,
			PositionId = item.PositionId,
			Side = item.Direction,
			Price = item.Price,
			Volume = item.Quantity,
			ContractSize = item.ContractSize,
			Contracts = item.Contracts,
			Fee = item.Fee,
			RealizedPnl = item.RealizedPnl,
			Time = item.Time,
		})], cancellationToken);
	}

	private void CompleteLogin(bool isSuccess, string message)
	{
		TaskCompletionSource<bool> completion;
		using (_sync.EnterScope())
			completion = _loginCompletion;
		if (completion is null)
			return;
		if (isSuccess)
			completion.TrySetResult(true);
		else
			completion.TrySetException(new InvalidOperationException($"CoinW WebSocket login failed: {message}"));
	}

	private void OnInit(ClientWebSocket socket)
		=> socket.Options.SetRequestHeader("User-Agent", "StockSharp-CoinW-Connector/1.0");

	private static T Deserialize<T>(string payload)
		where T : class
		=> JsonConvert.DeserializeObject<T>(payload)
			?? throw new InvalidDataException("CoinW WebSocket returned an empty JSON value.");

	private static T DeserializeSpotData<T>(string payload)
		where T : class
	{
		var json = Deserialize<CoinWWsStringEnvelope>(payload).Data;
		if (json.IsEmpty())
			throw new InvalidDataException("CoinW spot WebSocket returned an empty data value.");
		return Deserialize<T>(json);
	}

	private async ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static string ToBusiness(CoinWSections section)
		=> section == CoinWSections.Spot ? "exchange" : "futures";

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}
}
