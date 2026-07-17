namespace StockSharp.Marquee;

public partial class MarqueeMessageAdapter
{
	private const string _boardCode = "MARQ";
	private static readonly DataType _dailyCandles = TimeSpan.FromDays(1).TimeFrame().Immutable();

	private MarqueeClient _client;
	private readonly SynchronizedDictionary<string, MarqueeAsset> _assets =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Initializes a new instance.</summary>
	public MarqueeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames([TimeSpan.FromDays(1)]);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == _dailyCandles;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [_boardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (ClientId.IsEmpty())
			throw new InvalidOperationException(
				$"{LocalizedStrings.ClientId}: {LocalizedStrings.InvalidValue}.");
		if (ClientSecret.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);

		var client = new MarqueeClient(ClientId, ClientSecret.UnSecure(), IsDemo) { Parent = this };
		_client = client;
		try
		{
			await client.Authenticate(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			client.Dispose();
			_client = null;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeClient();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		_assets.Clear();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
	}

	private MarqueeClient SafeClient()
		=> _client ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private static string GetAssetKey(string code, string board)
		=> $"{code}@{board}";

	private void CacheAsset(MarqueeAsset asset)
	{
		var code = asset.GetSecurityCode();
		if (code.IsEmpty())
			return;

		var board = asset.GetBoardCode();
		_assets[GetAssetKey(code, board)] = asset;
		if (!_assets.ContainsKey(code))
			_assets[code] = asset;
	}

	private async Task<MarqueeAsset> ResolveAsset(SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (!securityId.BoardCode.IsEmpty() &&
			_assets.TryGetValue(GetAssetKey(code, securityId.BoardCode), out var cached))
			return cached;
		if (_assets.TryGetValue(code, out cached))
			return cached;

		var matches = new List<MarqueeAsset>();
		await foreach (var asset in SafeClient().LookupAssets(code, null, cancellationToken)
			.WithEnforcedCancellation(cancellationToken))
		{
			CacheAsset(asset);
			matches.Add(asset);
			if (matches.Count >= 100)
				break;
		}

		var exact = matches
			.Where(asset => asset.GetSecurityCode().EqualsIgnoreCase(code))
			.OrderByDescending(asset => !securityId.BoardCode.IsEmpty() &&
				asset.GetBoardCode().EqualsIgnoreCase(securityId.BoardCode))
			.ThenByDescending(asset => asset.Rank ?? 0)
			.FirstOrDefault();

		return exact ?? throw new InvalidOperationException(
			$"Goldman Sachs Marquee asset '{securityId}' was not found or is not entitled.");
	}
}
