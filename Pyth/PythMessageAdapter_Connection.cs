namespace StockSharp.Pyth;

public partial class PythMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is not null || _pool is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (Channel == PythChannels.Unknown)
			throw new InvalidOperationException("Pyth channel is not specified.");

		var rest = new PythRestClient(HistoryEndpoint, RouterEndpoint, Token,
			RequestInterval) { Parent = this };
		_rest = rest;
		try
		{
			CacheInstruments(await rest.GetSymbolsAsync(IsEntitledOnly,
				cancellationToken));
			if (GetInstruments().Length == 0)
				throw new InvalidDataException(
					"Pyth returned no supported entitled price feeds.");
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync();
			ClearState();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest is null && _pool is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClientsAsync();
		ClearState();
		await base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync();
		ClearState();
		await base.ResetAsync(message, cancellationToken);
	}

	private PythRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void CacheInstruments(IEnumerable<PythSymbol> instruments)
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			foreach (var instrument in instruments ?? [])
			{
				if (!IsSupportedInstrument(instrument))
					continue;
				_instruments[instrument.Key] = instrument;
			}
		}
	}

	private PythSymbol[] GetInstruments()
	{
		using (_sync.EnterScope())
			return [.. _instruments.Values];
	}

	private PythSymbol ResolveInstrument(SecurityId securityId)
	{
		var native = (securityId.Native as string)?.Trim();
		var code = securityId.SecurityCode?.Trim();
		using (_sync.EnterScope())
		{
			if (!native.IsEmpty() && _instruments.TryGetValue(native, out var exact))
				return exact;
			if (code.IsEmpty())
				throw new ArgumentException("Pyth security code is not specified.",
					nameof(securityId));
			var matches = _instruments.Values.Where(instrument =>
				instrument.Symbol.EqualsIgnoreCase(code) ||
				instrument.Name.EqualsIgnoreCase(code)).Take(2).ToArray();
			if (matches.Length == 1)
				return matches[0];
		}
		throw new InvalidOperationException(
			$"Pyth instrument '{code}' is unknown or ambiguous. Use security lookup to preserve its feed ID.");
	}

	private async ValueTask<PythSocketPool> EnsurePoolAsync(
		CancellationToken cancellationToken)
	{
		await _poolGate.WaitAsync(cancellationToken);
		try
		{
			if (_pool is not null)
				return _pool;
			var pool = new PythSocketPool(
				[WebSocketEndpoint1, WebSocketEndpoint2, WebSocketEndpoint3], Token,
				Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
			pool.MessageReceived += OnSocketMessageAsync;
			pool.Error += OnSocketErrorAsync;
			_pool = pool;
			try
			{
				await pool.ConnectAsync(cancellationToken);
				return pool;
			}
			catch
			{
				DetachPool(pool);
				_pool = null;
				pool.Dispose();
				throw;
			}
		}
		finally
		{
			_poolGate.Release();
		}
	}

	private async ValueTask DisposeClientsAsync()
	{
		var pool = _pool;
		_pool = null;
		if (pool is not null)
		{
			DetachPool(pool);
			try
			{
				await pool.DisconnectAsync();
			}
			finally
			{
				pool.Dispose();
			}
		}
		_rest?.Dispose();
		_rest = null;
	}

	private void DetachPool(PythSocketPool pool)
	{
		pool.MessageReceived -= OnSocketMessageAsync;
		pool.Error -= OnSocketErrorAsync;
	}

	private async ValueTask OnSocketMessageAsync(PythSocketMessage message,
		CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case PythMessageTypes.StreamUpdated:
				await ProcessLiveUpdateAsync(message, cancellationToken);
				break;
			case PythMessageTypes.SubscriptionError:
				await ProcessSubscriptionErrorAsync(message, cancellationToken);
				break;
			case PythMessageTypes.SubscribedWithInvalidFeedIdsIgnored:
				await ValidateSubscriptionAckAsync(message, cancellationToken);
				break;
			case PythMessageTypes.Error:
				await SendOutErrorAsync(new IOException(
					"Pyth WebSocket error: " +
					message.Error.IsEmpty("unspecified error")), cancellationToken);
				break;
			case PythMessageTypes.Subscribed:
			case PythMessageTypes.Unsubscribed:
				break;
			default:
				await SendOutErrorAsync(new InvalidDataException(
					$"Pyth WebSocket returned unexpected message type {message.Type}."),
					cancellationToken);
				break;
		}
	}

	private async ValueTask ProcessLiveUpdateAsync(PythSocketMessage update,
		CancellationToken cancellationToken)
	{
		if (update.SubscriptionId is not long transactionId)
			throw new InvalidDataException(
				"Pyth stream update has no subscription ID.");
		LiveSubscription subscription;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryGetValue(transactionId, out subscription))
				return;
		}
		if (!TryCreateLevel1(update, subscription.Instrument,
			transactionId, subscription.SecurityId, out var message,
			out var updateKey))
			return;

		var isFinished = false;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryGetValue(transactionId, out var current) ||
				current.LastUpdateKey.EqualsIgnoreCase(updateKey))
				return;
			current.LastUpdateKey = updateKey;
			if (current.Remaining is > 0 && --current.Remaining == 0)
			{
				_liveSubscriptions.Remove(transactionId);
				isFinished = true;
			}
		}
		await SendOutMessageAsync(message, cancellationToken);
		if (isFinished)
		{
			if (_pool is { } pool)
				await pool.UnsubscribeAsync(transactionId, cancellationToken);
			await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
		}
	}

	private async ValueTask ProcessSubscriptionErrorAsync(
		PythSocketMessage message, CancellationToken cancellationToken)
	{
		if (message.SubscriptionId is not long transactionId)
		{
			await SendOutErrorAsync(new IOException(
				"Pyth subscription error: " +
				message.Error.IsEmpty("unspecified error")), cancellationToken);
			return;
		}
		if (!RemoveLiveSubscription(transactionId))
			return;
		if (_pool is { } pool)
			await pool.UnsubscribeAsync(transactionId, cancellationToken);
		await SendOutErrorAsync(new IOException(
			$"Pyth subscription {transactionId} failed: " +
			message.Error.IsEmpty("unspecified error")), cancellationToken);
		await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
	}

	private async ValueTask ValidateSubscriptionAckAsync(
		PythSocketMessage message, CancellationToken cancellationToken)
	{
		if (message.SubscriptionId is not long transactionId)
			return;
		LiveSubscription subscription;
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryGetValue(transactionId, out subscription))
				return;
		}
		if ((message.SubscribedFeedIds ?? []).Contains(subscription.Instrument.Id))
			return;
		await ProcessSubscriptionErrorAsync(new()
		{
			Type = PythMessageTypes.SubscriptionError,
			SubscriptionId = transactionId,
			Error = "The requested feed was rejected as invalid or unavailable.",
		}, cancellationToken);
	}

	private async ValueTask OnSocketErrorAsync(Exception error, bool isTerminal,
		CancellationToken cancellationToken)
	{
		await SendOutErrorAsync(error, cancellationToken);
		if (!isTerminal)
			return;
		long[] finished;
		using (_sync.EnterScope())
		{
			finished = [.. _liveSubscriptions.Keys];
			_liveSubscriptions.Clear();
		}
		foreach (var transactionId in finished)
			await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
	}

	private void AddLiveSubscription(LiveSubscription subscription)
	{
		using (_sync.EnterScope())
		{
			if (!_liveSubscriptions.TryAdd(subscription.TransactionId, subscription))
				throw new InvalidOperationException(
					$"Pyth subscription {subscription.TransactionId} already exists.");
		}
	}

	private bool RemoveLiveSubscription(long transactionId)
	{
		using (_sync.EnterScope())
			return _liveSubscriptions.Remove(transactionId);
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			_liveSubscriptions.Clear();
		}
	}

	private bool IsSupportedInstrument(PythSymbol instrument)
	{
		if (instrument is null || instrument.Id == 0 || instrument.Symbol.IsEmpty() ||
			instrument.Name.IsEmpty() || instrument.AssetType == PythAssetTypes.Unknown ||
			instrument.MinimumChannel == PythChannels.Unknown ||
			instrument.AssetType == PythAssetTypes.FundingRate ||
			!IsIncludeInactive && instrument.State != PythSymbolStates.Stable)
			return false;
		try
		{
			_ = PythExtensions.NormalizePythSymbol(instrument.Symbol,
				nameof(instrument.Symbol));
			_ = instrument.Exponent.ToPriceStep();
			_ = instrument.ExpirationTime.ParsePythExpiration();
			_ = Channel.SelectChannel(instrument.MinimumChannel);
			return true;
		}
		catch (Exception error) when (error is ArgumentException or
			InvalidDataException or OverflowException)
		{
			return false;
		}
	}
}
