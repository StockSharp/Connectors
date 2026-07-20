namespace StockSharp.Avantis.Native;

sealed class AvantisFeedClient : BaseLogReceiver
{
	private const int _maximumMessageBytes = 1024 * 1024;
	private readonly Uri _lazerEndpoint;
	private readonly Uri _hermesEndpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	})
	{
		Timeout = Timeout.InfiniteTimeSpan,
	};
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _restart = new(1, 1);
	private readonly Dictionary<int, AvantisMarket> _subscriptions = [];
	private readonly CancellationTokenSource _lifetime = new();
	private CancellationTokenSource _streamCancellation;
	private Task _streamTask;

	public AvantisFeedClient(string lazerEndpoint, string hermesEndpoint)
	{
		_lazerEndpoint = CreateUri(lazerEndpoint, false,
			nameof(lazerEndpoint));
		_hermesEndpoint = CreateUri(hermesEndpoint, true,
			nameof(hermesEndpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Avantis-Connector/1.0");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Avantis_PriceFeed";

	public event Func<AvantisPriceUpdate, CancellationToken, ValueTask>
		PriceReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask SubscribeAsync(AvantisMarket market,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		using (_sync.EnterScope())
			_subscriptions[market.PairIndex] = market;
		await RestartAsync(cancellationToken);
	}

	public async ValueTask UnsubscribeAsync(int pairIndex,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			_subscriptions.Remove(pairIndex);
		await RestartAsync(cancellationToken);
	}

	public async ValueTask StopAsync(CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			_subscriptions.Clear();
		await RestartAsync(cancellationToken);
	}

	private async ValueTask RestartAsync(CancellationToken cancellationToken)
	{
		await _restart.WaitAsync(cancellationToken);
		try
		{
			CancellationTokenSource previousCancellation;
			Task previousTask;
			AvantisMarket[] markets;
			using (_sync.EnterScope())
			{
				previousCancellation = _streamCancellation;
				previousTask = _streamTask;
				_streamCancellation = null;
				_streamTask = null;
				markets = [.. _subscriptions.Values];
			}

			previousCancellation?.Cancel();
			if (previousTask is not null)
				try
				{
					await previousTask.WaitAsync(cancellationToken);
				}
				catch (OperationCanceledException) when (
					previousCancellation?.IsCancellationRequested == true)
				{
				}
			previousCancellation?.Dispose();

			if (markets.Length == 0 || _lifetime.IsCancellationRequested)
				return;

			var streamCancellation = CancellationTokenSource
				.CreateLinkedTokenSource(_lifetime.Token);
			var streamTask = RunStreamsAsync(markets,
				streamCancellation.Token);
			using (_sync.EnterScope())
			{
				_streamCancellation = streamCancellation;
				_streamTask = streamTask;
			}
		}
		finally
		{
			_restart.Release();
		}
	}

	private async Task RunStreamsAsync(AvantisMarket[] markets,
		CancellationToken cancellationToken)
	{
		var lazer = markets.Where(static market => market.IsLazerStable &&
			market.LazerFeedId is not null).ToArray();
		var hermes = markets.Where(static market => !market.IsLazerStable)
			.ToArray();
		var tasks = new List<Task>(2);
		if (lazer.Length > 0)
			tasks.Add(RunLazerLoopAsync(lazer, cancellationToken));
		if (hermes.Length > 0)
			tasks.Add(RunHermesLoopAsync(hermes, cancellationToken));
		if (tasks.Count > 0)
			await Task.WhenAll(tasks);
	}

	private async Task RunLazerLoopAsync(AvantisMarket[] markets,
		CancellationToken cancellationToken)
	{
		var attempt = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await RunLazerSessionAsync(markets, cancellationToken);
				attempt = 0;
			}
			catch (OperationCanceledException) when (
				cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
			if (!cancellationToken.IsCancellationRequested)
				await Task.Delay(GetReconnectDelay(attempt++),
					cancellationToken);
		}
	}

	private async Task RunLazerSessionAsync(AvantisMarket[] markets,
		CancellationToken cancellationToken)
	{
		var byFeed = markets.ToDictionary(
			static market => market.LazerFeedId.Value);
		var query = string.Join("&", byFeed.Keys.Select(static id =>
			"price_feed_ids=" + id.ToString(CultureInfo.InvariantCulture)));
		var uri = new Uri(_lazerEndpoint.AbsoluteUri +
			(_lazerEndpoint.Query.IsEmpty() ? "?" : "&") + query,
			UriKind.Absolute);
		using var request = new HttpRequestMessage(HttpMethod.Get, uri);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
			"text/event-stream"));
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException(
				"Pyth Lazer SSE HTTP " + (int)response.StatusCode + ".");
		await using var stream = await response.Content.ReadAsStreamAsync(
			cancellationToken);
		using var reader = new StreamReader(stream, Encoding.UTF8, false,
			8192, false);
		while (!cancellationToken.IsCancellationRequested)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line is null)
				throw new EndOfStreamException(
					"Pyth Lazer SSE stream closed.");
			if (!line.StartsWith("data:", StringComparison.Ordinal))
				continue;
			var payload = line[5..].Trim();
			if (payload.IsEmpty())
				continue;
			AvantisLazerPriceResponse update;
			try
			{
				update = JsonConvert.DeserializeObject<
					AvantisLazerPriceResponse>(payload, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Pyth Lazer returned an unexpected event shape.", error);
			}
			if (update?.Prices is null)
				continue;
			var time = update.TimestampMicroseconds.IsEmpty()
				? DateTime.UtcNow
				: update.TimestampMicroseconds.FromUnixMicrosecondsUtc();
			foreach (var price in update.Prices)
			{
				if (price is null || !byFeed.TryGetValue(price.FeedId,
					out var market))
					continue;
				await RaisePriceAsync(CreateLazerUpdate(market, price, time),
					cancellationToken);
			}
		}
	}

	private async Task RunHermesLoopAsync(AvantisMarket[] markets,
		CancellationToken cancellationToken)
	{
		var attempt = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await RunHermesSessionAsync(markets, cancellationToken);
				attempt = 0;
			}
			catch (OperationCanceledException) when (
				cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
			if (!cancellationToken.IsCancellationRequested)
				await Task.Delay(GetReconnectDelay(attempt++),
					cancellationToken);
		}
	}

	private async Task RunHermesSessionAsync(AvantisMarket[] markets,
		CancellationToken cancellationToken)
	{
		var byFeed = markets.ToDictionary(
			static market => market.FeedId.NormalizeFeedId(),
			StringComparer.OrdinalIgnoreCase);
		using var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		await socket.ConnectAsync(_hermesEndpoint, cancellationToken);
		var request = new AvantisHermesSubscribeRequest
		{
			Ids = [.. byFeed.Keys],
		};
		var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
			request, _jsonSettings));
		await socket.SendAsync(payload, WebSocketMessageType.Text, true,
			cancellationToken);

		while (socket.State == WebSocketState.Open &&
			!cancellationToken.IsCancellationRequested)
		{
			var message = await ReceiveAsync(socket, cancellationToken);
			if (message.IsEmpty())
				continue;
			AvantisHermesEnvelope envelope;
			try
			{
				envelope = JsonConvert.DeserializeObject<AvantisHermesEnvelope>(
					message, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Pyth Hermes returned an unexpected event shape.", error);
			}
			if (envelope?.Type != "price_update" ||
				envelope.PriceFeed?.Price is null ||
				envelope.PriceFeed.Id.IsEmpty())
				continue;
			var feedId = envelope.PriceFeed.Id.NormalizeFeedId();
			if (!byFeed.TryGetValue(feedId, out var market))
				continue;
			await RaisePriceAsync(CreateHermesUpdate(market,
				envelope.PriceFeed.Price), cancellationToken);
		}
	}

	private static AvantisPriceUpdate CreateLazerUpdate(AvantisMarket market,
		AvantisLazerPrice price, DateTime time)
	{
		var middle = price.Price.ApplyExponent(price.Exponent, "Lazer price");
		var confidence = price.Confidence.TryParseScaled(-price.Exponent);
		decimal? bid = price.Bid.IsEmpty()
			? confidence is decimal bidConfidence
				? middle - bidConfidence
				: null
			: price.Bid.ApplyExponent(price.Exponent, "Lazer bid");
		decimal? ask = price.Ask.IsEmpty()
			? confidence is decimal askConfidence
				? middle + askConfidence
				: null
			: price.Ask.ApplyExponent(price.Exponent, "Lazer ask");
		return new()
		{
			PairIndex = market.PairIndex,
			Time = time,
			Price = middle,
			Bid = bid,
			Ask = ask,
			Confidence = confidence,
		};
	}

	private static AvantisPriceUpdate CreateHermesUpdate(
		AvantisMarket market, AvantisHermesPrice price)
	{
		var middle = price.Price.ApplyExponent(price.Exponent, "Hermes price");
		var confidence = price.Confidence.TryParseScaled(-price.Exponent);
		return new()
		{
			PairIndex = market.PairIndex,
			Time = price.PublishTime > 0
				? price.PublishTime.FromUnix()
				: DateTime.UtcNow,
			Price = middle,
			Bid = confidence is decimal bidConfidence
				? middle - bidConfidence
				: null,
			Ask = confidence is decimal askConfidence
				? middle + askConfidence
				: null,
			Confidence = confidence,
		};
	}

	private async ValueTask RaisePriceAsync(AvantisPriceUpdate update,
		CancellationToken cancellationToken)
	{
		if (PriceReceived is { } handler)
			await handler(update, cancellationToken);
	}

	private async ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
	{
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static TimeSpan GetReconnectDelay(int attempt)
		=> TimeSpan.FromSeconds(Math.Min(10, 1 << Math.Min(attempt, 3)));

	private static Uri CreateUri(string endpoint, bool isWebSocket,
		string name)
	{
		endpoint = endpoint.ThrowIfEmpty(name).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			(isWebSocket
				? uri.Scheme is not ("ws" or "wss")
				: uri.Scheme is not ("http" or "https")))
			throw new ArgumentException(isWebSocket
				? "Pyth Hermes endpoint must use WS or WSS."
				: "Pyth Lazer endpoint must use HTTP or HTTPS.", name);
		return uri;
	}

	private static async ValueTask<string> ReceiveAsync(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		using var output = new MemoryStream();
		var block = new byte[8192];
		while (true)
		{
			var result = await socket.ReceiveAsync(block, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					"Pyth Hermes closed the WebSocket stream.");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					"Pyth Hermes returned a non-text WebSocket message.");
			if (output.Length + result.Count > _maximumMessageBytes)
				throw new InvalidDataException(
					"Pyth Hermes message exceeds the 1 MiB safety limit.");
			output.Write(block, 0, result.Count);
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(output.ToArray());
		}
	}

	protected override void DisposeManaged()
	{
		_lifetime.Cancel();
		_streamCancellation?.Cancel();
		_streamCancellation?.Dispose();
		_lifetime.Dispose();
		_restart.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
