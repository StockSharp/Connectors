namespace StockSharp.StonFi.Native;

sealed class StonTonClient : BaseLogReceiver
{
	private const uint _swapV1Operation = 0x25938561;
	private const uint _swapV2Operation = 0x6664de2a;
	private const uint _jettonTransferOperation = 0x0f8a7ea5;
	private const uint _proxyTonV2TransferOperation = 0x01f3835d;
	private static readonly BigInteger _proxyTonV2TransferGas =
		new(10_000_000);

	private readonly Uri _endpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly byte[] _privateKey;
	private readonly WalletV4 _wallet;

	public StonTonClient(string endpoint, SecureString apiKey,
		SecureString mnemonic, string walletAddress, uint subwalletId,
		int revision)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"TON Center endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		if (revision is not (1 or 2))
			throw new ArgumentOutOfRangeException(nameof(revision), revision,
				"TON Wallet V4 revision must be 1 or 2.");

		var apiKeyText = apiKey.IsEmpty() ? null : apiKey.UnSecure().Trim();
		if (!apiKeyText.IsEmpty())
			_http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key",
				apiKeyText);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-StonFi-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");

		if (!mnemonic.IsEmpty())
		{
			var words = mnemonic.UnSecure().Split(
				(char[])null, StringSplitOptions.RemoveEmptyEntries |
					StringSplitOptions.TrimEntries);
			Mnemonic phrase;
			try
			{
				phrase = new(words);
			}
			catch (Exception error)
			{
				throw new ArgumentException(
					"STON.fi mnemonic must be a valid 24-word TON mnemonic.",
					nameof(mnemonic), error);
			}
			_privateKey = phrase.Keys.PrivateKey;
			_wallet = new(new WalletV4Options
			{
				PublicKey = phrase.Keys.PublicKey,
				SubwalletId = subwalletId,
				Workchain = 0,
			}, (uint)revision);
			var derived = NormalizeWalletAddress(_wallet.Address);
			if (!walletAddress.IsEmpty() &&
				!derived.SameTonAddress(walletAddress))
				throw new ArgumentException(
					"The configured STON.fi wallet does not match the TON " +
						"mnemonic.", nameof(walletAddress));
			WalletAddress = derived;
		}
		else if (!walletAddress.IsEmpty())
			WalletAddress = walletAddress.NormalizeTonAddress();
	}

	public override string Name => "STON.fi_TON";

	public string WalletAddress { get; }

	public bool IsWalletConfigured => !WalletAddress.IsEmpty();

	public bool IsSigningAvailable =>
		_wallet is not null && _privateKey is { Length: > 0 };

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_sendGate.Dispose();
		if (_privateKey is not null)
			Array.Clear(_privateKey);
		base.DisposeManaged();
	}

	public async ValueTask VerifyAsync(
		CancellationToken cancellationToken)
	{
		var response = await GetAsync<JToken>("getMasterchainInfo",
			cancellationToken);
		if (response is null || response.Type is JTokenType.Null)
			throw new InvalidDataException(
				"TON Center returned no masterchain information.");
	}

	public async ValueTask<BigInteger> GetBalanceAsync(
		CancellationToken cancellationToken)
	{
		EnsureWalletConfigured();
		var value = await GetAsync<string>("getAddressBalance?address=" +
			Uri.EscapeDataString(WalletAddress), cancellationToken);
		var balance = value.ParseInteger("balance");
		if (balance < 0)
			throw new InvalidDataException(
				"TON Center returned a negative wallet balance.");
		return balance;
	}

	public async ValueTask<StonBroadcast> SendSwapAsync(
		StonSwapSimulation quote, StonAssetInfo offerAsset,
		StonAssetInfo walletAsset, ulong queryId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(quote);
		ArgumentNullException.ThrowIfNull(offerAsset);
		EnsureSigningAvailable();
		ValidateQuote(quote);

		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			var transfer = CreateSwapTransfer(quote, offerAsset,
				walletAsset, queryId, WalletAddress,
				DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds());
			var balance = await GetBalanceAsync(cancellationToken);
			if (balance < transfer.Value)
				throw new InvalidOperationException(
					$"Insufficient TON balance: {balance} nanotons " +
						$"available, {transfer.Value} required.");
			var seqno = await GetSeqnoAsync(cancellationToken);
			var internalMessage = new InternalMessage(
				new InternalMessageOptions
				{
					Info = new IntMsgInfo(new IntMsgInfoOptions
					{
						Dest = new Address(transfer.Destination),
						Value = NanoCoins(transfer.Value),
						Bounce = true,
					}),
					Body = transfer.Body,
				});
			var message = _wallet.CreateTransferMessage(
			[
				new WalletTransfer
				{
					Message = internalMessage,
					Mode = 3,
				},
			], seqno);
			message.Sign(_privateKey);
			var boc = message.Cell.Serialize(false, true)
				.ToString("base64");
			var hash = message.Cell.Hash.ToString("hex").ToLowerInvariant();
			await SendBocAsync(boc, cancellationToken);
			return new()
			{
				ExternalMessageHash = hash,
				SequenceNumber = seqno,
			};
		}
		finally
		{
			_sendGate.Release();
		}
	}

	internal static (string Destination, BigInteger Value, Cell Body)
		CreateSwapTransfer(StonSwapSimulation quote,
			StonAssetInfo offerAsset, StonAssetInfo walletAsset,
			ulong queryId, string ownerAddress, long deadline)
	{
		ArgumentNullException.ThrowIfNull(quote);
		ArgumentNullException.ThrowIfNull(offerAsset);
		ownerAddress = ownerAddress.NormalizeTonAddress();
		ValidateQuote(quote);
		var offerUnits = quote.OfferUnits.ParseInteger("offer_units");
		var minAskUnits = (quote.RecommendedMinAskUnits.IsEmpty()
				? quote.MinAskUnits
				: quote.RecommendedMinAskUnits)
			.ParseInteger("recommended_min_ask_units");
		var gas = quote.Gas ?? throw new InvalidDataException(
			"STON.fi simulation returned no gas budget.");
		var gasBudget = gas.GasBudget.ParseInteger("gas_budget");
		var forwardGas = gas.ForwardGas.ParseInteger("forward_gas");
		if (offerUnits <= 0 || minAskUnits <= 0 || gasBudget <= 0 ||
			forwardGas <= 0)
			throw new InvalidDataException(
				"STON.fi simulation returned non-positive transaction " +
					"amounts.");

		var swapBody = quote.Router.MajorVersion switch
		{
			1 => CreateSwapBodyV1(quote.AskJettonWallet, ownerAddress,
				minAskUnits),
			2 => CreateSwapBodyV2(quote.AskJettonWallet, ownerAddress,
				minAskUnits, deadline),
			_ => throw new NotSupportedException(
				$"STON.fi router major version " +
					$"'{quote.Router.MajorVersion}' is not supported."),
		};

		if (!offerAsset.IsNative())
		{
			var wallet = walletAsset?.WalletAddress;
			if (wallet.IsEmpty())
				throw new InvalidOperationException(
					$"The STON.fi wallet has no deployed " +
						$"'{offerAsset.GetSymbol()}' jetton wallet.");
			return (wallet.NormalizeTonAddress(), gasBudget,
				CreateJettonTransferBody(queryId, offerUnits,
					quote.RouterAddress, ownerAddress, forwardGas,
					swapBody));
		}

		var proxyWallet = quote.OfferJettonWallet.NormalizeTonAddress();
		if (quote.Router.MajorVersion == 1)
			return (proxyWallet, offerUnits + forwardGas,
				CreateJettonTransferBody(queryId, offerUnits,
					quote.RouterAddress, null, forwardGas, swapBody));
		if (!quote.Router.ProxyTonVersion.EqualsIgnoreCase("2.1"))
			throw new NotSupportedException(
				$"STON.fi pTON version " +
					$"'{quote.Router.ProxyTonVersion}' is not supported.");
		return (proxyWallet,
			offerUnits + forwardGas + _proxyTonV2TransferGas,
			CreateProxyTonV2TransferBody(queryId, offerUnits,
				ownerAddress, swapBody));
	}

	internal static Cell CreateSwapBodyV1(string askJettonWallet,
		string ownerAddress, BigInteger minAskUnits)
	{
		if (minAskUnits <= 0)
			throw new ArgumentOutOfRangeException(nameof(minAskUnits));
		return new CellBuilder()
			.StoreUInt(_swapV1Operation, 32)
			.StoreAddress(new Address(
				askJettonWallet.NormalizeTonAddress()))
			.StoreCoins(NanoCoins(minAskUnits))
			.StoreAddress(new Address(ownerAddress.NormalizeTonAddress()))
			.StoreBit(false)
			.Build();
	}

	internal static Cell CreateSwapBodyV2(string askJettonWallet,
		string ownerAddress, BigInteger minAskUnits, long deadline)
	{
		if (minAskUnits <= 0)
			throw new ArgumentOutOfRangeException(nameof(minAskUnits));
		if (deadline <= 0)
			throw new ArgumentOutOfRangeException(nameof(deadline));
		var owner = new Address(ownerAddress.NormalizeTonAddress());
		var details = new CellBuilder()
			.StoreCoins(NanoCoins(minAskUnits))
			.StoreAddress(owner)
			.StoreCoins(NanoCoins(BigInteger.Zero))
			.StoreOptRef(null)
			.StoreCoins(NanoCoins(BigInteger.Zero))
			.StoreOptRef(null)
			.StoreUInt(10, 16)
			.StoreAddress(null)
			.Build();
		return new CellBuilder()
			.StoreUInt(_swapV2Operation, 32)
			.StoreAddress(new Address(
				askJettonWallet.NormalizeTonAddress()))
			.StoreAddress(owner)
			.StoreAddress(owner)
			.StoreUInt(new BigInteger(deadline), 64)
			.StoreRef(details)
			.Build();
	}

	internal static Cell CreateJettonTransferBody(ulong queryId,
		BigInteger amount, string destination, string responseDestination,
		BigInteger forwardTonAmount, Cell forwardPayload)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		if (forwardTonAmount <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(forwardTonAmount));
		ArgumentNullException.ThrowIfNull(forwardPayload);
		return new CellBuilder()
			.StoreUInt(_jettonTransferOperation, 32)
			.StoreUInt(queryId, 64)
			.StoreCoins(NanoCoins(amount))
			.StoreAddress(new Address(destination.NormalizeTonAddress()))
			.StoreAddress(responseDestination.IsEmpty()
				? null
				: new Address(responseDestination.NormalizeTonAddress()))
			.StoreBit(false)
			.StoreCoins(NanoCoins(forwardTonAmount))
			.StoreBit(true)
			.StoreRef(forwardPayload)
			.Build();
	}

	internal static Cell CreateProxyTonV2TransferBody(ulong queryId,
		BigInteger amount, string refundAddress, Cell forwardPayload)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		ArgumentNullException.ThrowIfNull(forwardPayload);
		return new CellBuilder()
			.StoreUInt(_proxyTonV2TransferOperation, 32)
			.StoreUInt(queryId, 64)
			.StoreCoins(NanoCoins(amount))
			.StoreAddress(new Address(
				refundAddress.NormalizeTonAddress()))
			.StoreBit(true)
			.StoreRef(forwardPayload)
			.Build();
	}

	private async ValueTask<uint> GetSeqnoAsync(
		CancellationToken cancellationToken)
	{
		var info = await GetAsync<TonWalletInfo>(
			"getWalletInformation?address=" +
				Uri.EscapeDataString(WalletAddress), cancellationToken);
		if (info is null)
			throw new InvalidDataException(
				"TON Center returned no wallet information.");
		if (!info.IsWallet)
		{
			if (info.AccountState.EqualsIgnoreCase("uninitialized"))
				return 0;
			throw new InvalidOperationException(
				"The configured address is not an initialized TON Wallet V4.");
		}
		return info.Seqno ?? throw new InvalidDataException(
			"TON Center returned no wallet sequence number.");
	}

	private async ValueTask SendBocAsync(string boc,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_endpoint, "sendBoc"))
		{
			Content = new StringContent(
				JsonConvert.SerializeObject(new { boc }),
				Encoding.UTF8, "application/json"),
		};
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException(
				$"TON Center sendBoc HTTP {(int)response.StatusCode}: " +
					Truncate(content));
		var result = JsonConvert.DeserializeObject<
			TonCenterResponse<JToken>>(content);
		if (result?.IsOk != true)
			throw new InvalidOperationException(
				$"TON Center rejected the signed message: " +
					(result?.Error ?? Truncate(content)));
	}

	private async ValueTask<T> GetAsync<T>(string path,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get,
			new Uri(_endpoint, path));
		using var response = await _http.SendAsync(request,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var content = await response.Content.ReadAsStringAsync(
			cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException(
				$"TON Center HTTP {(int)response.StatusCode}: " +
					Truncate(content));
		var result = JsonConvert.DeserializeObject<TonCenterResponse<T>>(
			content);
		if (result?.IsOk != true)
			throw new InvalidOperationException(
				$"TON Center request failed: " +
					(result?.Error ?? Truncate(content)));
		return result.Result;
	}

	private static void ValidateQuote(StonSwapSimulation quote)
	{
		if (quote.Router is null || quote.RouterAddress.IsEmpty() ||
			quote.PoolAddress.IsEmpty() ||
			quote.OfferJettonWallet.IsEmpty() ||
			quote.AskJettonWallet.IsEmpty() ||
			!quote.RouterAddress.SameTonAddress(quote.Router.Address))
			throw new InvalidDataException(
				"STON.fi simulation returned incomplete router data.");
		if (quote.Router.MajorVersion is not (1 or 2))
			throw new NotSupportedException(
				$"STON.fi router major version " +
					$"'{quote.Router.MajorVersion}' is not supported.");
	}

	private void EnsureWalletConfigured()
	{
		if (!IsWalletConfigured)
			throw new InvalidOperationException(
				"A STON.fi wallet address is required.");
	}

	private void EnsureSigningAvailable()
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"A 24-word TON mnemonic is required for STON.fi trading.");
	}

	private static Coins NanoCoins(BigInteger value)
	{
		if (value < 0 || value > new BigInteger(decimal.MaxValue))
			throw new ArgumentOutOfRangeException(nameof(value));
		return new(value.ToString(CultureInfo.InvariantCulture),
			new CoinsOptions(true, 9));
	}

	private static string NormalizeWalletAddress(Address address)
		=> address.ToString(TonAddressType.Base64,
			new AddressStringifyOptions(true, false, true,
				address.GetWorkchain()));

	private static string Truncate(string value)
	{
		value = value?.Trim();
		return value.IsEmpty()
			? "(empty response)"
			: value.Length <= 1000
				? value
				: value[..1000] + "...";
	}
}
