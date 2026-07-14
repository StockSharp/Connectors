namespace StockSharp.Rss;

using System.ComponentModel.DataAnnotations;

using Ecng.Common;
using Ecng.ComponentModel;
using Ecng.Serialization;

using StockSharp.Localization;
using StockSharp.Messages;

/// <summary>
/// The market-data message adapter for RSS.
/// </summary>
[MediaIcon(Media.MediaNames.rss)]
[Doc("topics/api/connectors/common/rss.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = _rss,
	Description = LocalizedStrings.RssAdapterKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.News)]
partial class RssMessageAdapter : HistoricalMessageAdapter, IAddressAdapter<string>
{
	private const string _rss = "Rss";

	private string _address = RssAddresses.Reuters;

	/// <summary>
	/// RSS feed address.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.RssAddressKey,
		GroupName = _rss,
		Order = 0)]
	[ItemsSource(typeof(RssAddressesSource), IsEditable = true)]
	[BasicSetting]
	public string Address
	{
		get => _address;
		set => _address = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>
	/// Dates format. Required to be filled if RSS stream format is different from ddd, dd MMM yyyy HH:mm:ss zzzz.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DatesFormatKey,
		Description = LocalizedStrings.DatesFormatDescKey,
		GroupName = _rss,
		Order = 1)]
	public string CustomDateFormat { get; set; }

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Address = storage.GetValue<string>(nameof(Address));
		CustomDateFormat = storage.GetValue<string>(nameof(CustomDateFormat));
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Address), Address);
		storage.SetValue(nameof(CustomDateFormat), CustomDateFormat);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + Address.To<string>();
	}
}