namespace StockSharp.Groww.Native;

internal sealed class GrowwRestClient : BaseLogReceiver
{
	private static readonly Uri _apiRoot = new("https://api.groww.in/v1/");
	private static readonly Uri _instrumentUri = new("https://growwapi-assets.groww.in/instruments/instrument.csv");
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly HttpClient _http = new();
	private readonly string _configuredToken;
	private readonly string _apiKey;
	private readonly string _apiSecret;
	private readonly string _totpSecret;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _authenticationLock = new(1, 1);
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private GrowwInstrument[] _instruments;
	private string _accessToken;

	public GrowwRestClient(SecureString accessToken, SecureString apiKey, SecureString apiSecret, SecureString totpSecret, int maxAttempts)
	{
		_configuredToken = accessToken?.UnSecure();
		_apiKey = apiKey?.UnSecure();
		_apiSecret = apiSecret?.UnSecure();
		_totpSecret = totpSecret?.UnSecure();
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => nameof(Groww) + "_REST";

	public Task Connect(CancellationToken cancellationToken)
		=> EnsureAuthenticated(cancellationToken);

	public async Task<GrowwSocketToken> CreateSocketToken(string publicKey, CancellationToken cancellationToken)
		=> await Send<GrowwSocketToken>(HttpMethod.Post, "api/apex/v1/socket/token/create/",
			new GrowwSocketTokenRequest { SocketKey = publicKey.ThrowIfEmpty(nameof(publicKey)) }, cancellationToken);

	public Task<GrowwProfile> GetProfile(CancellationToken cancellationToken)
		=> Send<GrowwProfile>(HttpMethod.Get, "user/detail", null, cancellationToken);

	public Task<GrowwMargin> GetMargin(CancellationToken cancellationToken)
		=> Send<GrowwMargin>(HttpMethod.Get, "margins/detail/user", null, cancellationToken);

	public async Task<GrowwHolding[]> GetHoldings(CancellationToken cancellationToken)
		=> (await Send<GrowwHoldingsPayload>(HttpMethod.Get, "holdings/user", null, cancellationToken)).Holdings ?? [];

	public async Task<GrowwPosition[]> GetPositions(string segment, CancellationToken cancellationToken)
		=> (await Send<GrowwPositionsPayload>(HttpMethod.Get,
			$"positions/user?segment={Escape(segment.ThrowIfEmpty(nameof(segment)))}", null, cancellationToken)).Positions ?? [];

	public async Task<GrowwOrder[]> GetOrders(CancellationToken cancellationToken)
	{
		var result = new List<GrowwOrder>();
		for (var page = 0; page < 100; page++)
		{
			var items = (await Send<GrowwOrderListPayload>(HttpMethod.Get,
				$"order/list?page={page}&page_size=100", null, cancellationToken)).Orders ?? [];
			result.AddRange(items);
			if (items.Length < 100)
				break;
		}
		return [.. result.GroupBy(order => order.OrderId, StringComparer.OrdinalIgnoreCase).Select(group => group.Last())];
	}

	public async Task<GrowwTrade[]> GetTrades(string orderId, string segment, CancellationToken cancellationToken)
	{
		var result = new List<GrowwTrade>();
		for (var page = 0; page < 100; page++)
		{
			var path = $"order/trades/{Escape(orderId.ThrowIfEmpty(nameof(orderId)))}?segment={Escape(segment.ThrowIfEmpty(nameof(segment)))}&page={page}&page_size=50";
			var items = (await Send<GrowwTradeListPayload>(HttpMethod.Get, path, null, cancellationToken)).Trades ?? [];
			result.AddRange(items);
			if (items.Length < 50)
				break;
		}
		return [.. result];
	}

	public Task<GrowwOrderResult> PlaceOrder(GrowwPlaceOrderRequest request, CancellationToken cancellationToken)
		=> Send<GrowwOrderResult>(HttpMethod.Post, "order/create", request, cancellationToken);

	public Task<GrowwOrderResult> ModifyOrder(GrowwModifyOrderRequest request, CancellationToken cancellationToken)
		=> Send<GrowwOrderResult>(HttpMethod.Post, "order/modify", request, cancellationToken);

	public Task<GrowwOrderResult> CancelOrder(GrowwCancelOrderRequest request, CancellationToken cancellationToken)
		=> Send<GrowwOrderResult>(HttpMethod.Post, "order/cancel", request, cancellationToken);

	public async Task<GrowwCandle[]> GetCandles(GrowwSecurityInfo security, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		if (security.Segment.EqualsIgnoreCase("COMMODITY"))
			throw new NotSupportedException("Groww historical backtesting currently supports CASH and FNO segments only.");

		var (interval, maximumRange) = timeFrame.ToGrowwInterval();
		var end = (to ?? DateTime.UtcNow).ToUniversalTime();
		var start = (from ?? end - maximumRange).ToUniversalTime();
		if (start > end)
			return [];

		var result = new List<GrowwCandle>();
		while (start <= end)
		{
			var pageEnd = start + maximumRange;
			if (pageEnd > end)
				pageEnd = end;

			var path = "historical/candles" + Query(
				("exchange", security.Exchange),
				("segment", security.Segment),
				("groww_symbol", security.GrowwSymbol),
				("start_time", start.ToIndiaApiTime()),
				("end_time", pageEnd.ToIndiaApiTime()),
				("candle_interval", interval));
			var payload = await Send<GrowwCandlesPayload>(HttpMethod.Get, path, null, cancellationToken);
			result.AddRange(payload.Candles ?? []);

			if (pageEnd >= end)
				break;
			start = pageEnd.AddSeconds(1);
		}

		return [.. result.GroupBy(candle => candle.OpenTime).Select(group => group.Last()).OrderBy(candle => candle.OpenTime)];
	}

	public async Task<GrowwInstrument[]> GetInstruments(CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			using var response = await _http.GetAsync(_instrumentUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			response.EnsureSuccessStatusCode();
			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			using var reader = new StreamReader(stream, Encoding.UTF8, true);
			var header = ParseCsvLine(await reader.ReadLineAsync(cancellationToken));
			var columns = GrowwInstrumentColumns.Parse(header);
			var instruments = new List<GrowwInstrument>();
			while (await reader.ReadLineAsync(cancellationToken) is { } line)
			{
				if (line.IsEmpty())
					continue;
				var values = ParseCsvLine(line);
				var instrument = columns.Read(values);
				if (!instrument.TradingSymbol.IsEmpty() && !instrument.Exchange.IsEmpty())
					instruments.Add(instrument);
			}

			return _instruments = [.. instruments];
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_authenticationLock.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	private async Task EnsureAuthenticated(CancellationToken cancellationToken)
	{
		if (!_accessToken.IsEmpty())
			return;

		await _authenticationLock.WaitAsync(cancellationToken);
		try
		{
			if (!_accessToken.IsEmpty())
				return;

			if (!_configuredToken.IsEmpty())
			{
				_accessToken = _configuredToken;
				return;
			}

			_apiKey.ThrowIfEmpty(nameof(_apiKey));
			GrowwAccessTokenRequest body;
			if (!_apiSecret.IsEmpty())
			{
				var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				body = new()
				{
					KeyType = "approval",
					Timestamp = timestamp,
					Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_apiSecret + timestamp.ToString(CultureInfo.InvariantCulture)))).ToLowerInvariant(),
				};
			}
			else
			{
				_totpSecret.ThrowIfEmpty(nameof(_totpSecret));
				body = new()
				{
					KeyType = "totp",
					Totp = GenerateTotp(_totpSecret),
				};
			}

			var token = await SendDirect<GrowwAccessTokenResponse>(HttpMethod.Post, "token/api/access", body, _apiKey, cancellationToken);
			_accessToken = token.Token.ThrowIfEmpty(nameof(token.Token));
		}
		finally
		{
			_authenticationLock.Release();
		}
	}

	private async Task<T> Send<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
	{
		await EnsureAuthenticated(cancellationToken);

		for (var attempt = 1; ; attempt++)
		{
			try
			{
				using var response = await SendRequest(method, path, body, _accessToken, cancellationToken);
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				var envelope = JsonConvert.DeserializeObject<GrowwEnvelope<T>>(content, _jsonSettings);
				if (!response.IsSuccessStatusCode || envelope?.Status.EqualsIgnoreCase("FAILURE") == true)
					throw CreateException(response.StatusCode, envelope?.Error, content);
				return envelope is null
					? throw new InvalidOperationException("Groww returned an empty response.")
					: envelope.Payload;
			}
			catch (GrowwApiException ex) when (attempt < _maxAttempts && IsTransient(ex.StatusCode))
			{
				await Task.Delay(GetRetryDelay(attempt), cancellationToken);
			}
			catch (HttpRequestException) when (attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(attempt), cancellationToken);
			}
		}
	}

	private async Task<T> SendDirect<T>(HttpMethod method, string path, object body, string bearer, CancellationToken cancellationToken)
	{
		using var response = await SendRequest(method, path, body, bearer, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var error = JsonConvert.DeserializeObject<GrowwEnvelope<T>>(content, _jsonSettings)?.Error;
			throw CreateException(response.StatusCode, error, content);
		}
		return JsonConvert.DeserializeObject<T>(content, _jsonSettings)
			?? throw new InvalidOperationException("Groww returned an empty authentication response.");
	}

	private async Task<HttpResponseMessage> SendRequest(HttpMethod method, string path, object body, string bearer, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, new Uri(_apiRoot, path));
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.ThrowIfEmpty(nameof(bearer)));
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		request.Headers.TryAddWithoutValidation("X-API-VERSION", "1.0");
		request.Headers.TryAddWithoutValidation("x-request-id", Guid.NewGuid().ToString());
		request.Headers.TryAddWithoutValidation("x-client-id", "stocksharp");
		request.Headers.TryAddWithoutValidation("x-client-platform", "stocksharp-dotnet");
		if (body != null)
			request.Content = new StringContent(JsonConvert.SerializeObject(body, _jsonSettings), Encoding.UTF8, "application/json");
		return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
	}

	private static GrowwApiException CreateException(HttpStatusCode statusCode, GrowwError error, string content)
		=> new(statusCode, error?.Code, error?.DisplayMessage.IsEmpty(error?.Message).IsEmpty(content).IsEmpty(statusCode.ToString()));

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
			HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

	private static TimeSpan GetRetryDelay(int attempt)
		=> TimeSpan.FromMilliseconds(Math.Min(5000, 250 * Math.Pow(2, attempt - 1)));

	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

	private static string Query(params (string key, string value)[] values)
		=> "?" + string.Join("&", values.Select(pair => $"{Escape(pair.key)}={Escape(pair.value)}"));

	private static string GenerateTotp(string base32Secret)
	{
		var key = DecodeBase32(base32Secret);
		var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
		Span<byte> counterBytes = stackalloc byte[8];
		for (var index = 7; index >= 0; index--)
		{
			counterBytes[index] = (byte)counter;
			counter >>= 8;
		}
		var hash = HMACSHA1.HashData(key, counterBytes);
		var offset = hash[^1] & 0x0f;
		var code = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
		return (code % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
	}

	private static byte[] DecodeBase32(string value)
	{
		value = new string(value.Where(ch => !char.IsWhiteSpace(ch) && ch != '-').Select(char.ToUpperInvariant).ToArray()).TrimEnd('=');
		if (value.IsEmpty())
			throw new ArgumentException("TOTP secret is empty.", nameof(value));

		var output = new List<byte>(value.Length * 5 / 8);
		var buffer = 0;
		var bits = 0;
		foreach (var ch in value)
		{
			var digit = ch is >= 'A' and <= 'Z' ? ch - 'A' : ch is >= '2' and <= '7' ? ch - '2' + 26 : -1;
			if (digit < 0)
				throw new FormatException("TOTP secret is not valid Base32.");
			buffer = (buffer << 5) | digit;
			bits += 5;
			if (bits < 8)
				continue;
			bits -= 8;
			output.Add((byte)(buffer >> bits));
			buffer &= (1 << bits) - 1;
		}
		return [.. output];
	}

	private static string[] ParseCsvLine(string line)
	{
		if (line is null)
			return [];
		var values = new List<string>();
		var value = new StringBuilder();
		var quoted = false;
		for (var index = 0; index < line.Length; index++)
		{
			var ch = line[index];
			if (ch == '"')
			{
				if (quoted && index + 1 < line.Length && line[index + 1] == '"')
				{
					value.Append('"');
					index++;
				}
				else
					quoted = !quoted;
			}
			else if (ch == ',' && !quoted)
			{
				values.Add(value.ToString());
				value.Clear();
			}
			else
				value.Append(ch);
		}
		values.Add(value.ToString());
		return [.. values];
	}

	private sealed class GrowwInstrumentColumns
	{
		private int Exchange { get; set; } = -1;
		private int ExchangeToken { get; set; } = -1;
		private int TradingSymbol { get; set; } = -1;
		private int GrowwSymbol { get; set; } = -1;
		private int Name { get; set; } = -1;
		private int InstrumentType { get; set; } = -1;
		private int Segment { get; set; } = -1;
		private int Series { get; set; } = -1;
		private int Isin { get; set; } = -1;
		private int UnderlyingSymbol { get; set; } = -1;
		private int UnderlyingExchangeToken { get; set; } = -1;
		private int ExpiryDate { get; set; } = -1;
		private int StrikePrice { get; set; } = -1;
		private int LotSize { get; set; } = -1;
		private int TickSize { get; set; } = -1;
		private int FreezeQuantity { get; set; } = -1;
		private int IsReserved { get; set; } = -1;
		private int BuyAllowed { get; set; } = -1;
		private int SellAllowed { get; set; } = -1;

		public static GrowwInstrumentColumns Parse(string[] header)
		{
			var result = new GrowwInstrumentColumns();
			for (var index = 0; index < header.Length; index++)
			{
				switch (header[index].Trim().TrimStart('\ufeff').ToLowerInvariant())
				{
					case "exchange": result.Exchange = index; break;
					case "exchange_token": result.ExchangeToken = index; break;
					case "trading_symbol": result.TradingSymbol = index; break;
					case "groww_symbol": result.GrowwSymbol = index; break;
					case "name": result.Name = index; break;
					case "instrument_type": result.InstrumentType = index; break;
					case "segment": result.Segment = index; break;
					case "series": result.Series = index; break;
					case "isin": result.Isin = index; break;
					case "underlying_symbol": result.UnderlyingSymbol = index; break;
					case "underlying_exchange_token": result.UnderlyingExchangeToken = index; break;
					case "expiry_date": result.ExpiryDate = index; break;
					case "strike_price": result.StrikePrice = index; break;
					case "lot_size": result.LotSize = index; break;
					case "tick_size": result.TickSize = index; break;
					case "freeze_quantity": result.FreezeQuantity = index; break;
					case "is_reserved": result.IsReserved = index; break;
					case "buy_allowed": result.BuyAllowed = index; break;
					case "sell_allowed": result.SellAllowed = index; break;
				}
			}
			if (result.Exchange < 0 || result.ExchangeToken < 0 || result.TradingSymbol < 0 || result.Segment < 0)
				throw new FormatException("Groww instrument CSV is missing required columns.");
			return result;
		}

		public GrowwInstrument Read(string[] values)
			=> new()
			{
				Exchange = Get(values, Exchange),
				ExchangeToken = Get(values, ExchangeToken),
				TradingSymbol = Get(values, TradingSymbol),
				GrowwSymbol = Get(values, GrowwSymbol),
				Name = Get(values, Name),
				InstrumentType = Get(values, InstrumentType),
				Segment = Get(values, Segment),
				Series = Get(values, Series),
				Isin = Get(values, Isin),
				UnderlyingSymbol = Get(values, UnderlyingSymbol),
				UnderlyingExchangeToken = Get(values, UnderlyingExchangeToken),
				ExpiryDate = ParseDate(Get(values, ExpiryDate)),
				StrikePrice = ParseDecimal(Get(values, StrikePrice)),
				LotSize = ParseDecimal(Get(values, LotSize)),
				TickSize = ParseDecimal(Get(values, TickSize)),
				FreezeQuantity = ParseDecimal(Get(values, FreezeQuantity)),
				IsReserved = ParseBool(Get(values, IsReserved)),
				IsBuyAllowed = ParseBool(Get(values, BuyAllowed)),
				IsSellAllowed = ParseBool(Get(values, SellAllowed)),
			};

		private static string Get(string[] values, int index)
			=> index >= 0 && index < values.Length ? values[index].Trim() : null;

		private static decimal? ParseDecimal(string value)
			=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

		private static DateTime? ParseDate(string value)
			=> DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
				? DateTime.SpecifyKind(result, DateTimeKind.Utc)
				: null;

		private static bool ParseBool(string value)
			=> value == "1" || value.EqualsIgnoreCase("true") || value.EqualsIgnoreCase("yes");
	}
}
