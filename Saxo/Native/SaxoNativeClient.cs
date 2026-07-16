namespace StockSharp.Saxo.Native;

sealed class SaxoNativeClient : BaseLogReceiver
{
	private const string _balanceReference = "BALANCE";
	private const string _positionReference = "POSITIONS";
	private const string _activityReference = "ACTIVITIES";

	private readonly SaxoRestClient _restClient;
	private readonly SaxoStreamingClient _streamClient;
	private readonly CancellationTokenSource _tokenCancellation = new();
	private readonly SynchronizedDictionary<string, SaxoPriceRegistration> _prices = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SaxoCandleRegistration> _candles = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SaxoNetPosition> _positions = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _balanceSync = new();
	private Task _tokenTask;
	private bool _portfolioSubscribed;
	private bool _activitiesSubscribed;
	private string _lastSequenceId;
	private SaxoBalance _balance;

	public SaxoNativeClient(SaxoEnvironments environment, string accessToken, string refreshToken, string clientId,
		string clientSecret, string redirectUri, int reconnectAttempts)
	{
		_restClient = new(environment, accessToken, refreshToken, clientId, clientSecret, redirectUri) { Parent = this };
		_streamClient = new(environment, () => _restClient.AccessToken, reconnectAttempts) { Parent = this };
		_streamClient.PayloadReceived += OnPayloadReceived;
		_streamClient.SubscriptionsReset += OnSubscriptionsReset;
		_streamClient.Error += OnError;
		_streamClient.StateChanged += OnStateChanged;
	}

	public override string Name => nameof(Saxo) + "_" + nameof(SaxoNativeClient);
	public SaxoRestClient Rest => _restClient;
	public SaxoSessionInfo Session { get; private set; }
	public string ContextId => _streamClient.ContextId;

	public event Func<SaxoPriceEvent, CancellationToken, ValueTask> PriceReceived;
	public event Func<SaxoCandleEvent, CancellationToken, ValueTask> CandleReceived;
	public event Func<SaxoBalance, CancellationToken, ValueTask> BalanceReceived;
	public event Func<SaxoNetPosition, CancellationToken, ValueTask> PositionReceived;
	public event Func<SaxoActivity, CancellationToken, ValueTask> ActivityReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_streamClient.PayloadReceived -= OnPayloadReceived;
		_streamClient.SubscriptionsReset -= OnSubscriptionsReset;
		_streamClient.Error -= OnError;
		_streamClient.StateChanged -= OnStateChanged;
		_streamClient.Dispose();
		_restClient.Dispose();
		_tokenCancellation.Dispose();
		base.DisposeManaged();
	}

	public async Task Connect(string accountKey, CancellationToken cancellationToken)
	{
		await _restClient.EnsureToken(cancellationToken);
		Session = await _restClient.GetSession(accountKey, cancellationToken);
		await _streamClient.Connect(cancellationToken);
		_tokenTask = MonitorToken(_tokenCancellation.Token);
	}

	public async Task Disconnect()
	{
		_tokenCancellation.Cancel();
		if (_tokenTask != null)
		{
			try
			{
				await _tokenTask;
			}
			catch (OperationCanceledException)
			{
			}
			_tokenTask = null;
		}
		await _streamClient.Disconnect();
	}

	public async Task SubscribePrice(SaxoPriceRegistration registration, CancellationToken cancellationToken)
	{
		registration.ReferenceId = $"PRICE{registration.TransactionId}";
		_prices[registration.ReferenceId] = registration;
		try
		{
			await CreatePriceSubscription(registration, false, cancellationToken);
		}
		catch
		{
			_prices.Remove(registration.ReferenceId);
			throw;
		}
	}

	public async Task UnsubscribePrice(long transactionId, CancellationToken cancellationToken)
	{
		var registration = _prices.Values.FirstOrDefault(p => p.TransactionId == transactionId);
		if (registration == null || !_prices.Remove(registration.ReferenceId))
			return;
		await _restClient.DeleteSubscription("trade/v1/infoprices/subscriptions", ContextId,
			registration.ReferenceId, cancellationToken);
	}

	public async Task SubscribeCandles(SaxoCandleRegistration registration, CancellationToken cancellationToken)
	{
		registration.ReferenceId = $"CANDLE{registration.TransactionId}";
		_candles[registration.ReferenceId] = registration;
		try
		{
			await CreateCandleSubscription(registration, false, cancellationToken);
		}
		catch
		{
			_candles.Remove(registration.ReferenceId);
			throw;
		}
	}

	public async Task UnsubscribeCandles(long transactionId, CancellationToken cancellationToken)
	{
		var registration = _candles.Values.FirstOrDefault(p => p.TransactionId == transactionId);
		if (registration == null || !_candles.Remove(registration.ReferenceId))
			return;
		await _restClient.DeleteSubscription("chart/v3/charts/subscriptions", ContextId,
			registration.ReferenceId, cancellationToken);
	}

	public async Task SubscribePortfolio(CancellationToken cancellationToken)
	{
		if (_portfolioSubscribed)
			return;
		_portfolioSubscribed = true;
		try
		{
			await CreatePortfolioSubscriptions(false, cancellationToken);
		}
		catch
		{
			_portfolioSubscribed = false;
			throw;
		}
	}

	public async Task UnsubscribePortfolio(CancellationToken cancellationToken)
	{
		if (!_portfolioSubscribed)
			return;
		_portfolioSubscribed = false;
		await _restClient.DeleteSubscription("port/v1/balances/subscriptions", ContextId, _balanceReference, cancellationToken);
		await _restClient.DeleteSubscription("port/v1/netpositions/subscriptions", ContextId, _positionReference, cancellationToken);
	}

	public async Task SubscribeActivities(CancellationToken cancellationToken)
	{
		if (_activitiesSubscribed)
			return;
		_activitiesSubscribed = true;
		try
		{
			await CreateActivitySubscription(false, cancellationToken);
		}
		catch
		{
			_activitiesSubscribed = false;
			throw;
		}
	}

	public async Task UnsubscribeActivities(CancellationToken cancellationToken)
	{
		if (!_activitiesSubscribed)
			return;
		_activitiesSubscribed = false;
		await _restClient.DeleteSubscription("ens/v1/activities/subscriptions", ContextId, _activityReference, cancellationToken);
	}

	private async Task CreatePriceSubscription(SaxoPriceRegistration registration, bool replace,
		CancellationToken cancellationToken)
	{
		var response = await _restClient.SubscribePrices(new()
		{
			ContextId = ContextId,
			ReferenceId = registration.ReferenceId,
			ReplaceReferenceId = replace ? registration.ReferenceId : null,
			RefreshRate = 1000,
			Arguments = new()
			{
				AccountKey = Session.AccountKey,
				Uics = registration.Instrument.Uic.ToString(CultureInfo.InvariantCulture),
				AssetType = registration.Instrument.AssetType,
				FieldGroups = ["InstrumentPriceDetails", "MarketDepth", "PriceInfo", "PriceInfoDetails", "Quote"],
			},
		}, cancellationToken);
		foreach (var price in response.Snapshot?.Data ?? [])
		{
			registration.LastPrice = registration.LastPrice.Apply(price);
			await Invoke(PriceReceived, new SaxoPriceEvent(registration.TransactionId, registration.SecurityId,
				registration.DataType, registration.Instrument, registration.LastPrice, price.LastUpdated), cancellationToken);
		}
	}

	private async Task CreateCandleSubscription(SaxoCandleRegistration registration, bool replace,
		CancellationToken cancellationToken)
	{
		var response = await _restClient.SubscribeCandles(new()
		{
			ContextId = ContextId,
			ReferenceId = registration.ReferenceId,
			ReplaceReferenceId = replace ? registration.ReferenceId : null,
			RefreshRate = 1000,
			Arguments = new()
			{
				Uic = registration.Instrument.Uic,
				AssetType = registration.Instrument.AssetType,
				Horizon = registration.TimeFrame.ToSaxoHorizon(),
				Count = 10,
				FieldGroups = ["Data"],
			},
		}, cancellationToken);
		await ProcessCandleSnapshot(registration, response.Snapshot?.Data, cancellationToken);
	}

	private async Task CreatePortfolioSubscriptions(bool replace, CancellationToken cancellationToken)
	{
		var balance = await _restClient.SubscribeBalance(new()
		{
			ContextId = ContextId,
			ReferenceId = _balanceReference,
			ReplaceReferenceId = replace ? _balanceReference : null,
			RefreshRate = 10000,
			Arguments = new()
			{
				AccountKey = Session.AccountKey,
				ClientKey = Session.ClientKey,
				FieldGroups = ["CalculateCashForTrading", "MarginOverview"],
			},
		}, cancellationToken);
		if (balance.Snapshot != null)
		{
			lock (_balanceSync)
				_balance = _balance.Apply(balance.Snapshot);
			await Invoke(BalanceReceived, _balance, cancellationToken);
		}

		var positions = await _restClient.SubscribePositions(new()
		{
			ContextId = ContextId,
			ReferenceId = _positionReference,
			ReplaceReferenceId = replace ? _positionReference : null,
			RefreshRate = 1000,
			Arguments = new()
			{
				AccountKey = Session.AccountKey,
				ClientKey = Session.ClientKey,
				FieldGroups = ["DisplayAndFormat", "ExchangeInfo", "NetPositionBase", "NetPositionView"],
			},
		}, cancellationToken);
		_positions.Clear();
		foreach (var position in positions.Snapshot?.Data ?? [])
			await ProcessPosition(position, cancellationToken);
	}

	private async Task CreateActivitySubscription(bool replace, CancellationToken cancellationToken)
	{
		var response = await _restClient.SubscribeActivities(new()
		{
			ContextId = ContextId,
			ReferenceId = _activityReference,
			ReplaceReferenceId = replace ? _activityReference : null,
			Arguments = new()
			{
				AccountKey = Session.AccountKey,
				ClientKey = Session.ClientKey,
				Activities = ["Orders"],
				FieldGroups = ["DisplayAndFormat", "ExchangeInfo"],
				SequenceId = _lastSequenceId,
			},
		}, cancellationToken);
		foreach (var activity in response.Snapshot?.Data ?? [])
			await ProcessActivity(activity, cancellationToken);
	}

	private async ValueTask OnPayloadReceived(string referenceId, string payload, CancellationToken cancellationToken)
	{
		if (_prices.TryGetValue(referenceId, out var priceRegistration))
		{
			var update = JsonConvert.DeserializeObject<SaxoInfoPriceUpdate>(payload);
			foreach (var price in update?.Data ?? [])
			{
				if (price.Uic == 0)
					price.Uic = priceRegistration.Instrument.Uic;
				priceRegistration.LastPrice = priceRegistration.LastPrice.Apply(price);
				await Invoke(PriceReceived, new SaxoPriceEvent(priceRegistration.TransactionId,
					priceRegistration.SecurityId, priceRegistration.DataType, priceRegistration.Instrument,
					priceRegistration.LastPrice,
					update.Timestamp), cancellationToken);
			}
			return;
		}
		if (_candles.TryGetValue(referenceId, out var candleRegistration))
		{
			var update = JsonConvert.DeserializeObject<SaxoChartResponse>(payload);
			await ProcessCandleUpdate(candleRegistration, update?.Data, cancellationToken);
			return;
		}
		if (referenceId.EqualsIgnoreCase(_balanceReference))
		{
			var balance = JsonConvert.DeserializeObject<SaxoBalance>(payload);
			if (balance != null)
			{
				lock (_balanceSync)
					_balance = _balance.Apply(balance);
				await Invoke(BalanceReceived, _balance, cancellationToken);
			}
			return;
		}
		if (referenceId.EqualsIgnoreCase(_positionReference))
		{
			var positions = JsonConvert.DeserializeObject<SaxoFeed<SaxoNetPosition>>(payload);
			foreach (var position in positions?.Data ?? [])
				await ProcessPosition(position, cancellationToken);
			return;
		}
		if (referenceId.EqualsIgnoreCase(_activityReference))
		{
			var activities = JsonConvert.DeserializeObject<SaxoActivity[]>(payload);
			foreach (var activity in activities ?? [])
				await ProcessActivity(activity, cancellationToken);
		}
	}

	private async Task ProcessCandleSnapshot(SaxoCandleRegistration registration, SaxoChartSample[] samples,
		CancellationToken cancellationToken)
	{
		registration.Samples.Clear();
		foreach (var sample in samples ?? [])
		{
			if (sample.Time == default)
				continue;
			registration.Samples[sample.Time.UtcKind()] = sample;
		}
		TrimSamples(registration);
		if (registration.Samples.Count > 0)
		{
			var sample = registration.Samples.Values.Last();
			await Invoke(CandleReceived, new SaxoCandleEvent(registration.TransactionId,
				registration.SecurityId, registration.TimeFrame, sample, CandleStates.Active), cancellationToken);
		}
	}

	private async Task ProcessCandleUpdate(SaxoCandleRegistration registration, SaxoChartSample[] updates,
		CancellationToken cancellationToken)
	{
		if (updates.IsEmpty())
			return;
		var previousLatest = registration.Samples.Count == 0 ? (DateTime?)null : registration.Samples.Keys.Last();
		var changed = new HashSet<DateTime>();
		foreach (var update in updates)
		{
			var time = update.Time == default ? previousLatest : update.Time.UtcKind();
			if (time is null)
				continue;
			if (!registration.Samples.TryGetValue(time.Value, out var sample))
			{
				update.Time = time.Value;
				sample = update;
				registration.Samples[time.Value] = sample;
			}
			else
				sample.Apply(update);
			changed.Add(time.Value);
		}
		TrimSamples(registration);
		if (registration.Samples.Count == 0)
			return;
		var latest = registration.Samples.Keys.Last();
		if (previousLatest is not null && previousLatest < latest)
			changed.Add(previousLatest.Value);
		foreach (var time in changed.OrderBy(t => t))
		{
			if (!registration.Samples.TryGetValue(time, out var sample))
				continue;
			await Invoke(CandleReceived, new SaxoCandleEvent(registration.TransactionId,
				registration.SecurityId, registration.TimeFrame, sample,
				time == latest ? CandleStates.Active : CandleStates.Finished), cancellationToken);
		}
	}

	private static void TrimSamples(SaxoCandleRegistration registration)
	{
		while (registration.Samples.Count > 10)
			registration.Samples.Remove(registration.Samples.Keys.First());
	}

	private async Task ProcessPosition(SaxoNetPosition update, CancellationToken cancellationToken)
	{
		if (update == null)
			return;
		var id = update.NetPositionId;
		if (id.IsEmpty())
		{
			var positionBase = update.NetPositionBase;
			id = _positions.FirstOrDefault(p => p.Value.NetPositionBase?.Uic == positionBase?.Uic &&
				p.Value.NetPositionBase?.AssetType.EqualsIgnoreCase(positionBase?.AssetType) == true).Key;
		}
		if (id.IsEmpty())
			return;
		var position = _positions.TryGetValue(id, out var current) ? current.Apply(update) : update;
		_positions[id] = position;
		await Invoke(PositionReceived, position, cancellationToken);
	}

	private async ValueTask ProcessActivity(SaxoActivity activity, CancellationToken cancellationToken)
	{
		if (activity == null)
			return;
		if (!_lastSequenceId.IsEmpty() && long.TryParse(_lastSequenceId, out var current) &&
			long.TryParse(activity.SequenceId, out var next) && next <= current)
			return;
		if (!activity.SequenceId.IsEmpty())
			_lastSequenceId = activity.SequenceId;
		await Invoke(ActivityReceived, activity, cancellationToken);
	}

	private async ValueTask OnSubscriptionsReset(SaxoResetSubscriptions reset, CancellationToken cancellationToken)
	{
		var targets = reset?.TargetReferenceIds ?? [];
		var resetAll = targets.Length == 0;
		foreach (var registration in _prices.Values.ToArray())
			if (resetAll || targets.Contains(registration.ReferenceId, StringComparer.OrdinalIgnoreCase))
				await CreatePriceSubscription(registration, true, cancellationToken);
		foreach (var registration in _candles.Values.ToArray())
			if (resetAll || targets.Contains(registration.ReferenceId, StringComparer.OrdinalIgnoreCase))
				await CreateCandleSubscription(registration, true, cancellationToken);
		if (_portfolioSubscribed && (resetAll || targets.Contains(_balanceReference, StringComparer.OrdinalIgnoreCase) ||
			targets.Contains(_positionReference, StringComparer.OrdinalIgnoreCase)))
			await CreatePortfolioSubscriptions(true, cancellationToken);
		if (_activitiesSubscribed && (resetAll || targets.Contains(_activityReference, StringComparer.OrdinalIgnoreCase)))
			await CreateActivitySubscription(true, cancellationToken);
	}

	private async Task MonitorToken(CancellationToken cancellationToken)
	{
		try
		{
			var token = _restClient.AccessToken;
			using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				await _restClient.EnsureToken(cancellationToken);
				if (!token.Equals(_restClient.AccessToken, StringComparison.Ordinal))
				{
					token = _restClient.AccessToken;
					await _restClient.ReauthorizeStreaming(ContextId, cancellationToken);
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			await Invoke(Error, ex, CancellationToken.None);
		}
	}

	private ValueTask OnError(Exception error, CancellationToken cancellationToken)
		=> Invoke(Error, error, cancellationToken);

	private ValueTask OnStateChanged(ConnectionStates state, CancellationToken cancellationToken)
		=> Invoke(StateChanged, state, cancellationToken);

	private static ValueTask Invoke<T>(Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);
}
