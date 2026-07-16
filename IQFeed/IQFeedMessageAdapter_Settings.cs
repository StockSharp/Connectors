namespace StockSharp.IQFeed;

using System.Security;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;
using Ecng.Serialization;

/// <summary>
/// The messages adapter for IQFeed.
/// </summary>
[MediaIcon(Media.MediaNames.iqfeed)]
[Doc("topics/api/connectors/stock_market/iqfeed.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.IQFeedKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.OrderLog |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.Stock | MessageAdapterCategories.FX | MessageAdapterCategories.News)]
partial class IQFeedMessageAdapter : ILoginPasswordAdapter
{
	private const string _defaultProductId = "STOCKSHARP_47086";

	/// <summary>
	/// Product id.
	/// </summary>
	[Browsable(false)]
	public string ProductId { get; set; } = _defaultProductId;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>
	/// Address for obtaining data on Level1.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.Level1ServerKey,
		Description = LocalizedStrings.Level1ServerDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public EndPoint Level1Address { get; set; } = IQFeedAddresses.DefaultLevel1Address;

	/// <summary>
	/// Address for obtaining data on Level2.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.Level2ServerKey,
		Description = LocalizedStrings.Level2ServerDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public EndPoint Level2Address { get; set; } = IQFeedAddresses.DefaultLevel2Address;

	/// <summary>
	/// Address for obtaining history data.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LookupServerKey,
		Description = LocalizedStrings.LookupServerDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public EndPoint LookupAddress { get; set; } = IQFeedAddresses.DefaultLookupAddress;

	/// <summary>
	/// Address for obtaining service data.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AdminServerKey,
		Description = LocalizedStrings.AdminServerDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public EndPoint AdminAddress { get; set; } = IQFeedAddresses.DefaultAdminAddress;

	/// <summary>
	/// Address for obtaining derivative data.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DerivativesKey,
		Description = LocalizedStrings.DerivativesServerDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public EndPoint DerivativeAddress { get; set; } = IQFeedAddresses.DefaultDerivativeAddress;

	private class IQFeedLevel1ColumnSource : ItemsSourceBase<IQFeedLevel1Column>
	{
		protected override IEnumerable<IQFeedLevel1Column> GetValues() => IQFeedLevel1ColumnRegistry.Instance.AllColumns;
	}

	private IQFeedLevel1Column[] _level1Columns;

	/// <summary>
	/// All <see cref="IQFeedLevel1Column"/> to be transmit.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.Level1FieldsKey,
		Description = LocalizedStrings.Level1FieldsDescKey,
		GroupName = LocalizedStrings.AdditionalKey,
		Order = 0)]
	[ItemsSource(typeof(IQFeedLevel1ColumnSource))]
	public IEnumerable<IQFeedLevel1Column> Level1Columns
	{
		get => _level1Columns;
		set => _level1Columns = [.. (value ?? throw new ArgumentNullException(nameof(value)))
			.Where(column => !column.IsMandatory)];
	}

	/// <summary>
	/// Whether to load instruments from the archive of the IQFeed site. The default is off.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DownloadSecuritiesKey,
		Description = LocalizedStrings.DownloadSecuritiesDescKey,
		GroupName = LocalizedStrings.AdditionalKey,
		Order = 2)]
	public bool IsDownloadSecurityFromSite { get; set; }

	/// <summary>
	/// Path to file with IQFeed list of securities, downloaded from the website. If path is specified, then secondary download from website does not occur, and only the local copy gets parsed.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FileWithSecsKey,
		Description = LocalizedStrings.FileWithSecsDescKey,
		GroupName = LocalizedStrings.AdditionalKey,
		Order = 3)]
	public string SecuritiesFile { get; set; }

	/// <summary>Whether to connect only to locally available symbol data.</summary>
	[Browsable(false)]
	public bool IsOffline { get; set; }

	/// <summary>
	/// Default value for <see cref="Version"/>.
	/// </summary>
	public static readonly Version DefaultVersion = new(6, 2);

	private Version _version = DefaultVersion;

	/// <summary>
	/// Protocol version.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VersionKey,
		Description = LocalizedStrings.VersionKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.AdditionalKey,
		Order = 4)]
	public Version Version
	{
		get => _version;
		set
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));

			if (value.Major != 6 || value.Minor != 2)
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);

			_version = value;
		}
	}

	/// <summary>
	/// The list of all available <see cref="IQFeedLevel1Column"/>.
	/// </summary>
	[Browsable(false)]
	public IQFeedLevel1ColumnRegistry Level1ColumnRegistry { get; }

	private static readonly HashSet<TimeSpan> _timeFrames = new(
    [
        TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromTicks(TimeHelper.TicksPerMonth)
	]);

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		ProductId = storage.GetValue<string>(nameof(ProductId));
		if(ProductId.IsEmptyOrWhiteSpace())
			ProductId = _defaultProductId;

		Login = storage.GetValue<string>(nameof(Login)) ?? storage.GetValue<string>("Username");
		Password = storage.GetValue<SecureString>(nameof(Password));
		Level1Address = storage.GetValue(nameof(Level1Address), Level1Address);
		Level2Address = storage.GetValue(nameof(Level2Address), Level2Address);
		LookupAddress = storage.GetValue(nameof(LookupAddress), LookupAddress);
		AdminAddress = storage.GetValue(nameof(AdminAddress), AdminAddress);
		DerivativeAddress = storage.GetValue(nameof(DerivativeAddress), DerivativeAddress);
		Version = storage.GetValue(nameof(Version), Version);

		IsDownloadSecurityFromSite = storage.GetValue<bool>(nameof(IsDownloadSecurityFromSite));
		SecuritiesFile = storage.GetValue<string>(nameof(SecuritiesFile));
		IsOffline = storage.GetValue(nameof(IsOffline), IsOffline);

		if (storage.ContainsKey(nameof(Level1Columns)))
		{
			Level1Columns = [.. storage
				.GetValue<string>(nameof(Level1Columns))
				.SplitByComma()
				.Select(name => Level1ColumnRegistry[name])];
		}
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(ProductId), ProductId);
		storage.SetValue(nameof(Login), Login);
		storage.SetValue(nameof(Password), Password);
		storage.SetValue(nameof(Level1Address), Level1Address.To<string>());
		storage.SetValue(nameof(Level2Address), Level2Address.To<string>());
		storage.SetValue(nameof(LookupAddress), LookupAddress.To<string>());
		storage.SetValue(nameof(AdminAddress), AdminAddress.To<string>());
		storage.SetValue(nameof(DerivativeAddress), DerivativeAddress.To<string>());
		storage.SetValue(nameof(Version), Version.To<string>());

		storage.SetValue(nameof(IsDownloadSecurityFromSite), IsDownloadSecurityFromSite);
		storage.SetValue(nameof(SecuritiesFile), SecuritiesFile);
		storage.SetValue(nameof(IsOffline), IsOffline);

		storage.SetValue(nameof(Level1Columns), Level1Columns.Select(c => c.Name).JoinComma());
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + $"Level1='{Level1Address}' Level2='{Level2Address}'";
	}
}
