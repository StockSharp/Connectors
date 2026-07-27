namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using StockSharp.Messages;

/// <summary>
/// Drives a <see cref="MessageAdapter"/> through a market data session and records every
/// outgoing message, so a test can await the particular message it is interested in.
/// </summary>
/// <remarks>
/// The harness replaces the parts of the regular pipeline an adapter relies on when it is
/// hosted by a connector: it pumps <see cref="TimeMessage"/> at the adapter's
/// <see cref="MessageAdapter.HeartbeatInterval"/> (many adapters flush pending stream
/// subscriptions from there) and keeps the whole out message history, so a reader created
/// before a request never misses the reply.
/// </remarks>
class MarketDataTestHarness : IAsyncDisposable
{
	private readonly MessageAdapter _adapter;
	private readonly List<Message> _received = [];
	private readonly Lock _sync = new();
	private readonly CancellationTokenSource _pumpSource = new();
	private readonly List<Task> _pending = [];

	private static readonly TimeSpan _teardownTimeout = TimeSpan.FromSeconds(5);

	private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private Task _pump;
	private bool _connected;

	/// <summary>
	/// Initializes a new instance of the <see cref="MarketDataTestHarness"/>.
	/// </summary>
	/// <param name="adapter">Adapter under test. The harness owns it and disposes it.</param>
	public MarketDataTestHarness(MessageAdapter adapter)
	{
		_adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
		_adapter.NewOutMessageAsync += OnNewOutMessageAsync;
	}

	/// <summary>
	/// Adapter under test.
	/// </summary>
	public MessageAdapter Adapter => _adapter;

	/// <summary>
	/// Errors the adapter has reported out of band.
	/// </summary>
	public Exception[] Errors
	{
		get
		{
			using (_sync.EnterScope())
				return [.. _received.OfType<ErrorMessage>().Select(m => m.Error)];
		}
	}

	private ValueTask OnNewOutMessageAsync(Message message, CancellationToken cancellationToken)
	{
		TaskCompletionSource signal;

		using (_sync.EnterScope())
		{
			_received.Add(message);

			signal = _signal;
			_signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		signal.TrySetResult();
		return default;
	}

	/// <summary>
	/// Create a reader positioned at the end of the current out message history.
	/// </summary>
	/// <remarks>
	/// Create the reader before sending the request it is going to await, so the reply
	/// cannot be missed even when the adapter answers synchronously.
	/// </remarks>
	/// <returns><see cref="MessageReader"/></returns>
	public MessageReader CreateReader()
	{
		using (_sync.EnterScope())
			return new(this, _received.Count);
	}

	internal bool TryRead<TMessage>(ref int index, Func<TMessage, bool> filter, out TMessage found, out Task signal)
		where TMessage : Message
	{
		using (_sync.EnterScope())
		{
			for (; index < _received.Count; index++)
			{
				if (_received[index] is TMessage typed && (filter is null || filter(typed)))
				{
					index++;

					found = typed;
					signal = null;
					return true;
				}
			}

			found = null;
			signal = _signal.Task;
			return false;
		}
	}

	/// <summary>
	/// Hand the message to the adapter without waiting for it to be fully processed.
	/// </summary>
	/// <remarks>
	/// A request such as a security lookup is answered message by message while the call is
	/// still running, and on a large venue it runs for minutes. The regular pipeline feeds the
	/// adapter from a channel and reads the answers meanwhile, so the harness does the same -
	/// awaiting the call here would deadlock the reader behind the whole download.
	/// </remarks>
	/// <param name="message"><see cref="Message"/></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	public void Post(Message message, CancellationToken cancellationToken)
	{
		// tie the work to the session as well, so tearing the harness down stops a download
		// that is still streaming
		var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _pumpSource.Token);
		var token = source.Token;

		var task = Task.Run(async () =>
		{
			try
			{
				await _adapter.SendInMessageAsync(message, token);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				// the adapter turns handler faults into out messages on its own, anything
				// escaping is a harness level failure worth surfacing
				await OnNewOutMessageAsync(new ErrorMessage { Error = ex }, CancellationToken.None);
			}
			finally
			{
				source.Dispose();
			}
		}, token);

		using (_sync.EnterScope())
			_pending.Add(task);
	}

	/// <summary>
	/// Connect and start the heartbeat pump.
	/// </summary>
	/// <param name="timeout">Maximum wait time.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns><see cref="Task"/></returns>
	public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
	{
		var reader = CreateReader();

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken);
		timeoutSource.CancelAfter(timeout);

		await _adapter.SendInMessageAsync(new ConnectMessage(), timeoutSource.Token)
			.AsTask().WaitAsync(timeout, cancellationToken);

		var response = await reader.WaitAsync<ConnectMessage>(null, timeout,
			timeoutSource.Token);

		if (response.Error is not null)
			throw response.Error;

		_connected = true;
		_pump = PumpTimeAsync(_pumpSource.Token);
	}

	private async Task PumpTimeAsync(CancellationToken cancellationToken)
	{
		var interval = _adapter.HeartbeatInterval;

		if (interval <= TimeSpan.Zero)
			interval = TimeSpan.FromSeconds(1);

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await interval.Delay(cancellationToken);
				await _adapter.SendInMessageAsync(new TimeMessage(), cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	/// <summary>
	/// Download securities.
	/// </summary>
	/// <param name="max">Stop once that many securities have been received.</param>
	/// <param name="timeout">Maximum wait time.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>Lookup result.</returns>
	public async Task<SecurityLookupResult> LookupSecuritiesAsync(int max, TimeSpan timeout, CancellationToken cancellationToken)
	{
		var transId = _adapter.TransactionIdGenerator.GetNextId();
		var reader = CreateReader();

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken);
		timeoutSource.CancelAfter(timeout);
		Post(new SecurityLookupMessage { TransactionId = transId },
			timeoutSource.Token);

		var securities = new List<SecurityMessage>();
		var deadline = DateTime.UtcNow + timeout;
		var isComplete = false;

		try
		{
			// adapters differ in how they acknowledge a lookup: some reply first and finish later,
			// some only send the end marker, so accept whichever of them arrives
			while (securities.Count < max)
			{
				var left = deadline - DateTime.UtcNow;

				if (left <= TimeSpan.Zero)
					break;

				var message = await reader.TryWaitAsync<Message>(
					m => IsFor(m, transId), left, cancellationToken);

				if (message is null)
					break;

				if (message is SecurityMessage security)
				{
					securities.Add(security);
					continue;
				}

				if (message is SubscriptionResponseMessage response)
				{
					if (response.Error is not null)
						throw response.Error;

					continue;
				}

				isComplete = true;
				break;
			}
		}
		finally
		{
			await timeoutSource.CancelAsync();
		}

		return new([.. securities], isComplete);
	}

	/// <summary>
	/// Does the message belong to the subscription?
	/// </summary>
	/// <remarks>
	/// Adapters mark streamed data either by the subscription that produced it or by the
	/// instrument it belongs to - the regular pipeline resolves both, so accept both here.
	/// </remarks>
	private static bool IsOwned(Message message, long transId, SecurityId securityId)
	{
		if (message is ISubscriptionIdMessage subscription &&
			(subscription.OriginalTransactionId == transId || subscription.SubscriptionIds?.Contains(transId) == true))
			return true;

		return message is ISecurityIdMessage security && security.SecurityId == securityId;
	}

	private static bool IsFor(Message message, long transId)
		=> message switch
		{
			SecurityMessage security => security.OriginalTransactionId == transId,
			SubscriptionResponseMessage response => response.OriginalTransactionId == transId,
			SubscriptionFinishedMessage finished => finished.OriginalTransactionId == transId,
			SubscriptionOnlineMessage online => online.OriginalTransactionId == transId,
			_ => false,
		};

	/// <summary>
	/// Subscribe to the market data and wait for the first message of the expected type.
	/// </summary>
	/// <typeparam name="TMessage">Expected market data message type.</typeparam>
	/// <param name="dataType"><see cref="DataType"/></param>
	/// <param name="securityId"><see cref="SecurityId"/></param>
	/// <param name="init">Optional subscription tuning.</param>
	/// <param name="filter">Optional additional filter for the awaited message.</param>
	/// <param name="timeout">Maximum wait time.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>Subscription result.</returns>
	public async Task<MarketDataResult<TMessage>> SubscribeAsync<TMessage>(
		DataType dataType,
		SecurityId securityId,
		Action<MarketDataMessage> init,
		Func<TMessage, bool> filter,
		TimeSpan timeout,
		CancellationToken cancellationToken)
		where TMessage : Message
	{
		var transId = _adapter.TransactionIdGenerator.GetNextId();

		var mdMsg = new MarketDataMessage
		{
			TransactionId = transId,
			IsSubscribe = true,
			SecurityId = securityId,
			DataType2 = dataType,
		};

		init?.Invoke(mdMsg);

		var reader = CreateReader();

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken);
		timeoutSource.CancelAfter(timeout);
		Post(mdMsg, timeoutSource.Token);

		TMessage data = null;
		Exception error = null;
		var deadline = DateTime.UtcNow + timeout;

		try
		{
			// the acknowledgement and the data race each other: adapters that answer a historical
			// request inline may deliver the data before the reply, so watch for both at once
			while (true)
			{
				var left = deadline - DateTime.UtcNow;

				if (left <= TimeSpan.Zero)
					break;

				var message = await reader.TryWaitAsync<Message>(m =>
					m is ErrorMessage ||
					(m is SubscriptionResponseMessage response &&
						response.OriginalTransactionId == transId) ||
					(m is TMessage typed && IsOwned(typed, transId, securityId) &&
						(filter is null || filter(typed))),
					left, cancellationToken);

				if (message is null)
					break;

				if (message is SubscriptionResponseMessage reply)
				{
					if (reply.Error is not null)
					{
						error = reply.Error;
						break;
					}

					continue;
				}

				if (message is ErrorMessage errorMessage)
				{
					error = errorMessage.Error;
					break;
				}

				data = (TMessage)message;
				break;
			}
		}
		finally
		{
			await timeoutSource.CancelAsync();
		}

		// unsubscribe is a best effort cleanup, the session is torn down right after
		Post(new MarketDataMessage
		{
			TransactionId = _adapter.TransactionIdGenerator.GetNextId(),
			OriginalTransactionId = transId,
			IsSubscribe = false,
			SecurityId = securityId,
			DataType2 = dataType,
		}, cancellationToken);

		return new(transId, error, data);
	}

	/// <summary>
	/// Disconnect and stop the heartbeat pump.
	/// </summary>
	/// <param name="timeout">Maximum wait time.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns><see cref="Task"/></returns>
	public async Task DisconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
	{
		if (!_connected)
			return;

		_connected = false;
		await StopPumpAsync();

		var reader = CreateReader();

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken);
		timeoutSource.CancelAfter(timeout);

		await _adapter.SendInMessageAsync(new DisconnectMessage(),
			timeoutSource.Token).AsTask().WaitAsync(timeout, cancellationToken);
		await reader.TryWaitAsync<DisconnectMessage>(null, timeout,
			timeoutSource.Token);
	}

	private async Task StopPumpAsync()
	{
		if (!_pumpSource.IsCancellationRequested)
			_pumpSource.Cancel();

		Task[] pending;

		using (_sync.EnterScope())
		{
			pending = [.. _pending];
			_pending.Clear();
		}

		var running = pending.Append(_pump).Where(t => t is not null).ToArray();

		if (running.Length > 0)
		{
			// a request that ignores its cancellation token would otherwise hold the whole
			// session open, and the point here is only to let the well behaved ones settle
			try
			{
				await Task.WhenAll(running).WaitAsync(_teardownTimeout, CancellationToken.None);
			}
			catch (Exception)
			{
			}
		}

		_pump = null;
	}

	async ValueTask IAsyncDisposable.DisposeAsync()
	{
		_adapter.NewOutMessageAsync -= OnNewOutMessageAsync;

		await StopPumpAsync();

		_pumpSource.Dispose();
		_adapter.Dispose();
	}
}

/// <summary>
/// Independent sequential reader over the out message history of a <see cref="MarketDataTestHarness"/>.
/// </summary>
class MessageReader(MarketDataTestHarness harness, int index)
{
	private readonly MarketDataTestHarness _harness = harness ?? throw new ArgumentNullException(nameof(harness));
	private int _index = index;

	/// <summary>
	/// Read the next message of the specified type matching the filter.
	/// </summary>
	/// <typeparam name="TMessage">Awaited message type.</typeparam>
	/// <param name="filter">Optional additional filter.</param>
	/// <param name="timeout">Maximum wait time.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>Found message or <see langword="null"/> when the timeout has elapsed.</returns>
	public async Task<TMessage> TryWaitAsync<TMessage>(Func<TMessage, bool> filter, TimeSpan timeout, CancellationToken cancellationToken)
		where TMessage : Message
	{
		var deadline = DateTime.UtcNow + timeout;

		while (true)
		{
			if (_harness.TryRead(ref _index, filter, out TMessage found, out var signal))
				return found;

			var left = deadline - DateTime.UtcNow;

			if (left <= TimeSpan.Zero)
				return null;

			try
			{
				await signal.WaitAsync(left, cancellationToken);
			}
			catch (TimeoutException)
			{
				return null;
			}
		}
	}

	/// <summary>
	/// Read the next message of the specified type matching the filter.
	/// </summary>
	/// <typeparam name="TMessage">Awaited message type.</typeparam>
	/// <param name="filter">Optional additional filter.</param>
	/// <param name="timeout">Maximum wait time.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>Found message.</returns>
	/// <exception cref="TimeoutException">The message has not arrived in time.</exception>
	public async Task<TMessage> WaitAsync<TMessage>(Func<TMessage, bool> filter, TimeSpan timeout, CancellationToken cancellationToken)
		where TMessage : Message
		=> await TryWaitAsync(filter, timeout, cancellationToken)
			?? throw new TimeoutException($"No {typeof(TMessage).Name} received in {timeout}.");
}

/// <summary>
/// Outcome of a security lookup made by <see cref="MarketDataTestHarness"/>.
/// </summary>
/// <param name="Securities">Received securities.</param>
/// <param name="IsComplete">The adapter has reported the whole list, no securities were cut off.</param>
record SecurityLookupResult(SecurityMessage[] Securities, bool IsComplete);

/// <summary>
/// Outcome of a market data subscription made by <see cref="MarketDataTestHarness"/>.
/// </summary>
/// <typeparam name="TMessage">Expected market data message type.</typeparam>
/// <param name="TransactionId">Subscription id.</param>
/// <param name="Error">Error reported by the adapter, if any.</param>
/// <param name="Data">First received message, or <see langword="null"/> if none arrived in time.</param>
record MarketDataResult<TMessage>(long TransactionId, Exception Error, TMessage Data)
	where TMessage : Message
{
	/// <summary>
	/// The adapter explicitly reported the subscription as unsupported.
	/// </summary>
	public bool IsNotSupported => Error is NotSupportedException;
}
