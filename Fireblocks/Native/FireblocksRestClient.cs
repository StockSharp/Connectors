namespace StockSharp.Fireblocks.Native;

sealed class FireblocksApiException : InvalidOperationException
{
	public FireblocksApiException(HttpStatusCode statusCode, int? code,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
}

sealed class FireblocksRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 32 * 1024 * 1024;
	private readonly HttpClient _client;
	private readonly string _apiKey;
	private readonly RSA _signer;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private bool _isDisposed;

	public FireblocksRestClient(string endpoint, string apiKey,
		SecureString privateKey)
	{
		endpoint = endpoint.NormalizeFireblocksEndpoint().ThrowIfEmpty(
			nameof(endpoint));
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).Trim();
		if (privateKey.IsEmpty())
			throw new ArgumentNullException(nameof(privateKey));
		_signer = RSA.Create();
		try
		{
			_signer.ImportFromPem(privateKey.UnSecure().Trim());
		}
		catch
		{
			_signer.Dispose();
			throw;
		}
		_client = new()
		{
			BaseAddress = new(endpoint + "/", UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		_client.DefaultRequestHeaders.Accept.Add(new("application/json"));
		_client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Fireblocks/1.0");
	}

	public override string Name => "Fireblocks_REST";

	public async ValueTask<FireblocksVaultAccount[]> GetVaultAccountsAsync(
		int pageSize, int maximum, CancellationToken cancellationToken)
	{
		if (pageSize is < 1 or > 500)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		if (maximum < 1)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var result = new List<FireblocksVaultAccount>();
		string cursor = null;
		while (result.Count < maximum)
		{
			var take = Math.Min(pageSize, maximum - result.Count);
			var path = "vault/accounts_paged?limit=" + take.ToString(
				CultureInfo.InvariantCulture);
			if (!cursor.IsEmpty())
				path += "&after=" + Escape(cursor);
			var page = await GetAsync<FireblocksVaultAccountsPage>(path,
				cancellationToken);
			result.AddRange(page?.Accounts ?? []);
			var next = page?.Paging?.After;
			if (next.IsEmpty() || next == cursor ||
				(page.Accounts?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. result.Take(maximum)];
	}

	public async ValueTask<FireblocksAsset[]> GetAssetsAsync(int maximum,
		CancellationToken cancellationToken)
	{
		if (maximum < 1)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var result = new List<FireblocksAsset>();
		string cursor = null;
		while (result.Count < maximum)
		{
			var pageSize = Math.Min(1000,
				Math.Max(100, maximum - result.Count));
			var path = "assets?deprecated=false&pageSize=" + pageSize.ToString(
				CultureInfo.InvariantCulture);
			if (!cursor.IsEmpty())
				path += "&pageCursor=" + Escape(cursor);
			var page = await GetAsync<FireblocksAssetsPage>(path,
				cancellationToken);
			result.AddRange(page?.Data ?? []);
			var next = page?.Next;
			if (next.IsEmpty() || next == cursor ||
				(page.Data?.Length ?? 0) == 0)
				break;
			cursor = next;
		}
		return [.. result.Take(maximum)];
	}

	public async ValueTask<FireblocksAsset> TryGetAssetAsync(string id,
		CancellationToken cancellationToken)
	{
		try
		{
			return await GetAsync<FireblocksAsset>("assets/" +
				Escape(id.ThrowIfEmpty(nameof(id))), cancellationToken);
		}
		catch (FireblocksApiException error) when (
			error.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public ValueTask<FireblocksCreateTransactionResponse>
		CreateTransactionAsync(FireblocksTransactionRequest request,
			CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return PostAsync<FireblocksCreateTransactionResponse,
			FireblocksTransactionRequest>("transactions", request,
			CreateIdempotencyKey(request.ExternalTransactionId),
			cancellationToken);
	}

	public ValueTask<FireblocksTransaction> GetTransactionAsync(string id,
		CancellationToken cancellationToken)
		=> GetAsync<FireblocksTransaction>("transactions/" +
			Escape(id.ThrowIfEmpty(nameof(id))), cancellationToken);

	public ValueTask<FireblocksTransaction> GetTransactionByExternalIdAsync(
		string externalId, CancellationToken cancellationToken)
		=> GetAsync<FireblocksTransaction>("transactions/external_tx_id/" +
			Escape(externalId.ThrowIfEmpty(nameof(externalId))),
			cancellationToken);

	public async ValueTask<FireblocksTransaction>
		TryGetTransactionByExternalIdAsync(string externalId,
			CancellationToken cancellationToken)
	{
		try
		{
			return await GetTransactionByExternalIdAsync(externalId,
				cancellationToken);
		}
		catch (FireblocksApiException error) when (
			error.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public async ValueTask<FireblocksTransaction[]> GetTransactionsAsync(
		DateTime? from, DateTime? to, string sourceId, string destinationId,
		string assetId, int limit, CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 500)
			throw new ArgumentOutOfRangeException(nameof(limit));
		var path = "transactions?orderBy=createdAt&sort=DESC&limit=" +
			limit.ToString(CultureInfo.InvariantCulture);
		if (from is DateTime start)
			path += "&after=" + start.ToFireblocksMilliseconds().ToString(
				CultureInfo.InvariantCulture);
		if (to is DateTime end)
			path += "&before=" + end.ToFireblocksMilliseconds().ToString(
				CultureInfo.InvariantCulture);
		if (!sourceId.IsEmpty())
			path += "&sourceType=VAULT_ACCOUNT&sourceId=" + Escape(sourceId);
		if (!destinationId.IsEmpty())
			path += "&destType=VAULT_ACCOUNT&destId=" + Escape(destinationId);
		if (!assetId.IsEmpty())
			path += "&assets=" + Escape(assetId);
		return await GetAsync<FireblocksTransaction[]>(path, cancellationToken)
			?? [];
	}

	public async ValueTask CancelTransactionAsync(string id,
		CancellationToken cancellationToken)
	{
		id = id.ThrowIfEmpty(nameof(id));
		var response = await SendAsync<FireblocksBooleanResponse>(
			HttpMethod.Post, "transactions/" + Escape(id) + "/cancel", [],
			CreateIdempotencyKey("cancel:" + id), cancellationToken);
		if (response?.IsSuccess == false)
			throw new InvalidOperationException(
				$"Fireblocks did not cancel transaction '{id}'.");
	}

	private ValueTask<TResponse> GetAsync<TResponse>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(HttpMethod.Get, path, [], null,
			cancellationToken);

	private ValueTask<TResponse> PostAsync<TResponse, TRequest>(string path,
		TRequest request, string idempotencyKey,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var json = JsonConvert.SerializeObject(request, _settings);
		return SendAsync<TResponse>(HttpMethod.Post, path,
			Encoding.UTF8.GetBytes(json), idempotencyKey, cancellationToken);
	}

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, byte[] body, string idempotencyKey,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		await _gate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 0; ; attempt++)
			{
				using var request = new HttpRequestMessage(method, path);
				var uri = new Uri(_client.BaseAddress, path);
				request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
				request.Headers.Authorization = new("Bearer",
					CreateToken(uri.PathAndQuery, body));
				if (!idempotencyKey.IsEmpty())
					request.Headers.TryAddWithoutValidation("Idempotency-Key",
						idempotencyKey);
				if (body.Length > 0)
					request.Content = new ByteArrayContent(body)
					{
						Headers = { ContentType = new("application/json") },
					};

				HttpResponseMessage response;
				try
				{
					response = await _client.SendAsync(request,
						HttpCompletionOption.ResponseHeadersRead,
						cancellationToken);
				}
				catch (Exception error) when (attempt < 3 &&
					!cancellationToken.IsCancellationRequested &&
					error is HttpRequestException or TaskCanceledException)
				{
					await Task.Delay(TimeSpan.FromMilliseconds(
						250 * (1 << attempt)), cancellationToken);
					continue;
				}
				using (response)
				{
					var bytes = await ReadResponseAsync(response, cancellationToken);
					if (response.IsSuccessStatusCode)
					{
						if (bytes.Length == 0)
							return default;
						var result = JsonConvert.DeserializeObject<TResponse>(
							Encoding.UTF8.GetString(bytes), _settings);
						return result;
					}
					if (attempt < 3 && IsTransient(response.StatusCode))
					{
						var delay = response.Headers.RetryAfter?.Delta ??
							TimeSpan.FromMilliseconds(250 * (1 << attempt));
						if (delay < TimeSpan.Zero)
							delay = TimeSpan.Zero;
						if (delay > TimeSpan.FromSeconds(10))
							delay = TimeSpan.FromSeconds(10);
						await Task.Delay(delay, cancellationToken);
						continue;
					}
					throw CreateException(response.StatusCode, bytes);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private string CreateToken(string pathAndQuery, byte[] body)
	{
		var now = checked((long)(DateTime.UtcNow - DateTime.UnixEpoch)
			.TotalSeconds);
		var header = Base64Url(Encoding.UTF8.GetBytes(
			JsonConvert.SerializeObject(new FireblocksJwtHeader(), _settings)));
		var payload = Base64Url(Encoding.UTF8.GetBytes(
			JsonConvert.SerializeObject(new FireblocksJwtPayload
			{
				Uri = pathAndQuery,
				Nonce = Guid.NewGuid().ToString(),
				IssuedAt = now,
				ExpiresAt = now + 25,
				Subject = _apiKey,
				BodyHash = Convert.ToHexString(SHA256.HashData(body))
					.ToLowerInvariant(),
			}, _settings)));
		var signingInput = header + "." + payload;
		var signature = _signer.SignData(Encoding.ASCII.GetBytes(signingInput),
			HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		return signingInput + "." + Base64Url(signature);
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Fireblocks response is too large ({length} bytes).");
		var bytes = await response.Content.ReadAsByteArrayAsync(
			cancellationToken);
		if (bytes.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Fireblocks response is too large ({bytes.Length} bytes).");
		return bytes;
	}

	private FireblocksApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		FireblocksErrorResponse error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<FireblocksErrorResponse>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var message = error?.Message;
		if (message.IsEmpty())
			message = $"Fireblocks API returned HTTP {(int)statusCode} " +
				$"({statusCode}).";
		else
			message = $"Fireblocks API returned HTTP {(int)statusCode}: " +
				message;
		return new(statusCode, error?.Code, message);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 ||
			statusCode is HttpStatusCode.InternalServerError or
			HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
			HttpStatusCode.GatewayTimeout;

	private static string Base64Url(byte[] value)
		=> Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-')
			.Replace('/', '_');

	private static string CreateIdempotencyKey(string externalTransactionId)
	{
		externalTransactionId = externalTransactionId.ThrowIfEmpty(
			nameof(externalTransactionId));
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
			externalTransactionId)))[..40].ToLowerInvariant();
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_client.Dispose();
		_signer.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
