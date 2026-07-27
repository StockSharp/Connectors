namespace StockSharp.Ventura.Native;

sealed class VenturaRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly HttpClient _httpClient;
	private readonly string _appKey;
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private VenturaInstrument[] _instruments;
	private IReadOnlyDictionary<string, VenturaInstrument> _instrumentsByKey;
	private IReadOnlyDictionary<string, VenturaInstrument> _instrumentsBySymbol;
	private IReadOnlyDictionary<string, VenturaInstrument> _instrumentsByToken;
	private string _clientId;
	private string _token;

	public VenturaRestClient(
		Uri restAddress,
		SecureString appKey,
		string clientId = null,
		SecureString token = null,
		HttpMessageHandler handler = null)
	{
		_appKey = appKey.ThrowIfEmpty(nameof(appKey)).UnSecure();
		_httpClient = handler == null ? new() : new(handler);
		_httpClient.BaseAddress = restAddress ??
			throw new ArgumentNullException(nameof(restAddress));
		_httpClient.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Ventura-EaseAPI/1.0");
		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
			"X-EaseApi-Version",
			"1");
		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
			"x-app-key",
			_appKey);
		SetSession(clientId, token);
	}

	public override string Name =>
		nameof(Ventura) + "_" + nameof(VenturaRestClient);

	public string ClientId => _clientId;

	public string Token => _token;

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public void SetSession(string clientId, SecureString token)
	{
		_clientId = clientId;
		_token = token.IsEmpty() ? null : token.UnSecure();
	}

	public Uri CreateAuthorizationUri(string state)
	{
		state.ThrowIfEmpty(nameof(state));
		return new(
			_httpClient.BaseAddress,
			$"auth/v1/login?app_key={Uri.EscapeDataString(_appKey)}" +
			$"&state={Uri.EscapeDataString(state)}");
	}

	public async Task<VenturaAuthResult> ExchangeAccessToken(
		SecureString appSecret,
		SecureString requestToken,
		CancellationToken cancellationToken)
	{
		var response = await Send(
			"login/v1/authorization/token",
			HttpMethod.Post,
			new
			{
				request_token = requestToken
					.ThrowIfEmpty(nameof(requestToken))
					.UnSecure(),
				data = ComputeAuthHash(
					_appKey,
					appSecret.ThrowIfEmpty(nameof(appSecret)).UnSecure()),
			},
			false,
			null,
			null,
			cancellationToken);
		return ApplyAuthResult(ParseAuthResult(response));
	}

	public async Task<VenturaAuthResult> LoginWithTotp(
		string clientId,
		SecureString appSecret,
		SecureString pin,
		SecureString totpSecret,
		string macAddress,
		DateTime utcNow,
		CancellationToken cancellationToken)
	{
		clientId.ThrowIfEmpty(nameof(clientId));
		macAddress.ThrowIfEmpty(nameof(macAddress));
		var totp = GenerateTotp(
				totpSecret.ThrowIfEmpty(nameof(totpSecret)).UnSecure(),
				utcNow)
			.ToString("D6", CultureInfo.InvariantCulture);
		var response = await Send(
			"login/v1/authorization/totp",
			HttpMethod.Post,
			new
			{
				password = pin.ThrowIfEmpty(nameof(pin)).UnSecure(),
				data = ComputeAuthHash(
					_appKey,
					appSecret.ThrowIfEmpty(nameof(appSecret)).UnSecure()),
				totp,
			},
			false,
			clientId,
			new Dictionary<string, string>
			{
				["x-mac-address"] = macAddress,
			},
			cancellationToken);
		return ApplyAuthResult(ParseAuthResult(response));
	}

	public Task<JToken> GetProfile(CancellationToken cancellationToken)
		=> Send(
			"user/v1/profile",
			HttpMethod.Get,
			null,
			true,
			null,
			null,
			cancellationToken);

	public async Task<VenturaInstrument[]> GetInstruments(
		CancellationToken cancellationToken)
	{
		if (_instruments != null)
			return _instruments;

		await _instrumentLock.WaitAsync(cancellationToken);
		try
		{
			if (_instruments != null)
				return _instruments;

			using var request = new HttpRequestMessage(
				HttpMethod.Get,
				"instrument/v1/instruments");
			request.Headers.Accept.Clear();
			request.Headers.Accept.Add(
				new MediaTypeWithQualityHeaderValue("text/csv"));
			this.AddVerboseLog(
				"Ventura EaseAPI GET public instrument master.");
			using var response = await _httpClient.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				var error = await response.Content.ReadAsStringAsync(
					cancellationToken);
				throw new HttpRequestException(
					$"Ventura EaseAPI instrument master returned HTTP {(int)response.StatusCode}: {error.IsEmpty(response.ReasonPhrase)}.",
					null,
					response.StatusCode);
			}

			await using var stream =
				await response.Content.ReadAsStreamAsync(cancellationToken);
			_instruments = await ParseInstrumentCsv(
				stream,
				cancellationToken);
			if (_instruments.Length == 0)
			{
				throw new InvalidDataException(
					"Ventura EaseAPI instrument master contained no instruments.");
			}

			_instrumentsByKey = _instruments
				.GroupBy(
					instrument => VenturaExtensions.CreateInstrumentKey(
						instrument.Exchange,
						instrument.ExchangeToken),
					StringComparer.OrdinalIgnoreCase)
				.ToDictionary(
					group => group.Key,
					group => group.First(),
					StringComparer.OrdinalIgnoreCase);
			_instrumentsBySymbol = _instruments
				.Where(instrument =>
					!instrument.Exchange.IsEmpty() &&
					!instrument.TradingSymbol.IsEmpty())
				.GroupBy(
					instrument => SymbolKey(
						instrument.Exchange,
						instrument.TradingSymbol),
					StringComparer.OrdinalIgnoreCase)
				.ToDictionary(
					group => group.Key,
					group => group.First(),
					StringComparer.OrdinalIgnoreCase);
			_instrumentsByToken = _instruments
				.Where(instrument => !instrument.ExchangeToken.IsEmpty())
				.GroupBy(
					instrument => instrument.ExchangeToken,
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

	public async Task<VenturaInstrument> GetInstrument(
		string exchange,
		string exchangeToken,
		CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		if (_instrumentsByKey.TryGetValue(
			VenturaExtensions.CreateInstrumentKey(exchange, exchangeToken),
			out var instrument))
			return instrument;
		return !exchangeToken.IsEmpty() &&
			_instrumentsByToken.TryGetValue(exchangeToken, out instrument)
				? instrument
				: null;
	}

	public async Task<VenturaInstrument> FindInstrument(
		string exchange,
		string symbol,
		CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		if (_instrumentsBySymbol.TryGetValue(
			SymbolKey(exchange, symbol),
			out var instrument))
			return instrument;
		return _instruments.FirstOrDefault(item =>
			item.TradingSymbol.EqualsIgnoreCase(symbol) ||
			item.Name.EqualsIgnoreCase(symbol));
	}

	public async Task<VenturaMarketUpdate> GetMarketUpdate(
		VenturaInstrument instrument,
		bool depth,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		var response = await Send(
			depth
				? "instrument/v1/ltp_depth"
				: "instrument/v1/ohlcv",
			HttpMethod.Post,
			new
			{
				exchange = instrument.Exchange,
				tokens = new[] { instrument.ToStreamToken() },
			},
			true,
			null,
			null,
			cancellationToken);
		var data = UnwrapData(response);
		var row = data is JArray rows && rows.FirstOrDefault() is JArray first
			? first
			: data as JArray;
		if (row == null)
		{
			throw new InvalidDataException(
				"Ventura EaseAPI market quote contained no data row.");
		}
		return ParseQuoteRow(
			row,
			instrument.ToStreamAction(depth),
			CurrentUtc());
	}

	public async Task<string> PlaceOrder(
		VenturaProducts product,
		JObject order,
		CancellationToken cancellationToken)
		=> ParseOrderId(await Send(
			product == VenturaProducts.Intraday
				? "trade/v1/intraday/regular"
				: "trade/v1/delivery",
			HttpMethod.Post,
			order ?? throw new ArgumentNullException(nameof(order)),
			true,
			null,
			null,
			cancellationToken));

	public async Task<string> ModifyOrder(
		JObject order,
		CancellationToken cancellationToken)
		=> ParseOrderId(await Send(
			"trade/v1/modify",
			HttpMethod.Post,
			order ?? throw new ArgumentNullException(nameof(order)),
			true,
			null,
			null,
			cancellationToken));

	public Task CancelOrder(
		string orderId,
		CancellationToken cancellationToken)
		=> Send(
			"trade/v1/cancel",
			HttpMethod.Post,
			new
			{
				order_no = orderId.ThrowIfEmpty(nameof(orderId)),
			},
			true,
			null,
			null,
			cancellationToken);

	public async Task<VenturaOrder[]> GetOrders(
		CancellationToken cancellationToken)
		=> UnwrapResult(await Send(
				"trade/v1/orders",
				HttpMethod.Get,
				null,
				true,
				null,
				null,
				cancellationToken))
			.ToObject<VenturaOrder[]>() ?? [];

	public async Task<VenturaTrade[]> GetTrades(
		CancellationToken cancellationToken)
		=> UnwrapResult(await Send(
				"trade/v1/trades",
				HttpMethod.Get,
				null,
				true,
				null,
				null,
				cancellationToken))
			.ToObject<VenturaTrade[]>() ?? [];

	public async Task<VenturaPosition[]> GetPositions(
		CancellationToken cancellationToken)
	{
		var result = UnwrapResult(await Send(
			"portfolio/v1/positions",
			HttpMethod.Get,
			null,
			true,
			null,
			null,
			cancellationToken));
		if (result is JArray array)
			return array.ToObject<VenturaPosition[]>() ?? [];
		return
		[
			..
				new[] { result["open_positions"], result["closed_positions"] }
					.OfType<JArray>()
					.SelectMany(items => items)
					.Select(item => item.ToObject<VenturaPosition>())
					.Where(position => position != null)
		];
	}

	public async Task<VenturaHolding[]> GetHoldings(
		CancellationToken cancellationToken)
		=> UnwrapResult(await Send(
				"portfolio/v1/holdings",
				HttpMethod.Get,
				null,
				true,
				null,
				null,
				cancellationToken))
			.ToObject<VenturaHolding[]>() ?? [];

	public async Task<VenturaFunds> GetFunds(
		CancellationToken cancellationToken)
		=> UnwrapData(await Send(
				"user/v1/fund_details",
				HttpMethod.Get,
				null,
				true,
				null,
				null,
				cancellationToken))
			.ToObject<VenturaFunds>() ?? new();

	internal static string ComputeAuthHash(
		string appKey,
		string appSecret)
	{
		appKey.ThrowIfEmpty(nameof(appKey));
		appSecret.ThrowIfEmpty(nameof(appSecret));
		return Convert.ToHexString(
				SHA256.HashData(
					Encoding.UTF8.GetBytes(appKey + appSecret)))
			.ToLowerInvariant();
	}

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

	internal static async Task<VenturaInstrument[]> ParseInstrumentCsv(
		Stream stream,
		CancellationToken cancellationToken)
	{
		if (stream == null)
			throw new ArgumentNullException(nameof(stream));

		await using var source = new MemoryStream();
		await stream.CopyToAsync(source, cancellationToken);
		source.Position = 0;

		Stream decoded = source;
		MemoryStream expanded = null;
		ZipArchive archive = null;
		if (source.Length >= 2)
		{
			var first = source.ReadByte();
			var second = source.ReadByte();
			source.Position = 0;
			if (first == 0x1f && second == 0x8b)
			{
				expanded = new();
				await using (var gzip = new GZipStream(
					source,
					CompressionMode.Decompress,
					true))
				{
					await gzip.CopyToAsync(expanded, cancellationToken);
				}
				expanded.Position = 0;
				decoded = expanded;
			}
			else if (first == 0x50 && second == 0x4b)
			{
				archive = new(source, ZipArchiveMode.Read, true);
				var entry = archive.Entries.FirstOrDefault(item =>
					item.Length > 0 &&
					item.Name.EndsWith(
						".csv",
						StringComparison.OrdinalIgnoreCase)) ??
					archive.Entries.FirstOrDefault(item => item.Length > 0) ??
					throw new InvalidDataException(
						"Ventura EaseAPI instrument ZIP contained no files.");
				expanded = new();
				await using (var entryStream = entry.Open())
					await entryStream.CopyToAsync(expanded, cancellationToken);
				expanded.Position = 0;
				decoded = expanded;
			}
		}

		try
		{
			using var reader = new StreamReader(
				decoded,
				Encoding.UTF8,
				true,
				1 << 16,
				true);
			var header = await reader.ReadLineAsync(cancellationToken);
			const string expected =
				"exchange_token,trading_symbol,name,last_price,expiry," +
				"strike,tick_size,lot_size,instrument,segment,exchange";
			if (header.IsEmpty() ||
				!header.TrimStart('\uFEFF').EqualsIgnoreCase(expected))
			{
				throw new InvalidDataException(
					"Ventura EaseAPI instrument-master header is invalid.");
			}

			var csv = new FastCsvReader(reader, StringHelper.N)
			{
				ColumnSeparator = ',',
			};
			var result = new List<VenturaInstrument>();
			while (await csv.NextLineAsync(cancellationToken))
			{
				var values = new string[11];
				for (var index = 0; index < values.Length; index++)
					values[index] = csv.ReadString()?.Trim();
				if (values[0].IsEmpty() ||
					values[1].IsEmpty() ||
					values[10].IsEmpty())
					continue;
				result.Add(new()
				{
					ExchangeToken = values[0],
					TradingSymbol = values[1],
					Name = values[2],
					LastPrice = ParseDecimal(values[3]),
					Expiry = values[4],
					Strike = ParseDecimal(values[5]),
					TickSize = ParseDecimal(values[6]),
					LotSize = ParseDecimal(values[7], 1m),
					Instrument = values[8]?.ToUpperInvariant(),
					Segment = values[9],
					Exchange = values[10].ToUpperInvariant(),
				});
			}
			return [.. result];
		}
		finally
		{
			archive?.Dispose();
			expanded?.Dispose();
		}
	}

	internal static VenturaMarketUpdate ParseQuoteRow(
		JArray row,
		string action,
		DateTime fallback)
	{
		if (row == null || row.Count < 8)
		{
			throw new InvalidDataException(
				"Ventura EaseAPI quote row is incomplete.");
		}
		var depth = row.Count > 12 && row[12] is JArray depthRows
			? ParseDepth(depthRows)
			: [];
		return new()
		{
			Action = action,
			Token = row[0]?.Value<string>(),
			LastPrice = DecimalAt(row, 1),
			OpenPrice = DecimalAt(row, 2),
			HighPrice = DecimalAt(row, 3),
			LowPrice = DecimalAt(row, 4),
			PreviousClose = DecimalAt(row, 5),
			Volume = DecimalAt(row, 6),
			ServerTime = ParseTime(row[7], fallback),
			UpperCircuit = DecimalAt(row, 8),
			LowerCircuit = DecimalAt(row, 9),
			TotalBuyQuantity = DecimalAt(row, 10),
			TotalSellQuantity = DecimalAt(row, 11),
			Depth = depth,
		};
	}

	internal static VenturaDepthLevel[] ParseDepth(JArray rows)
		=>
		[
			..
				(rows ?? [])
					.OfType<JArray>()
					.Where(row => row.Count >= 6)
					.Select(row => new VenturaDepthLevel
					{
						BuyQuantity = DecimalAt(row, 0),
						SellQuantity = DecimalAt(row, 1),
						BuyOrders = LongAt(row, 2),
						SellOrders = LongAt(row, 3),
						BuyPrice = DecimalAt(row, 4),
						SellPrice = DecimalAt(row, 5),
					})
		];

	internal static JToken ParseResponse(
		string path,
		string json,
		HttpStatusCode statusCode)
	{
		JToken response = null;
		if (!json.IsEmpty())
		{
			try
			{
				response = JToken.Parse(json);
			}
			catch (JsonException error)
			{
				if ((int)statusCode is >= 200 and < 300)
				{
					throw new InvalidDataException(
						$"Ventura EaseAPI {path} returned invalid JSON.",
						error);
				}
			}
		}

		var success = (int)statusCode is >= 200 and < 300;
		if (response?["success"]?.Type == JTokenType.Boolean &&
			response["success"].Value<bool>() == false)
			success = false;
		if (response?["error"]?.Type == JTokenType.Boolean &&
			response["error"].Value<bool>())
			success = false;
		if (response?["session_expired"]?.Value<bool?>() == true)
			success = false;
		var status = response?["status"]?.Value<string>();
		if (!status.IsEmpty() &&
			!status.EqualsIgnoreCase("success") &&
			!status.EqualsIgnoreCase("ok"))
			success = false;
		var errorMessage = response?["error_message"]?.Value<string>();
		if (!errorMessage.IsEmpty())
			success = false;

		if (!success)
		{
			var message = FindString(
					response,
					"message",
					"error_message",
					"error",
					"detail",
					"reason")
				.IsEmpty(status)
				.IsEmpty(statusCode.ToString());
			throw new InvalidOperationException(
				$"Ventura EaseAPI {path} returned HTTP {(int)statusCode}: {message}");
		}
		return response ?? new JObject();
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
		string clientIdOverride,
		IReadOnlyDictionary<string, string> headers,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, path);
		var clientId = clientIdOverride.IsEmpty()
			? _clientId
			: clientIdOverride;
		if (authenticated)
		{
			clientId.ThrowIfEmpty("Ventura EaseAPI client ID");
			_token.ThrowIfEmpty("Ventura EaseAPI auth token");
			request.Headers.Authorization =
				new AuthenticationHeaderValue("Bearer", _token);
		}
		if (!clientId.IsEmpty())
		{
			request.Headers.TryAddWithoutValidation(
				"x-client-id",
				clientId);
		}
		if (headers != null)
		{
			foreach (var pair in headers)
			{
				request.Headers.TryAddWithoutValidation(
					pair.Key,
					pair.Value);
			}
		}
		if (body != null)
		{
			request.Content = new StringContent(
				JsonConvert.SerializeObject(
					body,
					Formatting.None,
					_jsonSettings),
				Encoding.UTF8,
				"application/json");
		}

		this.AddVerboseLog("Ventura EaseAPI {0} {1}.", method, path);
		using var response = await _httpClient.SendAsync(
			request,
			HttpCompletionOption.ResponseContentRead,
			cancellationToken);
		var json = await response.Content.ReadAsStringAsync(cancellationToken);
		return ParseResponse(path, json, response.StatusCode);
	}

	private VenturaAuthResult ApplyAuthResult(VenturaAuthResult result)
	{
		if (result == null ||
			result.ClientId.IsEmpty() ||
			result.AuthToken.IsEmpty())
		{
			throw new InvalidDataException(
				"Ventura EaseAPI authorization response contained no client ID or auth token.");
		}
		_clientId = result.ClientId;
		_token = result.AuthToken;
		return result;
	}

	private static VenturaAuthResult ParseAuthResult(JToken response)
		=> UnwrapData(response).ToObject<VenturaAuthResult>();

	private static JToken UnwrapData(JToken response)
		=> response?["data"] ?? response ?? new JObject();

	private static JToken UnwrapResult(JToken response)
		=> response?["result"] ?? response?["data"] ?? response ?? new JArray();

	private static string ParseOrderId(JToken response)
		=> FindString(
				response,
				"order_no",
				"order_id",
				"orderId")
			.ThrowIfEmpty("Ventura EaseAPI order number");

	private static decimal ParseDecimal(
		string value,
		decimal fallback = 0m)
		=> decimal.TryParse(
			value,
			NumberStyles.Any,
			CultureInfo.InvariantCulture,
			out var number)
				? number
				: fallback;

	internal static decimal DecimalAt(JArray row, int index)
		=> row != null && index >= 0 && index < row.Count
			? row[index]?.Value<decimal?>() ?? 0m
			: 0m;

	private static long LongAt(JArray row, int index)
		=> row != null && index >= 0 && index < row.Count
			? row[index]?.Value<long?>() ?? 0L
			: 0L;

	internal static DateTime ParseTime(
		JToken token,
		DateTime fallback)
	{
		if (token == null || token.Type == JTokenType.Null)
			return fallback;
		if (token.Type == JTokenType.Integer)
		{
			var value = token.Value<long>();
			try
			{
				return value >= 100_000_000_000L
					? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime
					: DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
			}
			catch (ArgumentOutOfRangeException)
			{
				return fallback;
			}
		}
		return token.Value<string>().ToVenturaTime(fallback);
	}

	private static DateTime CurrentUtc() => DateTime.UtcNow;

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
					$"Invalid Base32 character '{character}' in Ventura TOTP secret."),
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
