namespace StockSharp.GMTrade.Native;

sealed class GMTradeRpcClient : BaseLogReceiver
{
	public const string SolMint = "11111111111111111111111111111111";
	private const string _tokenProgram =
		"TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
	private const string _token2022Program =
		"TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";
	private const int _maximumResponseBytes = 16 * 1024 * 1024;

	private readonly HttpClient _http;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private long _requestId;
	private DateTime _nextRequestTime;

	public GMTradeRpcClient(string endpoint, string walletAddress)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"GMTrade Solana RPC endpoint must use HTTP or HTTPS.",
				nameof(endpoint));
		WalletAddress = walletAddress.IsEmpty()
			? null
			: walletAddress.NormalizePublicKey(nameof(walletAddress));
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			BaseAddress = uri,
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-GMTrade-Connector/1.0");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"Origin", "https://gmtrade.xyz");
	}

	public override string Name => "GMTRADE_SOLANA_RPC";

	public string WalletAddress { get; }

	public bool IsWalletAvailable => !WalletAddress.IsEmpty();

	public async ValueTask<GMTradeWalletBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
	{
		EnsureWallet();
		var lamports = (await SendAsync<GMTradeRpcContextValue<ulong>>(
			"getBalance", new GMTradeRpcBalanceParameters
			{
				Owner = WalletAddress,
				Config = new(),
			}, cancellationToken)).Value;
		var accounts = new List<GMTradeRpcTokenAccount>();
		foreach (var program in new[] { _tokenProgram, _token2022Program })
		{
			var response = await SendAsync<GMTradeRpcContextValue<
				GMTradeRpcTokenAccount[]>>("getTokenAccountsByOwner",
				new GMTradeRpcTokenAccountsParameters
				{
					Owner = WalletAddress,
					Filter = new() { ProgramId = program },
					Config = new(),
				}, cancellationToken);
			accounts.AddRange(response.Value ?? []);
		}

		var result = new List<GMTradeWalletBalance>
		{
			new()
			{
				Mint = SolMint,
				Amount = lamports.ToString(CultureInfo.InvariantCulture),
				Decimals = 9,
			},
		};
		foreach (var group in accounts
			.Select(static account => account?.Account?.Data?.Parsed?.Information)
			.Where(static info => info?.Mint.IsEmpty() == false &&
				info.TokenAmount?.Amount.IsEmpty() == false)
			.GroupBy(static info => info.Mint, StringComparer.Ordinal))
		{
			var decimals = group.First().TokenAmount.Decimals;
			if (group.Any(info => info.TokenAmount.Decimals != decimals))
				throw new InvalidDataException(
					$"Solana RPC returned inconsistent decimals for '{group.Key}'.");
			var amount = BigInteger.Zero;
			foreach (var item in group)
			{
				if (!BigInteger.TryParse(item.TokenAmount.Amount,
					NumberStyles.None, CultureInfo.InvariantCulture, out var current) ||
					current < 0)
					throw new InvalidDataException(
						$"Solana RPC returned invalid token balance " +
						$"'{item.TokenAmount.Amount}'.");
				amount += current;
			}
			result.Add(new()
			{
				Mint = group.Key,
				Amount = amount.ToString(CultureInfo.InvariantCulture),
				Decimals = decimals,
			});
		}
		return [.. result];
	}

	private async ValueTask<TResult> SendAsync<TResult>(string method,
		GMTradeRpcParameters parameters, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			var request = new GMTradeRpcRequest<GMTradeRpcParameters>
			{
				Id = Interlocked.Increment(ref _requestId),
				Method = method,
				Parameters = parameters,
			};
			using var content = new StringContent(JsonConvert.SerializeObject(
				request, _jsonSettings), Encoding.UTF8, "application/json");
			using var response = await _http.PostAsync(string.Empty, content,
				cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (attempt < 2 && (response.StatusCode ==
					HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					$"GMTrade Solana RPC HTTP {(int)response.StatusCode}: " +
					Limit(body, 1024));
			GMTradeRpcResponse<TResult> envelope;
			try
			{
				envelope = JsonConvert.DeserializeObject<
					GMTradeRpcResponse<TResult>>(body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					$"Solana RPC returned malformed JSON for '{method}'.", error);
			}
			if (envelope is null)
				throw new InvalidDataException(
					$"Solana RPC returned no response for '{method}'.");
			if (envelope.Error is not null)
				throw new InvalidOperationException(
					$"Solana RPC '{method}' failed ({envelope.Error.Code}): " +
					envelope.Error.Message);
			return envelope.Result;
		}
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(50);
		}
		finally
		{
			_requestSync.Release();
		}
	}

	private void EnsureWallet()
	{
		if (!IsWalletAvailable)
			throw new InvalidOperationException(
				"A Solana wallet address is required for account monitoring.");
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Solana RPC response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Solana RPC response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	protected override void DisposeManaged()
	{
		_requestSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
