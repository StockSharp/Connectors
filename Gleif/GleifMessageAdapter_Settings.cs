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
        Description = LocalizedStrings.OfficialGleifProductionApiRootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.gleif.org/api/v1/");

    /// <summary>Whether inactive legal entities are excluded.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ActiveEntitiesOnlyKey,
        Description = LocalizedStrings.RequestOnlyLegalEntitiesWhoseGleifEntityStatusIsActiveDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 1)]
    public bool ActiveOnly { get; set; } = true;

    /// <summary>Whether LEI results are expanded into mapped ISINs.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ExpandMappedISINsKey,
        Description = LocalizedStrings.ReturnMappedIsinSecuritiesInAdditionToTheLegalEntityLeiRecordDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 2)]
    public bool ExpandIsins { get; set; }

    /// <summary>JSON:API page size.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageSizeKey,
        Description = LocalizedStrings.NumberOfGleifRecordsRequestedPerPageDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 3)]
    public int PageSize { get; set; } = 50;

    /// <summary>Maximum pages per lookup.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumPagesKey,
        Description = LocalizedStrings.MaximumPagesDownloadedByOneLeiOrIsinLookupDescKey,
        GroupName = LocalizedStrings.LimitsKey,
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
