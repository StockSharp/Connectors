namespace StockSharp.Gmx;

public partial class GmxMessageAdapter
{
	private GmxNetworks _network = GmxNetworks.Arbitrum;
	private string _apiEndpoint = GmxNetworks.Arbitrum.ApiEndpoint(false);
	private string _secondaryApiEndpoint =
		GmxNetworks.Arbitrum.ApiEndpoint(true);
	private string _defaultCollateralToken =
		GmxNetworks.Arbitrum.DefaultCollateral();

	/// <summary>GMX production network.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public GmxNetworks Network
	{
		get => _network;
		set
		{
			if (_network == value)
				return;
			if (string.Equals(_apiEndpoint, _network.ApiEndpoint(false),
				StringComparison.OrdinalIgnoreCase))
				_apiEndpoint = value.ApiEndpoint(false);
			if (string.Equals(_secondaryApiEndpoint, _network.ApiEndpoint(true),
				StringComparison.OrdinalIgnoreCase))
				_secondaryApiEndpoint = value.ApiEndpoint(true);
			if (string.Equals(_defaultCollateralToken,
				_network.DefaultCollateral(),
				StringComparison.OrdinalIgnoreCase))
				_defaultCollateralToken = value.DefaultCollateral();
			_network = value;
		}
	}

	/// <summary>Primary official GMX API endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	[BasicSetting]
	public string ApiEndpoint
	{
		get => _apiEndpoint;
		set => _apiEndpoint = value;
	}

	/// <summary>Secondary official GMX API peer.</summary>
	[Display(Name = "Secondary API",
		Description = "Independent official GMX API peer used for safe reads.",
		GroupName = LocalizedStrings.AddressesKey, Order = 2)]
	public string SecondaryApiEndpoint
	{
		get => _secondaryApiEndpoint;
		set => _secondaryApiEndpoint = value;
	}

	/// <summary>Optional EVM wallet for read-only account data.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional EVM private key for express-order signing.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private decimal _defaultLeverage = 5m;

	/// <summary>Leverage used to derive collateral when it is not supplied.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 5)]
	public decimal DefaultLeverage
	{
		get => _defaultLeverage;
		set => _defaultLeverage = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"GMX leverage must be between one and 1000.");
	}

	private decimal _slippage = 0.3m;

	/// <summary>Default order slippage in percent.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 6)]
	public decimal Slippage
	{
		get => _slippage;
		set => _slippage = value is >= 0 and <= 50
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"GMX slippage must be between zero and 50%.");
	}

	/// <summary>Default position collateral token.</summary>
	[Display(Name = "Collateral token",
		GroupName = LocalizedStrings.TransactionKey, Order = 7)]
	public string DefaultCollateralToken
	{
		get => _defaultCollateralToken;
		set => _defaultCollateralToken = value.ThrowIfEmpty(nameof(value)).Trim();
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(2);

	/// <summary>Polling interval for current API snapshots.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(1) &&
			value <= TimeSpan.FromMinutes(5)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"GMX polling interval must be between one second and five minutes.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum rows requested for one history subscription.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 9)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 10000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"GMX history limit must be between one and 10000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Network), Network)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(SecondaryApiEndpoint), SecondaryApiEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(DefaultLeverage), DefaultLeverage)
			.Set(nameof(Slippage), Slippage)
			.Set(nameof(DefaultCollateralToken), DefaultCollateralToken)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Network = storage.GetValue(nameof(Network), Network);
		ApiEndpoint = NormalizeEndpoint(storage.GetValue(nameof(ApiEndpoint),
			ApiEndpoint), nameof(ApiEndpoint));
		SecondaryApiEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(SecondaryApiEndpoint), SecondaryApiEndpoint),
			nameof(SecondaryApiEndpoint));
		WalletAddress = storage.GetValue(nameof(WalletAddress), WalletAddress);
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		DefaultLeverage = storage.GetValue(nameof(DefaultLeverage),
			DefaultLeverage);
		Slippage = storage.GetValue(nameof(Slippage), Slippage);
		DefaultCollateralToken = storage.GetValue(nameof(DefaultCollateralToken),
			DefaultCollateralToken);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	private static string NormalizeEndpoint(string endpoint,
		string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"GMX API endpoint must use HTTP or HTTPS.", parameterName);
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + ": " + Network.NetworkName() +
			(WalletAddress.IsEmpty() && PrivateKey.IsEmpty()
				? " Public"
				: PrivateKey.IsEmpty() ? " Read-only" : " Trading");
}
