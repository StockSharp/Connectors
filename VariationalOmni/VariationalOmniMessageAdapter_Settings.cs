namespace StockSharp.VariationalOmni;

/// <summary>
/// The read-only message adapter for Variational Omni.
/// </summary>
[MediaIcon(Media.MediaNames.variational_omni)]
[Doc("topics/api/connectors/crypto_exchanges/variational_omni.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.VariationalOmniKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Level1)]
public partial class VariationalOmniMessageAdapter : MessageAdapter
{
	private const string _defaultEndpoint =
		"https://omni-client-api.prod.ap-northeast-1.variational.io";

	/// <summary>
	/// Public Variational Omni REST endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string Endpoint { get; set; } = _defaultEndpoint;

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

	/// <summary>
	/// Interval between public market-statistics refreshes.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(1)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Variational Omni polling cannot be faster than once per second.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Endpoint), Endpoint)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Endpoint = NormalizeEndpoint(storage.GetValue(nameof(Endpoint), Endpoint));
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.IsEmpty() ? _defaultEndpoint : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			(!uri.Scheme.Equals(Uri.UriSchemeHttp,
				StringComparison.OrdinalIgnoreCase) &&
			 !uri.Scheme.Equals(Uri.UriSchemeHttps,
				StringComparison.OrdinalIgnoreCase)))
			throw new ArgumentException(
				"Variational Omni endpoint must be an HTTP or HTTPS URI.",
				nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Endpoint={Endpoint}";
}
