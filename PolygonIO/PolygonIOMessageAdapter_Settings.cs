namespace StockSharp.PolygonIO;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

using Ecng.ComponentModel;

/// <summary>
/// Possible connection types.
/// </summary>
[DataContract]
[Serializable]
public enum PolygonIOConnectionTypes
{
	/// <summary>
	/// History.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HistoryKey)]
	History,

	/// <summary>
	/// Delayed.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DelayKey)]
	Delayed,

	/// <summary>
	/// Real-time.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RealTimeKey,
		Description = LocalizedStrings.RealTimeDataKey)]
	RealTime,
}

/// <summary>
/// The message adapter for <see cref="PolygonIO"/>.
/// </summary>
[MediaIcon(Media.MediaNames.polygonio)]
[Doc("topics/api/connectors/stock_market/polygonio.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PolygonIOKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.History | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX | MessageAdapterCategories.Candles | MessageAdapterCategories.Free)]
public partial class PolygonIOMessageAdapter : MessageAdapter, ITokenAdapter, IKeySecretAdapter, IAddressAdapter<string>
{
	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames { get; } =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(60),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromTicks(TimeHelper.TicksPerMonth),
		TimeSpan.FromTicks(TimeHelper.TicksPerMonth * 3),
		TimeSpan.FromTicks(TimeHelper.TicksPerYear),
	];

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	private string _address = "massive.com";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string Address
	{
		get => _address;
		set => _address = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>
	/// Polygon flat files (S3) bucket name.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StorageKey,
		Description = LocalizedStrings.StorageKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.FlatFilesKey,
		Order = 42)]
	public string FlatFilesRepo { get; set; } = "flatfiles";

	/// <summary>
	/// AWS access key id for Polygon flat files (S3).
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.FlatFilesKey,
		Order = 43)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>
	/// AWS secret access key for Polygon flat files (S3).
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.FlatFilesKey,
		Order = 44)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	private PolygonIOConnectionTypes _connectionType = PolygonIOConnectionTypes.RealTime;

	/// <summary>
	/// Connection type.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ConnectionTypeKey,
		Description = LocalizedStrings.ConnectionTypeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public PolygonIOConnectionTypes ConnectionType
	{
		get => _connectionType;
		set
		{
			_connectionType = value;
			OnPropertyChanged(nameof(FeatureName));
		}
	}

	private static readonly HashSet<SecurityTypes> _possibleFlatFilesSections = new(
	[
		SecurityTypes.Stock,
		SecurityTypes.Index,
		SecurityTypes.CryptoCurrency,
		SecurityTypes.Currency,
		SecurityTypes.Future
	]);

	private class FlatFilesSectionsSource : ItemsSourceBase<SecurityTypes>
	{
		public FlatFilesSectionsSource()
			: base(_possibleFlatFilesSections)
		{
		}
	}

	private IEnumerable<SecurityTypes> _flatFilesSections = [];

	/// <summary>
	/// Instrument sections (security types) for Polygon flat-files. If empty, flat-files mode is off.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FlatFilesKey,
		Description = LocalizedStrings.FlatFilesDescKey,
		GroupName = LocalizedStrings.FlatFilesKey,
		Order = 11)]
	[ItemsSource(typeof(FlatFilesSectionsSource))]
	[BasicSetting]
	public IEnumerable<SecurityTypes> FlatFilesSections
	{
		get => _flatFilesSections;
		set
		{
			if (value is null)
				throw new ArgumentNullException(nameof(value));

			var arr = value.ToArray();
			var unsupported = arr.Where(v => !_possibleFlatFilesSections.Contains(v)).Distinct().ToArray();

			if (unsupported.Length > 0)
				throw new ArgumentOutOfRangeException(nameof(value), unsupported.Select(t => t.To<string>()).JoinComma(), LocalizedStrings.InvalidValue);

			_flatFilesSections = arr;
		}
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(FlatFilesRepo), FlatFilesRepo)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ConnectionType), ConnectionType)
			.Set(nameof(FlatFilesSections), FlatFilesSections)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Token = storage.GetValue<SecureString>(nameof(Token));
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Address = storage.GetValue(nameof(Address), Address);
		FlatFilesRepo = storage.GetValue(nameof(FlatFilesRepo), FlatFilesRepo);
		ConnectionType = storage.GetValue(nameof(ConnectionType), ConnectionType);
		FlatFilesSections = storage.GetValue<IEnumerable<SecurityTypes>>(nameof(FlatFilesSections)) ?? FlatFilesSections;
	}
}