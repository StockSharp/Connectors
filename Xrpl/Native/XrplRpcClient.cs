namespace StockSharp.Xrpl.Native;

sealed class XrplRpcClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _requestGate = new(8, 8);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private long _requestId;

	public XrplRpcClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"XRPL RPC endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-XRPL-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "XRPL_JSON_RPC";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask VerifyAsync(CancellationToken cancellationToken)
	{
		var result = await CallAsync("server_info", new(),
			cancellationToken);
		var info = result["info"] as JObject ??
			throw new InvalidDataException(
				"XRPL RPC returned no server information.");
		if (info.Value<string>("network_id") is { Length: > 0 } network &&
			network != "0")
			throw new InvalidDataException(
				$"XRPL RPC belongs to network '{network}', not mainnet.");
		var state = info.Value<string>("server_state");
		if (state.IsEmpty() || state.EqualsIgnoreCase("disconnected"))
			throw new InvalidOperationException(
				$"XRPL RPC server state is '{state ?? "unknown"}'.");
	}

	public async ValueTask<XrplLedgerPoint> GetLedgerAsync(
		uint? ledgerIndex, CancellationToken cancellationToken)
	{
		var parameters = new JObject
		{
			["ledger_index"] = ledgerIndex is uint index
				? index
				: "validated",
			["transactions"] = false,
			["expand"] = false,
		};
		var result = await CallAsync("ledger", parameters,
			cancellationToken);
		var ledger = result["ledger"] as JObject ?? result;
		var indexValue = ledger.Value<uint?>("ledger_index") ??
			result.Value<uint?>("ledger_index") ??
			throw new InvalidDataException(
				"XRPL ledger response contains no index.");
		var closeTime = ledger.Value<long?>("close_time") ??
			throw new InvalidDataException(
				"XRPL ledger response contains no close time.");
		return new()
		{
			Index = indexValue,
			Time = XrplExtensions.FromRippleTime(closeTime),
		};
	}

	public async ValueTask<XrplBook> GetBookAsync(XrplMarket market,
		int depth, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		depth = depth.Max(1).Min(400);
		var asksTask = GetBookSideAsync(market.Base, market.Quote,
			market.DomainId, depth, cancellationToken).AsTask();
		var bidsTask = GetBookSideAsync(market.Quote, market.Base,
			market.DomainId, depth, cancellationToken).AsTask();
		await Task.WhenAll(asksTask, bidsTask);
		return XrplExtensions.ParseBook(market, asksTask.Result,
			bidsTask.Result, depth, DateTime.UtcNow);
	}

	public ValueTask<JObject> GetBookChangesAsync(uint? ledgerIndex,
		CancellationToken cancellationToken)
		=> CallAsync("book_changes", new JObject
		{
			["ledger_index"] = ledgerIndex is uint index
				? index
				: "validated",
		}, cancellationToken);

	public async ValueTask<XrplAccountState> GetAccountStateAsync(
		string account, CancellationToken cancellationToken)
	{
		account = ValidateAccount(account);
		var result = await CallAsync("account_info", new JObject
		{
			["account"] = account,
			["ledger_index"] = "validated",
			["queue"] = false,
			["signer_lists"] = false,
		}, cancellationToken);
		var data = result["account_data"] as JObject ??
			throw new InvalidDataException(
				"XRPL account_info returned no account data.");
		var drops = XrplExtensions.ParseDecimal(data["Balance"],
			"Balance");
		var sequence = data.Value<uint?>("Sequence") ??
			throw new InvalidDataException(
				"XRPL account_info returned no sequence.");
		return new()
		{
			Account = account,
			XrpBalance = drops / 1_000_000m,
			Sequence = sequence,
			LedgerIndex = result.Value<uint?>("ledger_index") ?? 0,
		};
	}

	public async ValueTask<XrplBalance[]> GetBalancesAsync(string account,
		CancellationToken cancellationToken)
	{
		var state = await GetAccountStateAsync(account, cancellationToken);
		var result = new List<XrplBalance>
		{
			new()
			{
				Asset = XrplExtensions.ParseAsset("XRP"),
				Current = state.XrpBalance,
			},
		};
		JToken marker = null;
		do
		{
			var parameters = new JObject
			{
				["account"] = state.Account,
				["ledger_index"] = "validated",
				["limit"] = 400,
			};
			if (marker is not null)
				parameters["marker"] = marker.DeepClone();
			var page = await CallAsync("account_lines", parameters,
				cancellationToken);
			foreach (var line in page["lines"]?.OfType<JObject>() ?? [])
			{
				var code = line.Value<string>("currency");
				var issuer = line.Value<string>("account");
				if (code.IsEmpty() || issuer.IsEmpty())
					continue;
				XrplAsset asset;
				try
				{
					asset = XrplExtensions.ParseAsset(
						$"{code}:{issuer}");
				}
				catch (FormatException)
				{
					continue;
				}
				result.Add(new()
				{
					Asset = asset,
					Current = XrplExtensions.ParseDecimal(
						line["balance"], "balance"),
				});
			}
			marker = page["marker"];
		}
		while (marker is not null && marker.Type != JTokenType.Null);
		return
		[
			.. result.GroupBy(static balance => balance.Asset.Key,
					StringComparer.OrdinalIgnoreCase)
				.Select(static group => new XrplBalance
				{
					Asset = group.First().Asset,
					Current = group.Sum(static item => item.Current),
				})
		];
	}

	public async ValueTask<XrplAccountOffer[]> GetAccountOffersAsync(
		string account, IEnumerable<XrplMarket> markets,
		CancellationToken cancellationToken)
	{
		account = ValidateAccount(account);
		ArgumentNullException.ThrowIfNull(markets);
		var marketArray = markets.ToArray();
		var offers = new List<XrplAccountOffer>();
		JToken marker = null;
		do
		{
			var parameters = new JObject
			{
				["account"] = account,
				["ledger_index"] = "validated",
				["limit"] = 400,
			};
			if (marker is not null)
				parameters["marker"] = marker.DeepClone();
			var page = await CallAsync("account_offers", parameters,
				cancellationToken);
			foreach (var offer in page["offers"]?.OfType<JObject>() ?? [])
			{
				var parsed = TryParseOffer(offer, marketArray);
				if (parsed is not null)
					offers.Add(parsed);
			}
			marker = page["marker"];
		}
		while (marker is not null && marker.Type != JTokenType.Null);
		return [.. offers];
	}

	public async ValueTask<decimal> GetFeeDropsAsync(decimal multiplier,
		CancellationToken cancellationToken)
	{
		if (multiplier is < 1m or > 100m)
			throw new ArgumentOutOfRangeException(nameof(multiplier));
		var result = await CallAsync("fee", new(), cancellationToken);
		var drops = result["drops"] as JObject ??
			throw new InvalidDataException("XRPL fee returned no drops.");
		var baseFee = XrplExtensions.ParseDecimal(
			drops["open_ledger_fee"] ?? drops["minimum_fee"] ??
				drops["base_fee"], "open_ledger_fee");
		return decimal.Ceiling(baseFee * multiplier);
	}

	public async ValueTask<XrplSubmitResult> SubmitAsync(string blob,
		CancellationToken cancellationToken)
	{
		blob = blob.ThrowIfEmpty(nameof(blob)).Trim();
		var result = await CallAsync("submit", new JObject
		{
			["tx_blob"] = blob,
			["fail_hard"] = true,
		}, cancellationToken, true);
		var transaction = result["tx_json"] as JObject ??
			result["tx"] as JObject;
		return new()
		{
			Hash = transaction?.Value<string>("hash"),
			EngineResult = result.Value<string>("engine_result"),
			Message = result.Value<string>("engine_result_message"),
			LedgerIndex = result.Value<uint?>("ledger_current_index"),
		};
	}

	public async ValueTask<XrplTransactionStatus> GetTransactionAsync(
		string hash, CancellationToken cancellationToken)
	{
		hash = hash.ThrowIfEmpty(nameof(hash)).Trim().ToUpperInvariant();
		var result = await CallAsync("tx", new JObject
		{
			["transaction"] = hash,
			["binary"] = false,
		}, cancellationToken, true);
		var transaction = result["tx_json"] as JObject ??
			result["transaction"] as JObject ?? result;
		var metadata = result["meta"] as JObject ??
			result["metaData"] as JObject;
		var date = transaction.Value<long?>("date") ??
			result.Value<long?>("date");
		return new()
		{
			Hash = result.Value<string>("hash") ??
				transaction.Value<string>("hash") ?? hash,
			Validated = result.Value<bool?>("validated") == true,
			Result = metadata?.Value<string>("TransactionResult") ??
				result.Value<string>("engine_result"),
			Time = date is long seconds
				? XrplExtensions.FromRippleTime(seconds)
				: null,
			LedgerIndex = result.Value<uint?>("ledger_index") ??
				transaction.Value<uint?>("ledger_index"),
			Sequence = transaction.Value<uint?>("Sequence"),
			Transaction = transaction,
			Metadata = metadata,
		};
	}

	private ValueTask<JObject> GetBookSideAsync(XrplAsset gets,
		XrplAsset pays, string domainId, int depth,
		CancellationToken cancellationToken)
	{
		var parameters = new JObject
		{
			["taker_gets"] = gets.ToCurrencySpec(),
			["taker_pays"] = pays.ToCurrencySpec(),
			["ledger_index"] = "validated",
			["limit"] = depth,
		};
		if (!domainId.IsEmpty())
			parameters["domain"] = domainId;
		return CallAsync("book_offers", parameters, cancellationToken);
	}

	private async ValueTask<JObject> CallAsync(string method,
		JObject parameters, CancellationToken cancellationToken,
		bool allowEngineFailure = false)
	{
		method = method.ThrowIfEmpty(nameof(method));
		parameters ??= new();
		parameters["api_version"] = 2;
		var requestBody = new JObject
		{
			["jsonrpc"] = "2.0",
			["id"] = Interlocked.Increment(ref _requestId),
			["method"] = method,
			["params"] = new JArray(parameters),
		};
		for (var attempt = 0; ; attempt++)
		{
			await _requestGate.WaitAsync(cancellationToken);
			try
			{
				using var request = new HttpRequestMessage(HttpMethod.Post,
					_endpoint)
				{
					Content = new StringContent(
						JsonConvert.SerializeObject(requestBody,
							_jsonSettings), Encoding.UTF8,
						"application/json"),
				};
				using var response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				var content = await ReadBodyAsync(response.Content,
					cancellationToken);
				if (attempt < 3 && (response.StatusCode ==
						(HttpStatusCode)429 ||
						(int)response.StatusCode >= 500))
				{
					await Task.Delay(TimeSpan.FromMilliseconds(
						250 * (1 << attempt)), cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw new InvalidOperationException(
						$"XRPL RPC HTTP {(int)response.StatusCode}: " +
							Truncate(content));
				JObject envelope;
				try
				{
					envelope = JsonConvert.DeserializeObject<JObject>(
						content, _jsonSettings);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						"XRPL RPC returned an unexpected payload.", error);
				}
				if (envelope is null)
					throw new InvalidDataException(
						"XRPL RPC returned an empty payload.");
				if (envelope["error"] is JObject rpcError)
					throw CreateRpcError(method, rpcError);
				var result = envelope["result"] as JObject ??
					throw new InvalidDataException(
						$"XRPL RPC '{method}' returned no result.");
				var errorCode = result.Value<string>("error");
				var status = result.Value<string>("status");
				if (!errorCode.IsEmpty())
					throw CreateResultError(method, result);
				if (!allowEngineFailure &&
					!status.IsEmpty() &&
					!status.EqualsIgnoreCase("success"))
					throw CreateResultError(method, result);
				return result;
			}
			finally
			{
				_requestGate.Release();
			}
		}
	}

	private static XrplAccountOffer TryParseOffer(JObject offer,
		IEnumerable<XrplMarket> markets)
	{
		var gets = offer["TakerGets"];
		var pays = offer["TakerPays"];
		foreach (var market in markets)
		{
			try
			{
				var baseGets = market.Base.ParseAmount(gets, "TakerGets");
				var quotePays = market.Quote.ParseAmount(pays, "TakerPays");
				if (baseGets > 0 && quotePays > 0)
				{
					var balance = offer["taker_gets_funded"] is
						JToken fundedGets
							? market.Base.ParseAmount(fundedGets,
								"taker_gets_funded")
							: baseGets;
					return CreateOffer(offer, market, Sides.Sell,
						baseGets, quotePays, balance);
				}
			}
			catch (InvalidDataException)
			{
			}
			try
			{
				var quoteGets = market.Quote.ParseAmount(gets, "TakerGets");
				var basePays = market.Base.ParseAmount(pays, "TakerPays");
				if (basePays > 0 && quoteGets > 0)
				{
					var balance = offer["taker_pays_funded"] is
						JToken fundedPays
							? market.Base.ParseAmount(fundedPays,
								"taker_pays_funded")
							: basePays;
					return CreateOffer(offer, market, Sides.Buy,
						basePays, quoteGets, balance);
				}
			}
			catch (InvalidDataException)
			{
			}
		}
		return null;
	}

	private static XrplAccountOffer CreateOffer(JObject offer,
		XrplMarket market, Sides side, decimal volume, decimal quote,
		decimal balance)
	{
		var sequence = offer.Value<uint?>("seq") ??
			offer.Value<uint?>("Sequence") ??
			throw new InvalidDataException(
				"XRPL account offer contains no sequence.");
		var expiration = offer.Value<long?>("expiration") ??
			offer.Value<long?>("Expiration");
		return new()
		{
			Sequence = sequence,
			Market = market,
			Side = side,
			Price = quote / volume,
			Volume = volume,
			Balance = balance,
			Expiration = expiration is long seconds
				? XrplExtensions.FromRippleTime(seconds)
				: null,
		};
	}

	private static string ValidateAccount(string account)
	{
		account = account.ThrowIfEmpty(nameof(account)).Trim();
		if (!XrplCodec.IsValidClassicAddress(account))
			throw new ArgumentException(
				$"XRPL account '{account}' is not a valid classic address.",
				nameof(account));
		return account;
	}

	private static InvalidOperationException CreateRpcError(string method,
		JObject error)
		=> new(
			$"XRPL RPC '{method}' failed ({error.Value<int?>("code")}): " +
				(error.Value<string>("message") ?? "request rejected"));

	private static InvalidOperationException CreateResultError(string method,
		JObject result)
		=> new(
			$"XRPL RPC '{method}' failed " +
				$"({result.Value<string>("error") ?? result.Value<string>("status")}): " +
				(result.Value<string>("error_message") ??
					result.Value<string>("error_exception") ??
					"request rejected"));

	private static async ValueTask<string> ReadBodyAsync(
		HttpContent content, CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"XRPL RPC response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(
			cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"XRPL RPC response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

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
