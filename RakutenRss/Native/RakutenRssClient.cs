namespace StockSharp.RakutenRss.Native;

[SupportedOSPlatform("windows")]
sealed class RakutenRssClient : Disposable
{
	private interface IWorkItem
	{
		void Execute(RakutenRssExcelSession session);
	}

	private sealed class WorkItem<T> : IWorkItem
	{
		private readonly Func<RakutenRssExcelSession, T> _action;
		private readonly TaskCompletionSource<T> _completion =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public WorkItem(Func<RakutenRssExcelSession, T> action)
			=> _action = action ?? throw new ArgumentNullException(nameof(action));

		public Task<T> Task => _completion.Task;

		public void Execute(RakutenRssExcelSession session)
		{
			try { _completion.TrySetResult(_action(session)); }
			catch (Exception error) { _completion.TrySetException(error); }
		}
	}

	private readonly BlockingCollection<IWorkItem> _queue = new();
	private readonly Thread _thread;
	private bool _isOpen;

	public RakutenRssClient()
	{
		_thread = new(Run)
		{
			IsBackground = true,
			Name = "StockSharp MARKETSPEED II RSS",
		};
		_thread.SetApartmentState(ApartmentState.STA);
		_thread.Start();
	}

	public async Task Open(bool visible, int maxRows, CancellationToken cancellationToken)
	{
		if (_isOpen)
			throw new InvalidOperationException("MARKETSPEED II RSS is already connected.");
		await Enqueue(session =>
		{
			session.Open(visible, maxRows);
			return true;
		}).WaitAsync(cancellationToken);
		_isOpen = true;
	}

	public Task<long> CreateQuoteFeed(RakutenRssQuoteRequest request)
		=> Enqueue(session => session.CreateQuoteFeed(request));

	public Task<RakutenRssQuote> ReadQuote(long feedId)
		=> Enqueue(session => session.ReadQuote(feedId));

	public Task<long> CreateTickFeed(RakutenRssTickRequest request)
		=> Enqueue(session => session.CreateTickFeed(request));

	public Task<RakutenRssTick[]> ReadTicks(long feedId)
		=> Enqueue(session => session.ReadTicks(feedId));

	public Task<long> CreateCandleFeed(RakutenRssCandleRequest request)
		=> Enqueue(session => session.CreateCandleFeed(request));

	public Task<RakutenRssCandle[]> ReadCandles(long feedId)
		=> Enqueue(session => session.ReadCandles(feedId));

	public Task<RakutenRssSecurityInfo> GetSecurity(string code, RakutenRssInstrumentKinds kind)
		=> Enqueue(session => session.GetSecurity(code, kind));

	public Task<RakutenRssOrderResult> PlaceOrder(RakutenRssPlaceOrderRequest request)
		=> Enqueue(session => session.PlaceOrder(request));

	public Task<RakutenRssOrderResult> ReplaceOrder(RakutenRssReplaceOrderRequest request)
		=> Enqueue(session => session.ReplaceOrder(request));

	public Task<RakutenRssOrderResult> CancelOrder(RakutenRssCancelOrderRequest request)
		=> Enqueue(session => session.CancelOrder(request));

	public Task<RakutenRssOrderIdRow[]> ReadOrderIds()
		=> Enqueue(session => session.ReadOrderIds());

	public Task<RakutenRssOrderRow[]> ReadOrders()
		=> Enqueue(session => session.ReadOrders());

	public Task<RakutenRssExecutionRow[]> ReadExecutions()
		=> Enqueue(session => session.ReadExecutions());

	public Task<RakutenRssPortfolioInfo> ReadPortfolio()
		=> Enqueue(session => session.ReadPortfolio());

	public Task RemoveFeed(long feedId)
		=> Enqueue(session =>
		{
			session.RemoveFeed(feedId);
			return true;
		});

	private Task<T> Enqueue<T>(Func<RakutenRssExcelSession, T> action)
	{
		if (_queue.IsAddingCompleted)
			throw new ObjectDisposedException(nameof(RakutenRssClient));
		var item = new WorkItem<T>(action);
		_queue.Add(item);
		return item.Task;
	}

	private void Run()
	{
		using var session = new RakutenRssExcelSession();
		foreach (var item in _queue.GetConsumingEnumerable())
			item.Execute(session);
	}

	protected override void DisposeManaged()
	{
		_queue.CompleteAdding();
		if (!_thread.Join(TimeSpan.FromSeconds(30)))
			throw new TimeoutException("Microsoft Excel did not stop within 30 seconds.");
		_queue.Dispose();
		base.DisposeManaged();
	}
}
