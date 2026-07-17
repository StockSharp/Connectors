namespace StockSharp.Daishin.Native;

sealed class DaishinStaDispatcher : IDisposable
{
	[StructLayout(LayoutKind.Sequential)]
	private struct NativePoint
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeMessage
	{
		public nint Window;
		public uint Id;
		public nuint WParam;
		public nint LParam;
		public uint Time;
		public NativePoint Point;
		public uint Private;
	}

	private const uint _removeMessage = 1;
	private readonly BlockingCollection<Action> _queue = [];
	private readonly TaskCompletionSource<object> _started =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Thread _thread;
	private int _isDisposed;

	public DaishinStaDispatcher()
	{
		if (!OperatingSystem.IsWindows())
			throw new PlatformNotSupportedException("Daishin CYBOS Plus requires Windows.");

		_thread = new(Run)
		{
			IsBackground = true,
			Name = "Daishin CYBOS Plus STA",
		};
		_thread.SetApartmentState(ApartmentState.STA);
		_thread.Start();
		_started.Task.GetAwaiter().GetResult();
	}

	public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(action);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
		cancellationToken.ThrowIfCancellationRequested();

		if (Thread.CurrentThread == _thread)
			return Task.FromResult(action());

		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		var state = 0;
		var cancellationRegistration = cancellationToken.Register(() =>
		{
			if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
				completion.TrySetCanceled(cancellationToken);
		});
		try
		{
			_queue.Add(() =>
			{
				if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
					return;
				try
				{
					completion.TrySetResult(action());
				}
				catch (Exception error)
				{
					completion.TrySetException(error);
				}
			});
		}
		catch (InvalidOperationException)
		{
			if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
				completion.TrySetException(new ObjectDisposedException(nameof(DaishinStaDispatcher)));
		}

		return AwaitCompletionAsync(completion.Task, cancellationRegistration);
	}

	public Task InvokeAsync(Action action, CancellationToken cancellationToken)
		=> InvokeAsync(() =>
		{
			action();
			return true;
		}, cancellationToken);

	private static async Task<T> AwaitCompletionAsync<T>(Task<T> task,
		CancellationTokenRegistration cancellationRegistration)
	{
		try
		{
			return await task;
		}
		finally
		{
			cancellationRegistration.Dispose();
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
			return;

		_queue.CompleteAdding();
		if (Thread.CurrentThread == _thread || _thread.Join(TimeSpan.FromSeconds(10)))
			_queue.Dispose();
	}

	private void Run()
	{
		try
		{
			_ = PeekMessage(out _, 0, 0, 0, 0);
			_started.TrySetResult(null);
			while (!_queue.IsCompleted)
			{
				if (_queue.TryTake(out var action, 10))
					action();
				PumpMessages();
			}

			while (_queue.TryTake(out var action))
				action();
			PumpMessages();
		}
		catch (Exception error)
		{
			_started.TrySetException(error);
		}
	}

	private static void PumpMessages()
	{
		while (PeekMessage(out var message, 0, 0, 0, _removeMessage))
		{
			_ = TranslateMessage(ref message);
			_ = DispatchMessage(ref message);
		}
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PeekMessage(out NativeMessage message, nint window,
		uint minimum, uint maximum, uint removeMessage);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool TranslateMessage(ref NativeMessage message);

	[DllImport("user32.dll")]
	private static extern nint DispatchMessage(ref NativeMessage message);
}
