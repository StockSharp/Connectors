namespace StockSharp.FalconX.Native;

sealed class FalconXMarketSocketClient : FalconXSocketClient
{
	private sealed class Subscription
	{
		public string Key { get; init; }
		public FalconXTokenPair Pair { get; init; }
		public decimal[] Levels { get; init; }
		public int ReferenceCount { get; set; }
		public TaskCompletionSource<bool> Ready { get; init; }
	}

	private sealed class PendingCommand
	{
		public FalconXSocketEvents Event { get; init; }
		public TaskCompletionSource<bool> Completion { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, Subscription> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PendingCommand> _commands =
		new(StringComparer.Ordinal);

	public FalconXMarketSocketClient(string endpoint,
		FalconXAuthenticator authenticator, WorkingTime workingTime,
		int reconnectAttempts)
		: base(endpoint, authenticator, workingTime, reconnectAttempts)
	{
	}

	public override string Name => "FalconX_Prices_WS";

	public event Func<FalconXPriceTick[], CancellationToken, ValueTask>
		PricesReceived;

	public async ValueTask SubscribeAsync(FalconXTokenPair pair,
		decimal[] levels, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pair);
		levels = ValidateLevels(levels);
		await EnsureConnectedAsync(cancellationToken);
		var key = pair.GetKey();
		Subscription subscription;
		var isNew = false;
		using (_sync.EnterScope())
		{
			if (_subscriptions.TryGetValue(key, out subscription))
				subscription.ReferenceCount++;
			else
			{
				isNew = true;
				subscription = new()
				{
					Key = key,
					Pair = pair,
					Levels = levels,
					ReferenceCount = 1,
					Ready = new(TaskCreationOptions.RunContinuationsAsynchronously),
				};
				_subscriptions.Add(key, subscription);
			}
		}
		if (!isNew)
		{
			try
			{
				await subscription.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30),
					cancellationToken);
			}
			catch
			{
				using (_sync.EnterScope())
					if (_subscriptions.TryGetValue(key, out var current) &&
						ReferenceEquals(current, subscription) &&
						current.ReferenceCount > 0)
						current.ReferenceCount--;
				throw;
			}
			return;
		}
		try
		{
			await SendSubscribeAsync(subscription, cancellationToken);
			subscription.Ready.TrySetResult(true);
		}
		catch (Exception error)
		{
			subscription.Ready.TrySetException(error);
			using (_sync.EnterScope())
				_subscriptions.Remove(key);
			throw;
		}
	}

	public async ValueTask UnsubscribeAsync(FalconXTokenPair pair,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pair);
		Subscription subscription;
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryGetValue(pair.GetKey(), out subscription))
				return;
			if (--subscription.ReferenceCount > 0)
				return;
			_subscriptions.Remove(subscription.Key);
		}
		if (!IsConnected)
			return;
		var requestId = Guid.NewGuid().ToString("D");
		await SendCommandAsync(new FalconXPriceUnsubscriptionRequest
		{
			BaseToken = subscription.Pair.BaseToken,
			QuoteToken = subscription.Pair.QuoteToken,
			RequestId = requestId,
		}, requestId, FalconXSocketEvents.UnsubscribeResponse,
			cancellationToken);
	}

	protected override async ValueTask OnAuthenticatedAsync(bool isRestore,
		CancellationToken cancellationToken)
	{
		if (!isRestore)
			return;
		Subscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _subscriptions.Values];
		foreach (var subscription in subscriptions)
			await SendAsync(CreateSubscriptionRequest(subscription,
				Guid.NewGuid().ToString("D")), cancellationToken);
	}

	protected override async ValueTask OnMessageAsync(string payload,
		FalconXSocketHeader header, CancellationToken cancellationToken)
	{
		if (header.Event == FalconXSocketEvents.Stream)
		{
			var response =
				Deserialize<FalconXSocketResponse<FalconXPriceTick[]>>(payload);
			if (response.Status != FalconXSocketStatuses.Success)
				throw CreateException(response.Error, "price stream");
			if (PricesReceived is { } handler && response.Body?.Length > 0)
				await handler(response.Body, cancellationToken);
			return;
		}
		if (header.Event is FalconXSocketEvents.SubscribeResponse or
			FalconXSocketEvents.UnsubscribeResponse)
		{
			var response =
				Deserialize<FalconXSocketResponse<FalconXSocketMessageBody>>(payload);
			CompleteCommand(response.RequestId, response.Event,
				response.Status == FalconXSocketStatuses.Success ? null :
				CreateException(response.Error, response.Body?.Message));
			return;
		}
		if (header.Event == FalconXSocketEvents.ErrorResponse)
		{
			CompleteCommand(header.RequestId, header.Event,
				CreateException(header.Error, "WebSocket request"));
			throw CreateException(header.Error, "WebSocket request");
		}
	}

	private ValueTask SendSubscribeAsync(Subscription subscription,
		CancellationToken cancellationToken)
	{
		var requestId = Guid.NewGuid().ToString("D");
		return SendCommandAsync(CreateSubscriptionRequest(subscription, requestId),
			requestId, FalconXSocketEvents.SubscribeResponse, cancellationToken);
	}

	private static FalconXPriceSubscriptionRequest CreateSubscriptionRequest(
		Subscription subscription, string requestId)
		=> new()
		{
			Action = FalconXSocketActions.Subscribe,
			BaseToken = subscription.Pair.BaseToken,
			QuoteToken = subscription.Pair.QuoteToken,
			Quantity = new()
			{
				Token = subscription.Pair.BaseToken,
				Levels = subscription.Levels,
			},
			RequestId = requestId,
		};

	private async ValueTask SendCommandAsync<TRequest>(TRequest request,
		string requestId, FalconXSocketEvents expectedEvent,
		CancellationToken cancellationToken)
	{
		var command = new PendingCommand
		{
			Event = expectedEvent,
			Completion = new(TaskCreationOptions.RunContinuationsAsynchronously),
		};
		using (_sync.EnterScope())
			_commands.Add(requestId, command);
		try
		{
			await SendAsync(request, cancellationToken);
			await command.Completion.Task.WaitAsync(TimeSpan.FromSeconds(30),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_commands.Remove(requestId);
		}
	}

	private void CompleteCommand(string requestId, FalconXSocketEvents @event,
		Exception error)
	{
		PendingCommand command;
		using (_sync.EnterScope())
			_commands.TryGetValue(requestId ?? string.Empty, out command);
		if (command is null)
			return;
		if (@event != command.Event && @event != FalconXSocketEvents.ErrorResponse)
			error = new InvalidDataException(
				$"FalconX returned {@event} for a {command.Event} request.");
		if (error is null)
			command.Completion.TrySetResult(true);
		else
			command.Completion.TrySetException(error);
	}

	private static decimal[] ValidateLevels(decimal[] levels)
	{
		if (levels is null || levels.Length == 0 || levels.Length > 50 ||
			levels.Any(static level => level <= 0))
			throw new ArgumentOutOfRangeException(nameof(levels),
				"FalconX quote levels must contain 1-50 positive quantities.");
		return [.. levels.Distinct().OrderBy(static level => level)];
	}

	private static InvalidOperationException CreateException(
		FalconXApiError error, string fallback)
		=> new("FalconX " + (error.GetMessage() ?? fallback ?? "WebSocket error"));

	public override async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		PendingCommand[] commands;
		using (_sync.EnterScope())
		{
			commands = [.. _commands.Values];
			_commands.Clear();
			_subscriptions.Clear();
		}
		foreach (var command in commands)
			command.Completion.TrySetCanceled(cancellationToken);
		await base.DisconnectAsync(cancellationToken);
	}
}
