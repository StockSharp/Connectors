namespace StockSharp.Nubra.Native;

sealed class NubraRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly HttpClient _httpClient;
	private readonly string _deviceId;
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private NubraInstrument[] _instruments;
	private IReadOnlyDictionary<long, NubraInstrument> _instrumentsById;
	private IReadOnlyDictionary<string, NubraInstrument> _instrumentsBySymbol;
	private string _token;

	public NubraRestClient(
		Uri restAddress,
		string deviceId,
		SecureString token = null,
		HttpMessageHandler handler = null)
	{
		_deviceId = deviceId.ThrowIfEmpty(nameof(deviceId));
		_httpClient = handler == null ? new() : new(handler);
		_httpClient.BaseAddress =
			restAddress ?? throw new ArgumentNullException(nameof(restAddress));
		_httpClient.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Nubra/1.0");
		SetToken(token);
	}

	public override string Name => nameof(Nubra) + "_" + nameof(NubraRestClient);

	public string Token => _token;

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public void SetToken(SecureString token)
		=> _token = token.IsEmpty() ? null : token.UnSecure();

	public async Task<NubraLoginResult> LoginWithTotp(
		string phone,
		SecureString mpin,
		SecureString totpSecret,
		DateTime utcNow,
		CancellationToken cancellationToken)
	{
		phone.ThrowIfEmpty(nameof(phone));
		var pin = mpin.ThrowIfEmpty(nameof(mpin)).UnSecure();
		var totp = GenerateTotp(
			totpSecret.ThrowIfEmpty(nameof(totpSecret)).UnSecure(),
			utcNow)
			.ToString("D6", CultureInfo.InvariantCulture);

		var login = await Send(
			"totp/login",
			HttpMethod.Post,
			new
			{
				phone,
				totp,
				otp = string.Empty,
			},
			false,
			null,
			cancellationToken);
		var authToken = FindString(login, "auth_token")
			.ThrowIfEmpty("Nubra auth token");
		var verification = await Send(
			"verifypin",
			HttpMethod.Post,
			new { pin },
			false,
			authToken,
			cancellationToken);
		var sessionToken = FindString(verification, "session_token", "token")
			.ThrowIfEmpty("Nubra session token");
		_token = sessionToken;

		return new()
		{
			SessionToken = sessionToken,
			UserId = FindString(verification, "userId", "user_id"),
		};
	}

	public async Task<NubraUserInfo> GetUserInfo(
		CancellationToken cancellationToken)
	{
		var response = await Send(
			"userinfo",
			HttpMethod.Get,
			null,
			true,
			null,
			cancellationToken);
		var webSocket = FindString(
			response,
			"user_ws_url",
			"userWsUrl",
			"websocket_url");

		return new()
		{
			ClientCode = FindString(
				response,
				"clientCode",
				"client_code",
				"exchange_client_code",
				"userId"),
			UserWebSocketAddress =
				Uri.TryCreate(webSocket, UriKind.Absolute, out var address)
					? address
					: null,
		};
	}

	public async Task<NubraInstrument[]> GetInstruments(
		DateTime referenceDate,
		CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			var instruments = new List<NubraInstrument>();

			foreach (var exchange in new[] { "NSE", "BSE", "MCX" })
			{
				var path =
					$"refdata/refdata/{referenceDate:yyyy-MM-dd}?exchange={exchange}";
				var response = await Send(
					path,
					HttpMethod.Get,
					null,
					true,
					null,
					cancellationToken);
				instruments.AddRange(ParseInstruments(exchange, response));
			}

			_instruments =
			[
				..
				instruments
					.Where(instrument => instrument.RefId > 0)
					.GroupBy(instrument => instrument.RefId)
					.Select(group => group.Last())
			];
			if (_instruments.Length == 0)
				throw new InvalidDataException("Nubra instrument masters contained no instruments.");

			_instrumentsById = _instruments.ToDictionary(
				instrument => instrument.RefId);
			_instrumentsBySymbol = _instruments
				.Where(instrument =>
					!instrument.Exchange.IsEmpty() &&
					!instrument.StockName.IsEmpty())
				.GroupBy(
					instrument => SymbolKey(
						instrument.Exchange,
						instrument.StockName),
					StringComparer.OrdinalIgnoreCase)
				.ToDictionary(
					group => group.Key,
					group => group.First(),
					StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<NubraInstrument> GetInstrument(
		long refId,
		DateTime referenceDate,
		CancellationToken cancellationToken)
	{
		await GetInstruments(referenceDate, cancellationToken);
		return _instrumentsById.TryGetValue(refId, out var instrument)
			? instrument
			: null;
	}

	public async Task<NubraInstrument> FindInstrument(
		string exchange,
		string symbol,
		DateTime referenceDate,
		CancellationToken cancellationToken)
	{
		await GetInstruments(referenceDate, cancellationToken);
		return _instrumentsBySymbol.TryGetValue(
			SymbolKey(exchange, symbol),
			out var instrument)
				? instrument
				: null;
	}

	public async Task<NubraMarketUpdate> GetMarketUpdate(
		long refId,
		int depth,
		CancellationToken cancellationToken)
	{
		var update = ParseMarketUpdate(
			await Send(
				$"orderbooks/{refId}?levels={depth}",
				HttpMethod.Get,
				null,
				true,
				null,
				cancellationToken));
		if (update.RefId != 0)
			return update;

		return new()
		{
			RefId = refId,
			Timestamp = update.Timestamp,
			LastPrice = update.LastPrice,
			LastQuantity = update.LastQuantity,
			Volume = update.Volume,
			Bids = update.Bids,
			Asks = update.Asks,
		};
	}

	public async Task<NubraCandle[]> GetCandles(
		NubraInstrument instrument,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		var body = new
		{
			query = new[]
			{
				new
				{
					exchange = instrument.Exchange,
					type = instrument.ToSecurityType().ToChartType(),
					values = new[] { instrument.StockName },
					fields = new[]
					{
						"open",
						"high",
						"low",
						"close",
						"tick_volume",
						"cumulative_volume",
					},
					startDate = from.ToUniversalTime().ToString(
						"yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
						CultureInfo.InvariantCulture),
					endDate = to.ToUniversalTime().ToString(
						"yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
						CultureInfo.InvariantCulture),
					interval = timeFrame.ToNativeInterval(),
					intraDay = false,
					realTime = false,
				},
			},
		};
		var response = await Send(
			"charts/timeseries",
			HttpMethod.Post,
			body,
			true,
			null,
			cancellationToken);
		return ParseCandles(response, instrument.StockName);
	}

	public async Task<NubraOrder> PlaceOrder(
		JObject order,
		CancellationToken cancellationToken)
	{
		var response = await Send(
			"sentinel/orders/create",
			HttpMethod.Post,
			new JObject { ["orders"] = new JArray(order) },
			true,
			null,
			cancellationToken);
		return ParseOrders(response).FirstOrDefault() ??
			throw new InvalidDataException(
				"Nubra create-order response contained no order.");
	}

	public async Task<NubraOrder> ModifyOrder(
		JObject order,
		CancellationToken cancellationToken)
	{
		var response = await Send(
			"sentinel/orders/modify",
			HttpMethod.Post,
			new JObject { ["orders"] = new JArray(order) },
			true,
			null,
			cancellationToken);
		return ParseOrders(response).FirstOrDefault();
	}

	public Task CancelOrder(long orderId, CancellationToken cancellationToken)
		=> Send(
			"sentinel/orders/cancel",
			HttpMethod.Post,
			new
			{
				orders = new[]
				{
					new { orderId },
				},
			},
			true,
			null,
			cancellationToken);

	public async Task<NubraOrder[]> GetOrders(
		CancellationToken cancellationToken)
		=> ParseOrders(
			await Send(
				"sentinel/orders",
				HttpMethod.Get,
				null,
				true,
				null,
				cancellationToken));

	public async Task<NubraPositionEnvelope> GetPositions(
		CancellationToken cancellationToken)
		=> (await Send(
				"sentinel/portfolio/positions",
				HttpMethod.Get,
				null,
				true,
				null,
				cancellationToken))
			.ToObject<NubraPositionEnvelope>() ?? new();

	public async Task<NubraHoldingEnvelope> GetHoldings(
		CancellationToken cancellationToken)
		=> (await Send(
				"sentinel/portfolio/holdings",
				HttpMethod.Get,
				null,
				true,
				null,
				cancellationToken))
			.ToObject<NubraHoldingEnvelope>() ?? new();

	public async Task<NubraFundsEnvelope> GetFunds(
		CancellationToken cancellationToken)
		=> (await Send(
				"sentinel/portfolio/user_funds_and_margin",
				HttpMethod.Get,
				null,
				true,
				null,
				cancellationToken))
			.ToObject<NubraFundsEnvelope>() ?? new();

	internal static int GenerateTotp(string secret, DateTime utcNow)
	{
		var key = DecodeBase32(secret);
		var counter = (ulong)new DateTimeOffset(
			utcNow.Kind == DateTimeKind.Utc
				? utcNow
				: utcNow.ToUniversalTime()).ToUnixTimeSeconds() / 30UL;
		Span<byte> counterBytes = stackalloc byte[8];
		for (var index = counterBytes.Length - 1; index >= 0; index--)
		{
			counterBytes[index] = (byte)(counter & 0xff);
			counter >>= 8;
		}

		var hash = HMACSHA1.HashData(key, counterBytes);
		var offset = hash[^1] & 0x0f;
		var binary =
			((hash[offset] & 0x7f) << 24) |
			((hash[offset + 1] & 0xff) << 16) |
			((hash[offset + 2] & 0xff) << 8) |
			(hash[offset + 3] & 0xff);
		return binary % 1_000_000;
	}

	internal static NubraInstrument[] ParseInstruments(
		string exchange,
		JToken response)
	{
		var envelope = response?.ToObject<NubraInstrumentEnvelope>() ??
			throw new InvalidDataException(
				$"Nubra {exchange} instrument response is empty.");
		var instruments = envelope.Instruments ?? [];
		foreach (var instrument in instruments)
		{
			instrument.Exchange = instrument.Exchange
				.IsEmpty(envelope.Exchange)
				.IsEmpty(exchange)
				.ToUpperInvariant();
			instrument.StockName = instrument.StockName?.Trim();
			instrument.Asset = instrument.Asset?.Trim();
			instrument.Isin = instrument.Isin?.Trim();
			if (instrument.Isin.EqualsIgnoreCase("N/A"))
				instrument.Isin = null;
		}
		return instruments;
	}

	internal static NubraMarketUpdate ParseMarketUpdate(JToken response)
	{
		var orderBook = response?["orderBook"] ?? response?["orderbook"] ??
			throw new InvalidDataException(
				"Nubra market-quote response contained no orderBook.");
		return new()
		{
			RefId = orderBook.Value<long?>("ref_id") ??
				orderBook.Value<long?>("refId") ?? 0,
			Timestamp = orderBook.Value<long?>("ts") ??
				orderBook.Value<long?>("timestamp") ?? 0,
			LastPrice = orderBook.Value<long?>("ltp") ?? 0,
			LastQuantity = orderBook.Value<long?>("ltq") ?? 0,
			Volume = orderBook.Value<long?>("volume") ?? 0,
			Bids = (orderBook["bid"] ?? orderBook["bids"])
				?.ToObject<NubraDepthLevel[]>() ?? [],
			Asks = (orderBook["ask"] ?? orderBook["asks"])
				?.ToObject<NubraDepthLevel[]>() ?? [],
		};
	}

	internal static NubraCandle[] ParseCandles(
		JToken response,
		string symbol)
	{
		var fields = response?["result"]?
			.Children()
			.SelectMany(item => item["values"]?.Children() ?? [])
			.Select(item => item[symbol] ?? item.Children<JProperty>()
				.FirstOrDefault()?.Value)
			.OfType<JObject>()
			.FirstOrDefault();
		if (fields == null)
			return [];

		var values = new Dictionary<long, long?[]>();
		foreach (var property in fields.Properties())
		{
			var index = property.Name.ToLowerInvariant() switch
			{
				"open" => 0,
				"high" => 1,
				"low" => 2,
				"close" => 3,
				"tick_volume" => 4,
				"cumulative_volume" => 5,
				_ => -1,
			};
			if (index < 0 || property.Value is not JArray points)
				continue;

			foreach (var point in points)
			{
				var timestamp = point.Value<long?>("ts") ??
					point.Value<long?>("timestamp");
				var value = point.Value<long?>("v") ??
					point.Value<long?>("value");
				if (timestamp == null || value == null)
					continue;
				if (!values.TryGetValue(timestamp.Value, out var candle))
					values[timestamp.Value] = candle = new long?[6];
				candle[index] = value.Value;
			}
		}

		return
		[
			..
				values
					.Where(pair =>
						pair.Value[0] != null &&
						pair.Value[1] != null &&
						pair.Value[2] != null &&
						pair.Value[3] != null)
					.OrderBy(pair => pair.Key)
					.Select(pair => new NubraCandle
					{
						Timestamp = pair.Key,
						Open = pair.Value[0].Value,
						High = pair.Value[1].Value,
						Low = pair.Value[2].Value,
						Close = pair.Value[3].Value,
						Volume = pair.Value[4] ?? pair.Value[5] ?? 0,
					})
		];
	}

	internal static NubraOrder[] ParseOrders(JToken response)
	{
		var token = response?["orders"] ?? response?["data"]?["orders"];
		if (token is JArray array)
			return array.ToObject<NubraOrder[]>() ?? [];
		if (token is not JObject buckets)
			return [];

		return
		[
			..
				buckets
					.Properties()
					.Where(property => property.Value is JArray)
					.SelectMany(property => property.Value.Children())
					.Select(item => item.ToObject<NubraOrder>())
					.Where(order => order != null)
		];
	}

	internal static string FindString(JToken token, params string[] names)
	{
		if (token is not JContainer container)
			return null;

		foreach (var property in container
			.Descendants()
			.OfType<JProperty>())
		{
			if (names.Any(name => property.Name.Equals(
					name,
					StringComparison.OrdinalIgnoreCase)) &&
				property.Value.Type is not JTokenType.Object and
					not JTokenType.Array and
					not JTokenType.Null)
				return property.Value.Value<string>();
		}

		return null;
	}

	private async Task<JToken> Send(
		string path,
		HttpMethod method,
		object body,
		bool authenticated,
		string bearerOverride,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, path);
		request.Headers.TryAddWithoutValidation("x-device-id", _deviceId);
		var bearer = bearerOverride.IsEmpty() ? _token : bearerOverride;
		if (authenticated)
			bearer.ThrowIfEmpty("Nubra session token");
		if (!bearer.IsEmpty())
			request.Headers.Authorization = new("Bearer", bearer);
		if (body != null)
		{
			request.Content = new StringContent(
				JsonConvert.SerializeObject(body, Formatting.None, _jsonSettings),
				Encoding.UTF8,
				"application/json");
		}

		this.AddVerboseLog("Nubra {0} {1}.", method, path);
		using var response = await _httpClient.SendAsync(
			request,
			HttpCompletionOption.ResponseContentRead,
			cancellationToken);
		var json = await response.Content.ReadAsStringAsync(cancellationToken);
		JToken parsed = null;
		if (!json.IsEmpty())
		{
			try
			{
				parsed = JToken.Parse(json);
			}
			catch (JsonException error)
			{
				if (response.IsSuccessStatusCode)
				throw new InvalidDataException(
					$"Nubra {path} returned invalid JSON.",
					error);
			}
		}

		if (!response.IsSuccessStatusCode)
		{
			var error = FindString(
					parsed,
					"message",
					"error",
					"detail",
					"reason")
				.IsEmpty(response.ReasonPhrase)
				.IsEmpty("unknown error");
			throw new InvalidOperationException(
				$"Nubra {path} returned HTTP {(int)response.StatusCode}: {error}");
		}

		return parsed ?? new JObject();
	}

	private static byte[] DecodeBase32(string value)
	{
		value = value
			.ThrowIfEmpty(nameof(value))
			.Replace(" ", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.TrimEnd('=')
			.ToUpperInvariant();
		var result = new List<byte>(value.Length * 5 / 8);
		var buffer = 0;
		var bits = 0;
		foreach (var character in value)
		{
			var digit = character switch
			{
				>= 'A' and <= 'Z' => character - 'A',
				>= '2' and <= '7' => character - '2' + 26,
				_ => throw new FormatException(
					$"Invalid Base32 character '{character}' in Nubra TOTP secret."),
			};
			buffer = (buffer << 5) | digit;
			bits += 5;
			if (bits < 8)
				continue;
			bits -= 8;
			result.Add((byte)(buffer >> bits));
			buffer &= (1 << bits) - 1;
		}

		return [.. result];
	}

	private static string SymbolKey(string exchange, string symbol)
		=> $"{exchange?.ToUpperInvariant()}|{symbol?.ToUpperInvariant()}";
}
