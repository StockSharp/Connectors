namespace StockSharp.Gleif;

/// <summary>Message adapter for the public GLEIF API.</summary>
[MediaIcon(Media.MediaNames.gleif)]
[Doc("topics/api/connectors/stock_market/gleif.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.GleifKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Stock)]
public partial class GleifMessageAdapter :
    MessageAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Official GLEIF production API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.gleif.org/api/v1/");

    /// <summary>Whether inactive legal entities are excluded.</summary>
    [Display(
        Name = "Active entities only",
        Description = "Request only legal entities whose GLEIF entity status is ACTIVE.",
        GroupName = "Filters",
        Order = 1)]
    public bool ActiveOnly { get; set; } = true;

    /// <summary>Whether LEI results are expanded into mapped ISINs.</summary>
    [Display(
        Name = "Expand mapped ISINs",
        Description = "Return mapped ISIN securities in addition to the legal-entity LEI record.",
        GroupName = "Filters",
        Order = 2)]
    public bool ExpandIsins { get; set; }

    /// <summary>JSON:API page size.</summary>
    [Display(
        Name = "Page size",
        Description = "Number of GLEIF records requested per page.",
        GroupName = "Limits",
        Order = 3)]
    public int PageSize { get; set; } = 50;

    /// <summary>Maximum pages per lookup.</summary>
    [Display(
        Name = "Maximum pages",
        Description = "Maximum pages downloaded by one LEI or ISIN lookup.",
        GroupName = "Limits",
        Order = 4)]
    public int MaxPages { get; set; } = 10;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Address), Address)
            .Set(nameof(ActiveOnly), ActiveOnly)
            .Set(nameof(ExpandIsins), ExpandIsins)
            .Set(nameof(PageSize), PageSize)
            .Set(nameof(MaxPages), MaxPages);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Address = storage.GetValue(nameof(Address), Address);
        ActiveOnly = storage.GetValue(nameof(ActiveOnly), ActiveOnly);
        ExpandIsins = storage.GetValue(nameof(ExpandIsins), ExpandIsins);
        PageSize = storage.GetValue(nameof(PageSize), PageSize);
        MaxPages = storage.GetValue(nameof(MaxPages), MaxPages);
    }
}
