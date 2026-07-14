namespace StockSharp.InteractiveBrokers;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Authentication;

/// <summary>
/// Market data types.
/// </summary>
public enum InteractiveBrokersMarketDataTypes
{
	/// <summary>
	/// Real-time.
	/// </summary>
	RealTime = 1,

	/// <summary>
	/// Frozen market data.
	/// </summary>
	Frozen = 2,

	/// <summary>
	/// Enables delayed and disables delayed-frozen market data.
	/// </summary>
	Delayed = 3,

	/// <summary>
	/// Enables delayed and delayed-frozen market data.
	/// </summary>
	DelayedFrozen = 4,

}

/// <summary>
/// The messages adapter for InteractiveBrokers.
/// </summary>
[MediaIcon(Media.MediaNames.interactivebrokers)]
[Doc("topics/api/connectors/stock_market/interactive_brokers.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.InteractiveBrokersKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Free | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions)]
public partial class InteractiveBrokersMessageAdapter : MessageAdapter, IAddressAdapter<EndPoint>
{
	/// <summary>
	/// Address by default.
	/// </summary>
	public static readonly EndPoint DefaultAddress = new IPEndPoint(IPAddress.Loopback, 7497);

	/// <summary>
	/// Address by default.
	/// </summary>
	public static readonly EndPoint DefaultGatewayAddress = new IPEndPoint(IPAddress.Loopback, 4001);

	/// <summary>
	/// Address.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public EndPoint Address { get; set; } = DefaultAddress;

	/// <summary>
	/// Unique ID. Used when several clients are connected to one terminal or gateway.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IdentifierKey,
		Description = LocalizedStrings.IBClientIdKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public int ClientId { get; set; }

	/// <summary>
	/// Whether to use <see cref="InteractiveBrokersMarketDataTypes.RealTime"/> data or <see cref="InteractiveBrokersMarketDataTypes.Frozen"/> on the broker server. By default, the <see cref="InteractiveBrokersMarketDataTypes.RealTime"/> data is used.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RealTimeKey,
		Description = LocalizedStrings.IBRealTimeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public InteractiveBrokersMarketDataTypes MarketDataType { get; set; } = InteractiveBrokersMarketDataTypes.RealTime;

	/// <summary>
	/// The server messages logging level. The default is <see cref="ServerLogLevels.Information"/>.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LogLevelKey,
		Description = LocalizedStrings.ServerLogLevelKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public ServerLogLevels ServerLogLevel { get; set; } = ServerLogLevels.Information;

	/// <summary>
	/// The connection time.
	/// </summary>
	[Browsable(false)]
	public DateTime? ConnectedTime { get; private set; }

	/// <summary>
	/// Extra authentication.
	/// </summary>
	[Browsable(false)]
	public bool ExtraAuth { get; set; }

	/// <summary>
	/// Optional capabilities.
	/// </summary>
	[Browsable(false)]
	public string OptionalCapabilities { get; set; }

	/// <summary>
	/// Use V100 plus version at the connect time.
	/// </summary>
	[Browsable(false)]
	public bool UseV100Plus { get; set; } = true;

	/// <summary>
	/// Max support version.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VersionKey,
		Description = LocalizedStrings.MaxSupportVersionKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public ServerVersions MaxVersion { get; set; } = ServerVersions.MinServerVerRfqFields;

	/// <summary>
	/// SSL protocol to establish connect.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProtocolKey,
		Description = LocalizedStrings.SslProtocolKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 201)]
	public SslProtocols SslProtocol { get; set; }

	/// <summary>
	/// SSL certificate.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CertificateKey,
		Description = LocalizedStrings.SslCertificateKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 202)]
	[Editor(typeof(IFileBrowserEditor), typeof(IFileBrowserEditor))]
	public string SslCertificate { get; set; }

	/// <summary>
	/// SSL certificate password.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.SslCertificatePasswordKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 203)]
	public SecureString SslCertificatePassword { get; set; }

	/// <summary>
	/// Check certificate revocation.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CheckCertificateRevocationKey,
		Description = LocalizedStrings.CheckCertificateRevocationDescKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 204)]
	public bool CheckCertificateRevocation { get; set; }

	/// <summary>
	/// Validate remove certificates.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ValidateRemoteCertificatesKey,
		Description = LocalizedStrings.ValidateRemoteCertificatesDescKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 205)]
	public bool ValidateRemoteCertificates { get; set; }

	/// <summary>
	/// The name of the server that shares SSL connection.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TargetHostKey,
		Description = LocalizedStrings.TargetHostDescKey,
		GroupName = LocalizedStrings.SslKey,
		Order = 206)]
	public string TargetHost { get; set; }

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Address = storage.GetValue<EndPoint>(nameof(Address));
		ClientId = storage.GetValue<int>(nameof(ClientId));
		MarketDataType = storage.GetValue<InteractiveBrokersMarketDataTypes>(nameof(MarketDataType));
		ServerLogLevel = storage.GetValue<ServerLogLevels>(nameof(ServerLogLevel));
		
		ExtraAuth = storage.GetValue<bool>(nameof(ExtraAuth));
		OptionalCapabilities = storage.GetValue<string>(nameof(OptionalCapabilities));
		UseV100Plus = storage.GetValue(nameof(UseV100Plus), UseV100Plus);
		MaxVersion = storage.GetValue(nameof(MaxVersion), MaxVersion);

		SslProtocol = storage.GetValue<SslProtocols>(nameof(SslProtocol));
		SslCertificate = storage.GetValue<string>(nameof(SslCertificate));
		SslCertificatePassword = storage.GetValue<SecureString>(nameof(SslCertificatePassword));
		CheckCertificateRevocation = storage.GetValue<bool>(nameof(CheckCertificateRevocation));
		ValidateRemoteCertificates = storage.GetValue<bool>(nameof(ValidateRemoteCertificates));
		TargetHost = storage.GetValue<string>(nameof(TargetHost));
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Address), Address.To<string>());
		storage.SetValue(nameof(ClientId), ClientId);
		storage.SetValue(nameof(MarketDataType), MarketDataType);
		storage.SetValue(nameof(ServerLogLevel), ServerLogLevel.To<string>());
		storage.SetValue(nameof(ExtraAuth), ExtraAuth);
		storage.SetValue(nameof(OptionalCapabilities), OptionalCapabilities);
		storage.SetValue(nameof(UseV100Plus), UseV100Plus);
		storage.SetValue(nameof(MaxVersion), MaxVersion);

		storage.SetValue(nameof(SslProtocol), SslProtocol);
		storage.SetValue(nameof(SslCertificate), SslCertificate);
		storage.SetValue(nameof(SslCertificatePassword), SslCertificatePassword);
		storage.SetValue(nameof(CheckCertificateRevocation), CheckCertificateRevocation);
		storage.SetValue(nameof(ValidateRemoteCertificates), ValidateRemoteCertificates);
		storage.SetValue(nameof(TargetHost), TargetHost);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + $": Addr={Address}";
	}
}