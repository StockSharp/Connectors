namespace StockSharp.HdfcSecurities.Native;

sealed class HdfcRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly HttpClient _httpClient;
	private readonly Uri _instrumentAddress;
	private readonly string _apiKey;
	private readonly SemaphoreSlim _instrumentLock = new(1, 1);
	private HdfcInstrument[] _instruments;
	private IReadOnlyDictionary<string, HdfcInstrument> _instrumentsByKey;
	private IReadOnlyDictionary<string, HdfcInstrument> _instrumentsByStream;
	private string _token;

	public HdfcRestClient(
		Uri restAddress,
		Uri instrumentAddress,
		SecureString apiKey,
		SecureString token = null,
		HttpMessageHandler handler = null)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
		_instrumentAddress = instrumentAddress ??
			throw new ArgumentNullException(nameof(instrumentAddress));
		_httpClient = handler == null ? new() : new(handler);
		_httpClient.BaseAddress = restAddress ??
			throw new ArgumentNullException(nameof(restAddress));
		_httpClient.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-HDFC-Securities/1.0");
		SetToken(token);
	}

	public override string Name => nameof(HdfcSecurities) + "_" +
		nameof(HdfcRestClient);

	public string Token => _token;

	protected override void DisposeManaged()
	{
		_httpClient.Dispose();
		_instrumentLock.Dispose();
		base.DisposeManaged();
	}

	public void SetToken(SecureString token)
		=> _token = token.IsEmpty() ? null : token.UnSecure();

	public Uri CreateAuthorizationUri()
		=> new(
			_httpClient.BaseAddress,
			$"v1/login?api_key={Uri.EscapeDataString(_apiKey)}");

	public async Task<string> ExchangeAccessToken(
		SecureString apiSecret,
		SecureString requestToken,
		CancellationToken cancellationToken)
	{
		var response = await Send(
			$"v1/access-token?api_key={Uri.EscapeDataString(_apiKey)}" +
			$"&request_token={Uri.EscapeDataString(requestToken
				.ThrowIfEmpty(nameof(requestToken)).UnSecure())}",
			HttpMethod.Post,
			new
			{
				apiSecret = apiSecret
					.ThrowIfEmpty(nameof(apiSecret))
					.UnSecure(),
			},
			false,
			false,
			cancellationToken);
		var token = FindString(response, "accessToken", "access_token")
			.ThrowIfEmpty("HDFC Securities access token");
		_token = token;
		return token;
	}

	public async Task<HdfcProfile> GetProfile(
		CancellationToken cancellationToken)
	{
		var data = await Send(
			"v3/user/profile",
			HttpMethod.Post,
			null,
			true,
			true,
			cancellationToken);
		return data is JArray array
			? array.FirstOrDefault()?.ToObject<HdfcProfile>()
			: data.ToObject<HdfcProfile>();
	}

	public async Task<HdfcInstrument[]> GetInstruments(
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
				_instrumentAddress);
			request.Headers.Accept.Clear();
			request.Headers.Accept.Add(
				new MediaTypeWithQualityHeaderValue("text/csv"));
			this.AddVerboseLog(
				"HDFC Securities GET public security master.");
			using var response = await _httpClient.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				throw new HttpRequestException(
					$"HDFC Securities security master returned HTTP {(int)response.StatusCode}.",
					null,
					response.StatusCode);
			}
			await using var stream = await response.Content.ReadAsStreamAsync(
				cancellationToken);
			_instruments = await ParseInstrumentCsv(
				stream,
				cancellationToken);
			if (_instruments.Length == 0)
			{
				throw new InvalidDataException(
					"HDFC Securities security master contained no instruments.");
			}

			_instrumentsByKey = _instruments
				.GroupBy(
					instrument => HdfcExtensions.CreateInstrumentKey(
						instrument.Exchange,
						instrument.SecurityId),
					StringComparer.OrdinalIgnoreCase)
				.ToDictionary(
					group => group.Key,
					group => group.First(),
					StringComparer.OrdinalIgnoreCase);
			_instrumentsByStream = _instruments
				.Select(instrument =>
				{
					try
					{
						return (stream: instrument.ToStreamId(), instrument);
					}
					catch (ArgumentException)
					{
						return default;
					}
				})
				.Where(pair => !pair.stream.IsEmpty())
				.GroupBy(
					pair => pair.stream,
					StringComparer.OrdinalIgnoreCase)
				.ToDictionary(
					group => group.Key,
					group => group.First().instrument,
					StringComparer.OrdinalIgnoreCase);
			return _instruments;
		}
		finally
		{
			_instrumentLock.Release();
		}
	}

	public async Task<HdfcInstrument> GetInstrument(
		string exchange,
		string securityId,
		CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		_instrumentsByKey.TryGetValue(
			HdfcExtensions.CreateInstrumentKey(exchange, securityId),
			out var instrument);
		return instrument;
	}

	public async Task<HdfcInstrument> GetInstrumentByStream(
		string streamId,
		CancellationToken cancellationToken)
	{
		await GetInstruments(cancellationToken);
		_instrumentsByStream.TryGetValue(streamId, out var instrument);
		return instrument;
	}

	public async Task<HdfcLtp[]> GetLtp(
		IEnumerable<HdfcInstrument> instruments,
		CancellationToken cancellationToken)
	{
		var items = instruments?.DistinctBy(
			instrument => instrument.ToStreamId()).ToArray() ??
			throw new ArgumentNullException(nameof(instruments));
		if (items.Length == 0)
			return [];
		var data = await Send(
			"v1/fetch-ltp",
			HttpMethod.Put,
			new
			{
				data = items.Select(instrument => new
				{
					exchange = instrument.Exchange,
					token = instrument.ExchangeSecurityId,
				}),
			},
			true,
			true,
			cancellationToken);
		return data.ToObject<HdfcLtp[]>() ?? [];
	}

	public async Task<string> PlaceOrder(
		JObject order,
		CancellationToken cancellationToken)
		=> ParseOrderId(await Send(
			"v1/orders/regular",
			HttpMethod.Post,
			order ?? throw new ArgumentNullException(nameof(order)),
			true,
			true,
			cancellationToken));

	public async Task<string> ModifyOrder(
		string orderId,
		JObject order,
		CancellationToken cancellationToken)
		=> ParseOrderId(await Send(
			$"v1/orders/regular/{Uri.EscapeDataString(
				orderId.ThrowIfEmpty(nameof(orderId)))}",
			HttpMethod.Put,
			order ?? throw new ArgumentNullException(nameof(order)),
			true,
			true,
			cancellationToken));

	public Task CancelOrder(
		string orderId,
		CancellationToken cancellationToken)
		=> Send(
			$"v1/orders/regular/{Uri.EscapeDataString(
				orderId.ThrowIfEmpty(nameof(orderId)))}",
			HttpMethod.Delete,
			null,
			true,
			true,
			cancellationToken);

	public async Task<HdfcOrder[]> GetOrders(
		CancellationToken cancellationToken)
		=> (await Send(
				"v1/orders",
				HttpMethod.Get,
				null,
				true,
				true,
				cancellationToken))
			.ToObject<HdfcOrder[]>() ?? [];

	public async Task<HdfcTrade[]> GetTrades(
		CancellationToken cancellationToken)
		=> (await Send(
				"v1/trades",
				HttpMethod.Get,
				null,
				true,
				true,
				cancellationToken))
			.ToObject<HdfcTrade[]>() ?? [];

	public async Task<HdfcPosition[]> GetPositions(
		CancellationToken cancellationToken)
	{
		var data = await Send(
			"v1/portfolio/cumulative-positions",
			HttpMethod.Get,
			null,
			true,
			true,
			cancellationToken);
		return (data["net"] ?? data).ToObject<HdfcPosition[]>() ?? [];
	}

	public async Task<HdfcHolding[]> GetHoldings(
		CancellationToken cancellationToken)
		=> (await Send(
				"v1/portfolio/holdings",
				HttpMethod.Get,
				null,
				true,
				true,
				cancellationToken))
			.ToObject<HdfcHolding[]>() ?? [];

	public async Task<HdfcMargins> GetMargins(
		CancellationToken cancellationToken)
	{
		var data = await Send(
			"v1/user/margins",
			HttpMethod.Get,
			null,
			true,
			true,
			cancellationToken);
		return (data["equity"] ?? data).ToObject<HdfcMargins>() ?? new();
	}

	internal static async Task<HdfcInstrument[]> ParseInstrumentCsv(
		Stream stream,
		CancellationToken cancellationToken)
	{
		if (stream == null)
			throw new ArgumentNullException(nameof(stream));
		using var reader = new StreamReader(
			stream,
			Encoding.UTF8,
			true,
			1 << 16,
			true);
		var header = await reader.ReadLineAsync(cancellationToken);
		var expected =
			"exchange,security_id,instrument_segment,expiry_date," +
			"strike_price,option_type,lot_size,tick_size,close_price," +
			"exch_security_id,symbol_name,underline_symbol,open_price";
		if (header.IsEmpty() ||
			!header.TrimStart('\uFEFF').EqualsIgnoreCase(expected))
		{
			throw new InvalidDataException(
				"HDFC Securities security-master header is invalid.");
		}

		var csv = new FastCsvReader(reader, StringHelper.N)
		{
			ColumnSeparator = ',',
		};
		var result = new List<HdfcInstrument>();
		while (await csv.NextLineAsync(cancellationToken))
		{
			var values = new string[13];
			for (var index = 0; index < values.Length; index++)
				values[index] = csv.ReadString()?.Trim();
			if (values[0].IsEmpty() ||
				values[1].IsEmpty() ||
				values[2].IsEmpty() ||
				values[9].IsEmpty())
				continue;

			result.Add(new()
			{
				Exchange = values[0].ToUpperInvariant(),
				SecurityId = values[1],
				InstrumentSegment = values[2].ToUpperInvariant(),
				ExpiryDate = values[3],
				StrikePrice = values[4].To<decimal?>(),
				OptionType = values[5],
				LotSize = values[6].To<decimal?>() ?? 1m,
				TickSize = values[7].To<decimal?>() ?? 0m,
				ClosePrice = values[8].To<decimal?>() ?? 0m,
				ExchangeSecurityId = values[9],
				SymbolName = values[10],
				UnderlyingSymbol = values[11],
				OpenPrice = values[12].To<decimal?>() ?? 0m,
			});
		}
		return [.. result];
	}

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
				throw new InvalidDataException(
					$"HDFC Securities {path} returned invalid JSON.",
					error);
			}
		}

		var success = (int)statusCode is >= 200 and < 300;
		var status = response?["status"]?.Value<string>();
		var metaStatus = response?["meta"]?["statusCode"]?.Value<string>();
		if (!status.IsEmpty() && !status.EqualsIgnoreCase("success"))
			success = false;
		if (!metaStatus.IsEmpty() &&
			!metaStatus.EqualsIgnoreCase("OK") &&
			!metaStatus.EqualsIgnoreCase("SUCCESS"))
			success = false;
		if (!success)
		{
			var message = FindString(
					response,
					"displayMessage",
					"statusMsg",
					"message",
					"error",
					"detail",
					"reason")
				.IsEmpty(status)
				.IsEmpty(statusCode.ToString());
			throw new InvalidOperationException(
				$"HDFC Securities {path} returned HTTP {(int)statusCode}: {message}");
		}

		return response?["data"] ?? response ?? new JObject();
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
		bool appendApiKey,
		CancellationToken cancellationToken)
	{
		if (appendApiKey)
		{
			path += path.Contains('?', StringComparison.Ordinal)
				? "&"
				: "?";
			path += $"api_key={Uri.EscapeDataString(_apiKey)}";
		}
		using var request = new HttpRequestMessage(method, path);
		if (authenticated)
		{
			request.Headers.TryAddWithoutValidation(
				"Authorization",
				_token.ThrowIfEmpty("HDFC Securities access token"));
		}
		if (body != null)
		{
			request.Content = new StringContent(
				JsonConvert.SerializeObject(body, Formatting.None, _jsonSettings),
				Encoding.UTF8,
				"application/json");
		}

		this.AddVerboseLog("HDFC Securities {0} {1}.", method, path);
		using var response = await _httpClient.SendAsync(
			request,
			HttpCompletionOption.ResponseContentRead,
			cancellationToken);
		var json = await response.Content.ReadAsStringAsync(cancellationToken);
		return ParseResponse(path, json, response.StatusCode);
	}

	private static string ParseOrderId(JToken data)
		=> FindString(data, "order_id", "orderId")
			.ThrowIfEmpty("HDFC Securities order id");
}
