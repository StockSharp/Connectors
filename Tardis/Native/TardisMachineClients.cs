namespace StockSharp.Tardis.Native;

sealed class TardisMachineRestClient : BaseLogReceiver
{
	private const int _maximumErrorLength = 64 * 1024;
	private readonly HttpClient _client;
	private readonly JsonSerializerSettings _settings = TardisProtocol.CreateSettings();
	private bool _isDisposed;

	public TardisMachineRestClient(string endpoint)
	{
		_client = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip |
				DecompressionMethods.Deflate,
		})
		{
			BaseAddress = ValidateHttpEndpoint(endpoint),
			Timeout = Timeout.InfiniteTimeSpan,
		};
		_client.DefaultRequestHeaders.Accept.ParseAdd(
			"application/x-ndjson, application/json");
		_client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Tardis-Machine/1.0");
	}

	public override string Name => "Tardis_Machine_HTTP";

	public async IAsyncEnumerable<TardisNormalizedMessage> ReplayAsync(
		string exchange, TardisStreamKey key, DateTime from, DateTime to,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		if (from >= to)
			throw new ArgumentOutOfRangeException(nameof(from), from,
				"Tardis replay start time must be earlier than its end time.");

		var options = TardisProtocol.CreateOptions(exchange, key, from, to, null);
		var path = "replay-normalized?options=" +
			Uri.EscapeDataString(JsonConvert.SerializeObject(options, _settings));
		if (path.Length > 10000)
			throw new ArgumentException("Tardis replay URL is too long.",
				nameof(key));

		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		using var response = await _client.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw await CreateExceptionAsync(response, cancellationToken);

		await using var stream = await response.Content.ReadAsStreamAsync(
			cancellationToken);
		await foreach (var line in TardisProtocol.ReadLinesAsync(stream,
			cancellationToken))
		{
			if (!line.IsEmpty())
				yield return TardisProtocol.Deserialize(line, _settings);
		}
	}

	private static async ValueTask<InvalidOperationException>
		CreateExceptionAsync(HttpResponseMessage response,
			CancellationToken cancellationToken)
	{
		var details = response.StatusCode.ToString();
		var bytes = await ReadErrorAsync(response.Content, cancellationToken);
		if (bytes.Length > 0)
		{
			try
			{
				var error = JsonConvert.DeserializeObject<TardisMachineError>(
					Encoding.UTF8.GetString(bytes));
				if (error is not null && !error.Details.IsEmpty())
					details = error.Details;
			}
			catch (JsonException)
			{
			}
		}
		return new InvalidOperationException(
			$"Tardis Machine replay failed ({(int)response.StatusCode}): {details}");
	}

	private static async ValueTask<byte[]> ReadErrorAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumErrorLength)
			return [];
		await using var stream = await content.ReadAsStreamAsync(cancellationToken);
		using var buffer = new MemoryStream();
		var chunk = new byte[8192];
		while (true)
		{
			var read = await stream.ReadAsync(chunk, cancellationToken);
			if (read == 0)
				break;
			if (buffer.Length + read > _maximumErrorLength)
				return [];
			buffer.Write(chunk, 0, read);
		}
		return buffer.ToArray();
	}

	internal static Uri ValidateHttpEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttp &&
			uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty() || !uri.Query.IsEmpty() ||
			!uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"Tardis Machine HTTP endpoint must be an absolute HTTP(S) URI without credentials, query, or fragment.",
				nameof(endpoint));
		return uri;
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_client.Dispose();
		base.DisposeManaged();
	}
}

sealed class TardisMachineStreamClient : BaseLogReceiver
{
	private readonly TardisStreamKey _key;
	private readonly string _exchange;
	private readonly Uri _uri;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly JsonSerializerSettings _settings = TardisProtocol.CreateSettings();
	private WebSocketClient _client;
	private bool _isDisposed;

	public TardisMachineStreamClient(string endpoint, string exchange,
		TardisStreamKey key, TimeSpan timeoutInterval, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_exchange = TardisExtensions.NormalizeExchange(exchange);
		_key = ValidateKey(key);
		if (timeoutInterval < TimeSpan.FromSeconds(10) ||
			timeoutInterval > TimeSpan.FromMinutes(10))
			throw new ArgumentOutOfRangeException(nameof(timeoutInterval));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = Math.Max(1, reconnectAttempts);
		_uri = BuildUri(endpoint, _exchange, _key, timeoutInterval, _settings);
	}

	public override string Name => "Tardis_Machine_WS_" + _key.Kind;

	public event Func<TardisStreamUpdate, CancellationToken, ValueTask>
		MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_client is not null)
			throw new InvalidOperationException(
				"Tardis Machine stream is already connected.");
		var client = new WebSocketClient(_uri.AbsoluteUri,
			OnStateChangedAsync, RaiseErrorAsync, OnProcessAsync,
			static (_, _) => { }, static (_, _) => { }, static (_, _) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		_client = client;
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch (Exception error)
		{
			_client = null;
			client.Dispose();
			if (cancellationToken.IsCancellationRequested)
				throw;
			throw new IOException(
				"Tardis Machine WebSocket connection failed: " + error.Message);
		}
	}

	private ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		this.AddInfoLog("Tardis Machine {0} stream state: {1}.",
			_key.Kind, state);
		return default;
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var json = message.AsString();
		if (json.IsEmpty())
			return;
		try
		{
			var update = TardisProtocol.Deserialize(json, _settings);
			if (update is TardisMachineError error)
				throw new InvalidOperationException(
					"Tardis Machine stream error: " +
					error.Details.IsEmpty("unspecified error"));
			if (update is TardisDisconnect)
			{
				this.AddWarningLog(
					"Tardis reported an underlying {0} exchange disconnect.",
					_exchange);
				return;
			}
			ValidateMessage(update);
			if (MessageReceived is { } handler)
				await handler(new(_key, update), cancellationToken);
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			ArgumentException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void ValidateMessage(TardisNormalizedMessage message)
	{
		if (message is null || !message.Exchange.EqualsIgnoreCase(_exchange) ||
			!message.Symbol.EqualsIgnoreCase(_key.Symbol))
			throw new InvalidDataException(
				"Tardis Machine returned data for a different exchange or symbol.");
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(new IOException(error.Message), cancellationToken)
			: default;

	private static TardisStreamKey ValidateKey(TardisStreamKey key)
	{
		if (key.Kind == TardisStreamKinds.Unknown || key.Symbol.IsEmpty())
			throw new ArgumentException("Tardis stream key is incomplete.",
				nameof(key));
		if (key.Symbol.Length > 512 || key.Symbol.Any(char.IsControl))
			throw new ArgumentException("Tardis stream symbol is invalid.",
				nameof(key));
		_ = key.ToDataTypes();
		return key;
	}

	private static Uri BuildUri(string endpoint, string exchange,
		TardisStreamKey key, TimeSpan timeoutInterval,
		JsonSerializerSettings settings)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var root) || root.Scheme is not ("ws" or "wss") ||
			root.Host.IsEmpty() || !root.UserInfo.IsEmpty() ||
			!root.Query.IsEmpty() || !root.Fragment.IsEmpty())
			throw new ArgumentException(
				"Tardis Machine WebSocket endpoint must be an absolute WS(S) URI without credentials, query, or fragment.",
				nameof(endpoint));
		var options = TardisProtocol.CreateOptions(exchange, key, null, null,
			checked((int)timeoutInterval.TotalMilliseconds));
		var json = JsonConvert.SerializeObject(options, settings);
		var uri = new Uri(root, "ws-stream-normalized?options=" +
			Uri.EscapeDataString(json));
		if (uri.AbsoluteUri.Length > 10000)
			throw new ArgumentException("Tardis stream URL is too long.",
				nameof(key));
		return uri;
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}

readonly record struct TardisStreamUpdate(TardisStreamKey Key,
	TardisNormalizedMessage Message);

static class TardisProtocol
{
	private const int _maximumLineLength = 16 * 1024 * 1024;
	private static readonly Encoding _strictUtf8 = new UTF8Encoding(false, true);

	public static JsonSerializerSettings CreateSettings()
		=> new()
		{
			Culture = CultureInfo.InvariantCulture,
			DateParseHandling = DateParseHandling.None,
			FloatParseHandling = FloatParseHandling.Decimal,
			Formatting = Formatting.None,
			NullValueHandling = NullValueHandling.Ignore,
		};

	public static TardisMachineOptions CreateOptions(string exchange,
		TardisStreamKey key, DateTime? from, DateTime? to,
		int? timeoutIntervalMilliseconds)
	{
		exchange = TardisExtensions.NormalizeExchange(exchange);
		if (key.Symbol.IsEmpty())
			throw new ArgumentException("Tardis symbol is missing.", nameof(key));
		return new()
		{
			Exchange = exchange,
			Symbols = [key.Symbol],
			From = from?.FormatTardisTime(),
			To = to?.FormatTardisTime(),
			DataTypes = key.ToDataTypes(),
			IsWithDisconnectMessages = true,
			IsWithErrorMessages = timeoutIntervalMilliseconds is not null
				? true
				: null,
			TimeoutIntervalMilliseconds = timeoutIntervalMilliseconds,
		};
	}

	public static TardisNormalizedMessage Deserialize(string json,
		JsonSerializerSettings settings)
	{
		if (json.IsEmpty())
			throw new InvalidDataException(
				"Tardis Machine returned an empty JSON message.");
		var header = JsonConvert.DeserializeObject<TardisNormalizedMessage>(
			json, settings) ?? throw new InvalidDataException(
				"Tardis Machine returned an empty JSON object.");
		return header.Type switch
		{
			TardisMessageTypes.Trade => Deserialize<TardisTrade>(json, settings),
			TardisMessageTypes.BookChange =>
				Deserialize<TardisBookChange>(json, settings),
			TardisMessageTypes.BookTicker =>
				Deserialize<TardisBookTicker>(json, settings),
			TardisMessageTypes.DerivativeTicker =>
				Deserialize<TardisDerivativeTicker>(json, settings),
			TardisMessageTypes.BookSnapshot =>
				Deserialize<TardisBookSnapshot>(json, settings),
			TardisMessageTypes.TradeBar =>
				Deserialize<TardisTradeBar>(json, settings),
			TardisMessageTypes.Disconnect =>
				Deserialize<TardisDisconnect>(json, settings),
			TardisMessageTypes.Error =>
				Deserialize<TardisMachineError>(json, settings),
			_ => throw new InvalidDataException(
				$"Tardis Machine returned unsupported message type '{header.Type}'."),
		};
	}

	private static T Deserialize<T>(string json,
		JsonSerializerSettings settings)
		=> JsonConvert.DeserializeObject<T>(json, settings) ??
			throw new InvalidDataException(
				"Tardis Machine returned an empty normalized message.");

	public static async IAsyncEnumerable<string> ReadLinesAsync(Stream stream,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(stream);
		var chunk = new byte[81920];
		using var line = new MemoryStream();
		while (true)
		{
			var read = await stream.ReadAsync(chunk, cancellationToken);
			if (read == 0)
				break;
			for (var index = 0; index < read; index++)
			{
				var value = chunk[index];
				if (value == (byte)'\n')
				{
					yield return DecodeLine(line);
					line.SetLength(0);
					continue;
				}
				if (line.Length >= _maximumLineLength)
					throw new InvalidDataException(
						"Tardis Machine NDJSON line exceeds 16 MiB.");
				line.WriteByte(value);
			}
		}
		if (line.Length > 0)
			yield return DecodeLine(line);
	}

	private static string DecodeLine(MemoryStream line)
	{
		var length = checked((int)line.Length);
		var buffer = line.GetBuffer();
		if (length > 0 && buffer[length - 1] == (byte)'\r')
			length--;
		try
		{
			return _strictUtf8.GetString(buffer, 0, length);
		}
		catch (DecoderFallbackException error)
		{
			throw new InvalidDataException(
				"Tardis Machine returned invalid UTF-8.", error);
		}
	}
}
