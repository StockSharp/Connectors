namespace StockSharp.Oanda.Native;

using System.IO;
using System.Net;
using System.Net.Http;

static class OandaStreamingNames
{
	public const string Pricing = "pricing";
	public const string Transactions = "transactions";
}

class OandaStreamingClient(bool isDemo, SecureString token, bool useCompression) : Disposable
{
	private class StreamingWorker<TResponse>
		where TResponse : IStreamingResponse
	{
		private enum States
		{
			Starting,
			Started,
			Stopping,
			Stopped,
		}

		private readonly OandaStreamingClient _parent;
		private readonly string _methodName;
		private readonly string _account;
		private readonly Action<QueryString> _fillQuery;
		private readonly Func<TResponse, CancellationToken, ValueTask> _newLine;
		private readonly Lock _stateLock = new();
		private States _currState = States.Stopped;
		private HttpClient _httpClient;
		private HttpRequestMessage _request;
		private CancellationTokenSource _cts;
		private Task _runTask;

		public StreamingWorker(OandaStreamingClient parent, string methodName, string account, Action<QueryString> fillQuery, Func<TResponse, CancellationToken, ValueTask> newLine)
		{
			if (account.IsEmpty())
				throw new ArgumentNullException(nameof(account));

			if (methodName.IsEmpty())
				throw new ArgumentNullException(nameof(methodName));

			_parent = parent ?? throw new ArgumentNullException(nameof(parent));
			_methodName = methodName;
			_account = account;
			_fillQuery = fillQuery;
			_newLine = newLine ?? throw new ArgumentNullException(nameof(newLine));
		}

		public void Start()
		{
			_request = null;

			using (_stateLock.EnterScope())
			{
				if (_currState != States.Stopped)
					throw new InvalidOperationException();

				_currState = States.Starting;
				_cts = new();

				var handler = new SocketsHttpHandler
				{
					AutomaticDecompression = _parent._useCompression ? DecompressionMethods.GZip | DecompressionMethods.Deflate : DecompressionMethods.None,
				};
				_httpClient = new HttpClient(handler, disposeHandler: true);
			}

			var token = _cts.Token;

			_runTask = Task.Run(async () =>
			{
				using (_stateLock.EnterScope())
				{
					if (_currState != States.Starting)
						return;

					_currState = States.Started;
				}

				var errorCount = 0;
				const int maxErrorCount = 10;

				while (!_parent.IsDisposed && !token.IsCancellationRequested)
				{
					using (_stateLock.EnterScope())
					{
						if (_currState != States.Started)
							break;
					}

					try
					{
						var url = new Url(_parent._streamingUrl + $"/v3/accounts/{_account}/{_methodName}/stream");
						_fillQuery?.Invoke(url.QueryString);

						var uri = new Uri(url.ToString());
						_request = new HttpRequestMessage(HttpMethod.Get, uri);

						if (_parent._token != null)
							_request.Headers.Authorization = new(AuthSchemas.Bearer, AuthSchemas.Bearer.FormatAuth(_parent._token));

						_request.Headers.TryAddWithoutValidation("Accept-Datetime-Format", "UNIX");
						if (_parent._useCompression)
							_request.Headers.TryAddWithoutValidation(HttpHeaders.AcceptEncoding, "gzip");

						using (_stateLock.EnterScope())
						{
							if (_currState != States.Started)
								break;
						}

						using var response = await _httpClient.SendAsync(_request, HttpCompletionOption.ResponseHeadersRead, token).NoWait();
						response.EnsureSuccessStatusCode();

						await using var responseStream = await response.Content.ReadAsStreamAsync(token).NoWait();
						if (responseStream == null)
							break;

						using var reader = new StreamReader(responseStream);

						var lineErrorCount = 0;
						const int maxLineErrorCount = 100;

						string line;
						while (!_parent.IsDisposed && !token.IsCancellationRequested && (line = await reader.ReadLineAsync(token).NoWait()) != null)
						{
							try
							{
								using (_stateLock.EnterScope())
								{
									if (_currState != States.Started)
										break;
								}

								_parent.Log?.Invoke(_methodName, line);

								var r = line.DeserializeObject<TResponse>();

								if (r != null && r.Type != "HEARTBEAT")
									await _newLine(r, token);

								lineErrorCount = 0;
							}
							catch (Exception ex)
							{
								var evt = _parent.NewError;
								if (evt is not null)
									await evt(ex, token);

								if (++lineErrorCount >= maxLineErrorCount)
								{
									//this.AddErrorLog("Max error {0} limit reached.", maxLineErrorCount);
									break;
								}
							}
						}
					}
					catch (Exception ex)
					{
						using (_stateLock.EnterScope())
						{
							if (_currState != States.Started)
								break;
						}

						var evt = _parent.NewError;
						if (evt is not null)
							await evt(ex, token);

						if (++errorCount >= maxErrorCount)
						{
							//this.AddErrorLog("Max error {0} limit reached.", maxErrorCount);
							break;
						}
					}
				}

				_request = null;

				using (_stateLock.EnterScope())
				{
					if (_currState == States.Stopping)
						_currState = States.Stopped;
				}
			}, token);
		}

		public void Stop()
		{
			using (_stateLock.EnterScope())
			{
				if (_currState == States.Started || _currState == States.Starting)
					_currState = States.Stopping;
			}

			try
			{
				_cts?.Cancel();
				_request?.Dispose();
				_httpClient?.Dispose();
				_runTask?.Wait(1000);
			}
			catch
			{
			}
		}
	}

	private readonly string _streamingUrl = isDemo ? "https://stream-fxpractice.oanda.com" : "https://stream-fxtrade.oanda.com";

	private readonly Dictionary<string, HashSet<string>> _instruments = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, StreamingWorker<StreamingPricingResponse>> _pricesWorkers = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, StreamingWorker<StreamingTransactionResponse>> _transactionsWorkers = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SecureString _token = token;
	private readonly bool _useCompression = useCompression;

	public event Func<Exception, CancellationToken, ValueTask> NewError;
	public event Action<string, string> Log;
	public event Func<StreamingPricingResponse, CancellationToken, ValueTask> NewPricing;
	public event Func<StreamingTransactionResponse, CancellationToken, ValueTask> NewTransaction;

	public void SubscribePricesStreaming(string accountId, string instrument)
	{
		var set = _instruments.SafeAdd(accountId, key => new HashSet<string>(StringComparer.InvariantCultureIgnoreCase));

		if (!set.Add(instrument))
			return;

		var worker = _pricesWorkers.TryGetValue(accountId);
		worker?.Stop();

		StartPriceWorker(accountId, set);
	}

	private void StartPriceWorker(string accountId, HashSet<string> set)
	{
		var instruments = set.JoinComma();

		_pricesWorkers[accountId] = new(this,
			OandaStreamingNames.Pricing, accountId,
			qs => qs.Append("instruments", instruments),
			(response, ct) => NewPricing?.Invoke(response, ct) ?? default);

		_pricesWorkers[accountId].Start();
	}

	public void UnSubscribePricesStreaming(string accountId, string instrument)
	{
		var set = _instruments.TryGetValue(accountId);

		if (set == null)
			return;

		set.Remove(instrument);

		if (set.Count == 0)
			_instruments.Remove(accountId);

		var worker = _pricesWorkers.TryGetValue(accountId);
		worker?.Stop();

		if (set.Count > 0)
			StartPriceWorker(accountId, set);
	}

	public void SubscribeTransactionsStreaming(string accountId)
	{
		_transactionsWorkers.SafeAdd(accountId, key => new(this,
			OandaStreamingNames.Transactions, key, null,
			(response, ct) => NewTransaction?.Invoke(response, ct) ?? default), out var isNew);

		if (isNew)
			_transactionsWorkers[accountId].Start();
	}

	public void UnSubscribeTransactionsStreaming(string accountId)
	{
		var worker = _transactionsWorkers.TryGetValue(accountId);
		
		if (worker == null)
			return;

		worker.Stop();
		_transactionsWorkers.Remove(accountId);
	}

	//protected override void DisposeManaged()
	//{
	//	_transactionsWorker.Stop();
	//	_pricesWorker.Stop();

	//	base.DisposeManaged();
	//}
}
