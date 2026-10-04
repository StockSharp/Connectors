namespace StockSharp.CSV;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;
using Ecng.Serialization;

/// <summary>
/// The message adapter for <see cref="CSV"/>.
/// </summary>
[MediaIcon(Media.MediaNames.csv)]
[Doc("topics/api/connectors/common/csv.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CSVKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.History | MessageAdapterCategories.Free |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.MarketDepth)]
public partial class CSVMessageAdapter
{
	private IEnumerable<ImportSettings> _settings = [];

	/// <summary>
	/// Settings of import.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SettingsKey,
		Description = LocalizedStrings.ImportSettingsKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public IEnumerable<ImportSettings> Settings
	{
		get => _settings;
		set
		{
			_settings = value ?? throw new ArgumentNullException(nameof(value));

			PossibleSupportedMessages = [];

			if (SecurityImportSettings != null)
				this.AddSupportedMessage(MessageTypes.SecurityLookup, true);

			if (AsyncHelper.Run(() => GetSupportedMarketDataTypesAsync(default, default, default).AnyAsync()))
				this.AddSupportedMessage(MessageTypes.MarketData, true);

			if (PortfolioImportSettings != null)
				this.AddSupportedMessage(MessageTypes.PortfolioLookup, false);

			if (TransactionImportSettings != null)
				this.AddSupportedMessage(MessageTypes.OrderStatus, false);
		}
	}

	/// <inheritdoc />
	public override async Task LoadAsync(SettingsStorage storage, CancellationToken cancellationToken)
	{
		await base.LoadAsync(storage, cancellationToken);

		Settings = await storage.GetValue<SettingsStorage[]>(nameof(Settings)).LoadAllAsync<ImportSettings>(cancellationToken);
	}

	/// <inheritdoc />
	public override async Task SaveAsync(SettingsStorage storage, CancellationToken cancellationToken)
	{
		await base.SaveAsync(storage, cancellationToken);

		storage.SetValue(nameof(Settings), await Settings.SaveAllAsync(cancellationToken));
	}
}