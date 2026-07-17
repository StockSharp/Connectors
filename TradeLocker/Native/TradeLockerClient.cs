namespace StockSharp.TradeLocker.Native;

internal sealed class TradeLockerApiException : InvalidOperationException
{
	public TradeLockerApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

internal sealed class TradeLockerClient : BaseLogReceiver, IDisposable
{
	private readonly HttpClient _http;
	private readonly string _email;
	private readonly string _password;
	private readonly string _server;
	private readonly string _developerApiKey;
	private readonly int _maxAttempts;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private string _accessToken;
	private string _refreshToken;
	private DateTimeOffset _tokenExpires;
	private int _accountNumber;
	private long _accountId;
	private TradeLockerConfig _config;

	public TradeLockerClient(bool isDemo, string email, string password, string server,
		string developerApiKey, int maxAttempts)
	{
		_email = email.ThrowIfEmpty(nameof(email));
		_password = password.ThrowIfEmpty(nameof(password));
		_server = server.ThrowIfEmpty(nameof(server));
		_developerApiKey = developerApiKey;
		_maxAttempts = Math.Max(1, maxAttempts);
		_http = new()
		{
			BaseAddress = new(isDemo
				? "https://demo.tradelocker.com/backend-api/"
				: "https://live.tradelocker.com/backend-api/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
	}

	public TradeLockerAccount Account { get; private set; }

	public async Task<TradeLockerAccount> Login(string requestedAccountId,
		CancellationToken cancellationToken)
	{
		var token = await Send<TradeLockerTokenResponse>(HttpMethod.Post, "auth/jwt/token",
			new TradeLockerLoginRequest { Email = _email, Password = _password, Server = _server },
			false, false, cancellationToken);
		SetTokens(token);

		var response = await Send<TradeLockerAccountsResponse>(HttpMethod.Get,
			"auth/jwt/all-accounts", null, true, false, cancellationToken);
		var accounts = response?.Accounts ?? [];
		TradeLockerAccount selected;
		if (requestedAccountId.IsEmpty())
		{
			if (accounts.Length != 1)
				throw new InvalidOperationException(accounts.Length == 0
					? "TradeLocker returned no accounts."
					: "TradeLocker returned multiple accounts. Configure AccountId explicitly.");
			selected = accounts[0];
		}
		else
		{
			selected = accounts.FirstOrDefault(a =>
				a.Id.ToString(CultureInfo.InvariantCulture).EqualsIgnoreCase(requestedAccountId) ||
				a.Name.EqualsIgnoreCase(requestedAccountId))
				?? throw new InvalidOperationException(
					$"TradeLocker account '{requestedAccountId}' was not found.");
		}

		Account = selected;
		_accountId = selected.Id;
		_accountNumber = selected.AccountNumber;
		_config = (await Send<TradeLockerConfigResponse>(HttpMethod.Get, "trade/config", null,
			true, true, cancellationToken))?.Data
			?? throw new InvalidOperationException("TradeLocker returned no protocol configuration.");
		return selected;
	}

	public async Task<TradeLockerInstrument[]> GetInstruments(CancellationToken cancellationToken)
		=> (await Send<TradeLockerInstrumentsResponse>(HttpMethod.Get,
			$"trade/accounts/{_accountId}/instruments", null, true, true, cancellationToken))
			?.Data?.Instruments ?? [];

	public async Task<TradeLockerInstrumentDetails> GetInstrument(long id, long routeId,
		CancellationToken cancellationToken)
		=> (await Send<TradeLockerInstrumentResponse>(HttpMethod.Get,
			$"trade/instruments/{id}?routeId={routeId.ToString(CultureInfo.InvariantCulture)}&locale=en",
			null, true, true, cancellationToken))?.Data;

	public async Task<TradeLockerQuote> GetQuote(long id, long routeId,
		CancellationToken cancellationToken)
		=> (await Send<TradeLockerQuotesResponse>(HttpMethod.Get,
			$"trade/quotes?tradableInstrumentId={id.ToString(CultureInfo.InvariantCulture)}" +
			$"&routeId={routeId.ToString(CultureInfo.InvariantCulture)}", null, true, true,
			cancellationToken))?.Data;

	public async Task<TradeLockerBar[]> GetHistory(long id, long routeId, string resolution,
		DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
	{
		var path = "trade/history?tradableInstrumentId=" + id.ToString(CultureInfo.InvariantCulture) +
			"&routeId=" + routeId.ToString(CultureInfo.InvariantCulture) +
			"&resolution=" + Uri.EscapeDataString(resolution) +
			"&from=" + from.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) +
			"&to=" + to.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
		var response = await Send<TradeLockerHistoryResponse>(HttpMethod.Get, path, null, true, true,
			cancellationToken);
		return response?.Data?.Bars ?? [];
	}

	public Task<TradeLockerOrder[]> GetOrders(bool history, CancellationToken cancellationToken)
		=> GetTable("orders" + (history ? "History" : string.Empty),
			history ? _config.OrdersHistory?.Columns : _config.Orders?.Columns,
			ReadOrder, cancellationToken);

	public Task<TradeLockerPosition[]> GetPositions(CancellationToken cancellationToken)
		=> GetTable("positions", _config.Positions?.Columns, ReadPosition, cancellationToken);

	public Task<TradeLockerAccountState[]> GetAccountState(CancellationToken cancellationToken)
		=> GetTable("state", _config.AccountDetails?.Columns, ReadAccountState, cancellationToken,
			"accountDetailsData");

	public async Task<long> PlaceOrder(TradeLockerOrderRequest request,
		CancellationToken cancellationToken)
		=> (await Send<TradeLockerOrderResponse>(HttpMethod.Post,
			$"trade/accounts/{_accountId}/orders", request, true, true, cancellationToken))
			?.Data?.OrderId ?? 0;

	public async Task CancelOrder(long orderId, CancellationToken cancellationToken)
	{
		var response = await Send<TradeLockerStatusResponse>(HttpMethod.Delete,
			$"trade/orders/{orderId.ToString(CultureInfo.InvariantCulture)}", null, true, true,
			cancellationToken);
		EnsureOk(response?.Status, "cancel order");
	}

	public async Task ClosePosition(long positionId, decimal quantity,
		CancellationToken cancellationToken)
	{
		var response = await Send<TradeLockerStatusResponse>(HttpMethod.Delete,
			$"trade/positions/{positionId.ToString(CultureInfo.InvariantCulture)}",
			new TradeLockerClosePositionRequest { Quantity = quantity }, true, true, cancellationToken);
		EnsureOk(response?.Status, "close position");
	}

	public Task Ping(CancellationToken cancellationToken)
		=> GetQuoteForPing(cancellationToken);

	private async Task GetQuoteForPing(CancellationToken cancellationToken)
	{
		await EnsureToken(cancellationToken);
		await Send<TradeLockerConfigResponse>(HttpMethod.Get, "trade/config", null, true, true,
			cancellationToken);
	}

	private async Task<T[]> GetTable<T>(string endpoint, TradeLockerColumn[] columns,
		Func<JsonReader, string[], T> rowReader, CancellationToken cancellationToken,
		string payloadName = null)
	{
		var path = endpoint == "state"
			? $"trade/accounts/{_accountId}/state"
			: $"trade/accounts/{_accountId}/{endpoint}";
		var payload = await SendText(HttpMethod.Get, path, null, true, true, cancellationToken);
		var names = columns?.Select(c => c.Id).ToArray() ?? [];
		var rows = new List<T>();
		using var textReader = new StringReader(payload);
		using var reader = new JsonTextReader(textReader) { DateParseHandling = DateParseHandling.None };
		var target = payloadName.IsEmpty(endpoint);
		while (reader.Read())
		{
			if (reader.TokenType != JsonToken.PropertyName || !reader.Value.To<string>().EqualsIgnoreCase(target))
				continue;
			if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
				throw new JsonSerializationException($"TradeLocker '{target}' is not an array.");

			if (endpoint == "state")
			{
				rows.Add(rowReader(reader, names));
				break;
			}

			while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			{
				if (reader.TokenType != JsonToken.StartArray)
					throw new JsonSerializationException($"TradeLocker '{target}' row is not an array.");
				rows.Add(rowReader(reader, names));
			}
			break;
		}
		return [.. rows];
	}

	private static TradeLockerOrder ReadOrder(JsonReader reader, string[] columns)
	{
		var row = new TradeLockerOrder();
		ReadRow(reader, columns, (name, value) =>
		{
			switch (name)
			{
				case "id": row.Id = AsLong(value); break;
				case "tradableInstrumentId": row.TradableInstrumentId = AsLong(value); break;
				case "routeId": row.RouteId = AsLong(value); break;
				case "qty": row.Quantity = AsDecimal(value); break;
				case "side": row.Side = value?.ToString(); break;
				case "type": row.Type = value?.ToString(); break;
				case "status": row.Status = value?.ToString(); break;
				case "filledQty": row.FilledQuantity = AsDecimal(value); break;
				case "avgPrice": row.AveragePrice = AsDecimal(value); break;
				case "price": row.Price = AsDecimal(value); break;
				case "stopPrice": row.StopPrice = AsDecimal(value); break;
				case "validity": row.Validity = value?.ToString(); break;
				case "expireDate": row.ExpireDate = AsLong(value); break;
				case "createdDate": row.CreatedDate = AsLong(value); break;
				case "lastModified": row.LastModified = AsLong(value); break;
				case "isOpen": row.IsOpen = AsBool(value); break;
				case "positionId": row.PositionId = AsLong(value); break;
			}
		});
		return row;
	}

	private static TradeLockerPosition ReadPosition(JsonReader reader, string[] columns)
	{
		var row = new TradeLockerPosition();
		ReadRow(reader, columns, (name, value) =>
		{
			switch (name)
			{
				case "id": row.Id = AsLong(value); break;
				case "tradableInstrumentId": row.TradableInstrumentId = AsLong(value); break;
				case "routeId": row.RouteId = AsLong(value); break;
				case "side": row.Side = value?.ToString(); break;
				case "qty": row.Quantity = AsDecimal(value); break;
				case "avgPrice": row.AveragePrice = AsDecimal(value); break;
				case "openDate": row.OpenDate = AsLong(value); break;
				case "unrealizedPl": row.UnrealizedPnL = AsDecimal(value); break;
			}
		});
		return row;
	}

	private static TradeLockerAccountState ReadAccountState(JsonReader reader, string[] columns)
	{
		var row = new TradeLockerAccountState();
		ReadRow(reader, columns, (name, value) =>
		{
			switch (name)
			{
				case "balance": row.Balance = AsDecimal(value); break;
				case "availableFunds": row.AvailableFunds = AsDecimal(value); break;
				case "cashBalance": row.CashBalance = AsDecimal(value); break;
				case "initialMarginReq": row.InitialMarginRequirement = AsDecimal(value); break;
				case "maintMarginReq": row.MaintenanceMarginRequirement = AsDecimal(value); break;
				case "openNetPnL": row.OpenNetPnL = AsDecimal(value); break;
			}
		});
		return row;
	}

	private static void ReadRow(JsonReader reader, string[] columns, Action<string, object> setter)
	{
		var index = 0;
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
		{
			if (reader.TokenType is JsonToken.StartArray or JsonToken.StartObject)
			{
				reader.Skip();
				index++;
				continue;
			}
			if (index < columns.Length)
				setter(columns[index], reader.Value);
			index++;
		}
	}

	private static long AsLong(object value)
		=> value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);

	private static decimal AsDecimal(object value)
		=> value == null ? 0 : Convert.ToDecimal(value, CultureInfo.InvariantCulture);

	private static bool AsBool(object value)
		=> value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture);

	private async Task<T> Send<T>(HttpMethod method, string path, object body, bool authenticated,
		bool includeAccountNumber, CancellationToken cancellationToken)
	{
		var payload = await SendText(method, path, body, authenticated, includeAccountNumber,
			cancellationToken);
		return JsonConvert.DeserializeObject<T>(payload);
	}

	private async Task<string> SendText(HttpMethod method, string path, object body, bool authenticated,
		bool includeAccountNumber, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (authenticated)
				await EnsureTokenLocked(cancellationToken);

			for (var attempt = 1; ; attempt++)
			{
				using var request = new HttpRequestMessage(method, path);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				if (authenticated)
					request.Headers.Authorization = new("Bearer", _accessToken);
				if (includeAccountNumber)
					request.Headers.TryAddWithoutValidation("accNum",
						_accountNumber.ToString(CultureInfo.InvariantCulture));
				if (!_developerApiKey.IsEmpty())
					request.Headers.TryAddWithoutValidation("tl-developer-api-key", _developerApiKey);
				if (body != null)
					request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8,
						"application/json");

				var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				var payload = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.IsSuccessStatusCode)
				{
					response.Dispose();
					if (payload.IsEmpty())
						throw new InvalidOperationException(
							$"TradeLocker {method} {path} returned an empty response.");
					return payload;
				}

				if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < _maxAttempts)
				{
					var delay = response.Headers.RetryAfter?.Delta ??
						TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
					response.Dispose();
					await Task.Delay(delay, cancellationToken);
					continue;
				}

				var status = response.StatusCode;
				var reason = payload.IsEmpty(response.ReasonPhrase);
				response.Dispose();
				throw new TradeLockerApiException(status,
					$"TradeLocker {method} {path} failed ({(int)status}): {reason}");
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private Task EnsureToken(CancellationToken cancellationToken)
		=> _tokenExpires > DateTimeOffset.UtcNow.AddMinutes(1)
			? Task.CompletedTask
			: Refresh(cancellationToken);

	private Task EnsureTokenLocked(CancellationToken cancellationToken)
		=> _tokenExpires > DateTimeOffset.UtcNow.AddMinutes(1)
			? Task.CompletedTask
			: RefreshLocked(cancellationToken);

	private async Task Refresh(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await EnsureTokenLocked(cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task RefreshLocked(CancellationToken cancellationToken)
	{
		if (_refreshToken.IsEmpty())
			return;

		using var request = new HttpRequestMessage(HttpMethod.Post, "auth/jwt/refresh")
		{
			Content = new StringContent(JsonConvert.SerializeObject(
				new TradeLockerRefreshRequest { RefreshToken = _refreshToken }), Encoding.UTF8,
				"application/json"),
		};
		var response = await _http.SendAsync(request, cancellationToken);
		var payload = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new TradeLockerApiException(response.StatusCode,
				$"TradeLocker token refresh failed ({(int)response.StatusCode}): {payload}");
		SetTokens(JsonConvert.DeserializeObject<TradeLockerTokenResponse>(payload));
	}

	private void SetTokens(TradeLockerTokenResponse token)
	{
		_accessToken = token?.AccessToken.ThrowIfEmpty(nameof(token.AccessToken));
		_refreshToken = token?.RefreshToken.ThrowIfEmpty(nameof(token.RefreshToken));
		_tokenExpires = DateTimeOffset.UtcNow.AddMinutes(10);
	}

	private static void EnsureOk(string status, string operation)
	{
		if (!status.EqualsIgnoreCase("ok"))
			throw new InvalidOperationException($"TradeLocker failed to {operation}: {status}.");
	}

	protected override void DisposeManaged()
	{
		_gate.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
