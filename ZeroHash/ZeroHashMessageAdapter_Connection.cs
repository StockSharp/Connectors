namespace StockSharp.ZeroHash;

public partial class ZeroHashMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient is not null || _connectionCancellation is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (Account.IsEmpty() != User.IsEmpty())
			throw new InvalidOperationException(
				"Zero Hash account and user must be configured together.");
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_authenticator = new(ApiKey, Secret, Passphrase);
			_restClient = new(ApiEndpoint, _authenticator)
			{
				Parent = this,
			};
			_connectionCancellation = new();
			await RefreshInstrumentsAsync(cancellationToken);
			ZeroHashInstrument[] instruments;
			using (_sync.EnterScope())
				instruments = [.. _instruments.Values];
			if (instruments.Length == 0)
				throw new InvalidDataException(
					"Zero Hash returned no CLOB instruments.");
			if (!Account.IsEmpty())
			{
				_portfolioName = "Zero Hash:" + AccountCode;
				_ = await RestClient.GetBalancesAsync(new()
				{
					Name = Account.Trim(),
					User = User.Trim(),
				}, cancellationToken) ?? throw new InvalidDataException(
					"Zero Hash returned an empty account-balance response.");
				StartOrderStream();
			}
			using (_sync.EnterScope())
				_nextPoll = DateTime.UtcNow + PollingInterval;
			connectMsg.SessionId = _portfolioName ?? "Zero Hash CLOB";
			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
			cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var isPollRequired = false;
		using (_sync.EnterScope())
		{
			if (_restClient is not null && !_portfolioName.IsEmpty() &&
				(_portfolioSubscriptions.Count > 0 ||
					_orderSubscriptions.Count > 0) &&
				DateTime.UtcNow >= _nextPoll)
			{
				_nextPoll = DateTime.UtcNow + PollingInterval;
				isPollRequired = true;
			}
		}
		if (isPollRequired)
			await RunSafelyAsync(PollPrivateAsync, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RefreshInstrumentsAsync(
		CancellationToken cancellationToken)
	{
		var pageToken = string.Empty;
		var seenTokens = new HashSet<string>(StringComparer.Ordinal);
		for (var pageNumber = 0; pageNumber < 1000; pageNumber++)
		{
			var page = await RestClient.ListInstrumentsAsync(new()
			{
				PageSize = 100,
				PageToken = pageToken,
			}, cancellationToken) ?? throw new InvalidDataException(
				"Zero Hash returned an empty instrument page.");
			foreach (var instrument in page.Instruments ?? [])
				AddInstrument(instrument);
			if (page.IsEndOfFile || page.NextPageToken.IsEmpty())
				return;
			if (!seenTokens.Add(page.NextPageToken))
				throw new InvalidDataException(
					"Zero Hash repeated an instrument page token.");
			pageToken = page.NextPageToken;
		}
		throw new InvalidDataException(
			"Zero Hash instrument pagination exceeded 1000 pages.");
	}

	private void StartOrderStream()
	{
		EnsureTradingConfigured();
		_orderStreamCancellation = CancellationTokenSource
			.CreateLinkedTokenSource(_connectionCancellation.Token);
		_orderStreamTask = RunOrderStreamLoopAsync(
			_orderStreamCancellation.Token).AsTask();
	}

	private async ValueTask RunOrderStreamLoopAsync(
		CancellationToken cancellationToken)
	{
		var attempt = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await RestClient.ReadOrderStreamAsync(new()
				{
					Accounts = [AccountCode],
					User = User.Trim(),
					SubscriptionId = "stocksharp-" + Guid.NewGuid().ToString("N"),
				}, OnOrderEnvelopeAsync, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				throw new EndOfStreamException(
					"Zero Hash closed the order subscription stream.");
			}
			catch (OperationCanceledException) when (
				cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception error)
			{
				await ReportStreamErrorAsync(error);
				attempt++;
				var delay = TimeSpan.FromSeconds(Math.Min(10,
					1 << Math.Min(attempt - 1, 3)));
				try
				{
					await Task.Delay(delay, cancellationToken);
				}
				catch (OperationCanceledException) when (
					cancellationToken.IsCancellationRequested)
				{
					return;
				}
			}
		}
	}

	private async ValueTask OnOrderEnvelopeAsync(ZeroHashOrderEnvelope envelope,
		CancellationToken cancellationToken)
	{
		var error = envelope?.Error?.GetMessage();
		if (!error.IsEmpty())
			throw new InvalidDataException(
				"Zero Hash order stream error: " + error);
		if (envelope?.Result is not { } result)
			return;
		var processedTime = result.ProcessedSentTime.TryParseZeroHashTime();
		if (processedTime is DateTime time)
			UpdateServerTime(time);
		foreach (var order in result.Snapshot?.Orders ?? [])
			await ProcessOrderAsync(order, 0, null, cancellationToken);
		foreach (var execution in result.Update?.Executions ?? [])
			await ProcessExecutionAsync(execution, 0, null, cancellationToken);
		if (result.Update?.CancelReject is { } rejection)
			await ProcessCancelRejectAsync(rejection, cancellationToken);
	}

	private async ValueTask RunSafelyAsync(
		Func<CancellationToken, ValueTask> action,
		CancellationToken cancellationToken)
	{
		try
		{
			await action(cancellationToken);
		}
		catch (Exception error)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ReportStreamErrorAsync(Exception error)
	{
		try
		{
			await SendOutErrorAsync(error, default);
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var connectionCancellation = _connectionCancellation;
		var orderCancellation = _orderStreamCancellation;
		var orderTask = _orderStreamTask;
		var rest = _restClient;
		var authenticator = _authenticator;
		MarketSubscription[] marketSubscriptions;
		using (_sync.EnterScope())
			marketSubscriptions = [.. _marketSubscriptions.Values];

		foreach (var subscription in marketSubscriptions)
			subscription.Cancellation.Cancel();
		orderCancellation?.Cancel();
		connectionCancellation?.Cancel();

		var errors = new List<Exception>();
		foreach (var task in marketSubscriptions.Select(static value => value.Task)
			.Append(orderTask).Where(static value => value is not null))
		{
			try
			{
				await task;
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
		}

		_connectionCancellation = null;
		_orderStreamCancellation = null;
		_orderStreamTask = null;
		_restClient = null;
		_authenticator = null;

		foreach (var subscription in marketSubscriptions)
			subscription.Cancellation.Dispose();
		orderCancellation?.Dispose();
		connectionCancellation?.Dispose();
		try
		{
			rest?.Dispose();
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
		try
		{
			authenticator?.Dispose();
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
		ClearState();
		cancellationToken.ThrowIfCancellationRequested();
		if (errors.Count == 1)
			ExceptionDispatchInfo.Capture(errors[0]).Throw();
		if (errors.Count > 1)
			throw new AggregateException(
				"One or more Zero Hash clients could not be disposed.", errors);
	}
}
