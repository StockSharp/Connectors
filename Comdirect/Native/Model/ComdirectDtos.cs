namespace StockSharp.Comdirect.Native.Model;

sealed class ComdirectToken
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("token_type")]
    public string TokenType { get; set; }

    [JsonProperty("refresh_token")]
    public string RefreshToken { get; set; }

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }

    public string Scope { get; set; }
    public string Kdnr { get; set; }
    public string Bpid { get; set; }
    public string KontaktId { get; set; }
}

sealed class ComdirectSession
{
    public string Identifier { get; set; }
    public bool SessionTanActive { get; set; }
    public bool Activated2FA { get; set; }
}

sealed class ComdirectAuthenticationInfo
{
    public string Id { get; set; }
    public string Typ { get; set; }
    public string Challenge { get; set; }
    public string[] AvailableTypes { get; set; }
}

class ComdirectPage<T>
{
    public ComdirectPaging Paging { get; set; }
    public T[] Values { get; set; }
}

sealed class ComdirectPaging
{
    public int Index { get; set; }
    public int Matches { get; set; }
}

sealed class ComdirectAmount
{
    public string Value { get; set; }
    public string Unit { get; set; }
}

sealed class ComdirectEnumText
{
    public string Key { get; set; }
    public string Text { get; set; }
}

sealed class ComdirectDepot
{
    public string DepotId { get; set; }
    public string DepotDisplayId { get; set; }
    public string ClientId { get; set; }
    public string DefaultSettlementAccountId { get; set; }
    public string[] SettlementAccountIds { get; set; }
}

sealed class ComdirectAccount
{
    public string AccountId { get; set; }
    public string AccountDisplayId { get; set; }
    public string Currency { get; set; }
    public string ClientId { get; set; }
    public ComdirectEnumText AccountType { get; set; }
    public string Iban { get; set; }
}

sealed class ComdirectAccountBalance
{
    public ComdirectAccount Account { get; set; }
    public string AccountId { get; set; }
    public ComdirectAmount Balance { get; set; }
    public ComdirectAmount BalanceEUR { get; set; }
    public ComdirectAmount AvailableCashAmount { get; set; }
    public ComdirectAmount AvailableCashAmountEUR { get; set; }
}

sealed class ComdirectPositionPage : ComdirectPage<ComdirectPosition>
{
    public ComdirectDepotAggregation Aggregated { get; set; }
}

sealed class ComdirectDepotAggregation
{
    public ComdirectAmount DepotValue { get; set; }
    public ComdirectAmount PrevDayDepotValue { get; set; }
}

sealed class ComdirectPosition
{
    public string DepotId { get; set; }
    public string PositionId { get; set; }
    public string Wkn { get; set; }
    public string CustodyType { get; set; }
    public ComdirectAmount Quantity { get; set; }
    public ComdirectAmount AvailableQuantity { get; set; }
    public ComdirectPrice CurrentPrice { get; set; }
    public ComdirectAmount PurchasePrice { get; set; }
    public ComdirectPrice PrevDayPrice { get; set; }
    public ComdirectAmount CurrentValue { get; set; }
    public ComdirectAmount PurchaseValue { get; set; }
    public ComdirectAmount ProfitLossPurchaseAbs { get; set; }
    public string ProfitLossPurchaseRel { get; set; }
    public ComdirectAmount ProfitLossPrevDayAbs { get; set; }
    public string ProfitLossPrevDayRel { get; set; }
    public ComdirectInstrument Instrument { get; set; }
}

sealed class ComdirectPrice
{
    public ComdirectAmount Price { get; set; }
    public string Type { get; set; }
    public ComdirectAmount Quantity { get; set; }
    public string PriceDateTime { get; set; }
}

sealed class ComdirectInstrument
{
    public string InstrumentId { get; set; }
    public string Wkn { get; set; }
    public string Mnemonic { get; set; }
    public string Isin { get; set; }
    public string Name { get; set; }
    public string ShortName { get; set; }
    public ComdirectStaticData StaticData { get; set; }
    public ComdirectDimensions OrderDimensions { get; set; }
    public ComdirectDerivativeData DerivativeData { get; set; }
}

sealed class ComdirectStaticData
{
    public string Notation { get; set; }
    public string Currency { get; set; }
    public string InstrumentType { get; set; }
    public bool PriipsRelevant { get; set; }
    public bool KidAvailable { get; set; }
    public bool ShippingWaiverRequired { get; set; }
    public bool FundRedemptionLimited { get; set; }
}

sealed class ComdirectDerivativeData
{
    public ComdirectInstrument UnderlyingInstrument { get; set; }
    public ComdirectPrice UnderlyingPrice { get; set; }
    public string CertificateType { get; set; }
    public ComdirectAmount StrikePrice { get; set; }
    public string Leverage { get; set; }
    public string Multiplier { get; set; }
    public string ExpiryDate { get; set; }
    public string WarrantType { get; set; }
    public string MaturityDate { get; set; }
}

sealed class ComdirectDimensions
{
    public ComdirectVenue[] Venues { get; set; }
}

sealed class ComdirectVenue
{
    public string VenueId { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public string Country { get; set; }
    public string[] Currencies { get; set; }
    public string[] Sides { get; set; }
    public string[] ValidityTypes { get; set; }
    public IDictionary<string, ComdirectOrderTypeDimension> OrderTypes
    { get; set; }
}

sealed class ComdirectOrderTypeDimension
{
    public string Name { get; set; }
    public string[] LimitExtensions { get; set; }
    public string[] TradingRestrictions { get; set; }
}

sealed class ComdirectOrder
{
    public string DepotId { get; set; }
    public string SettlementAccountId { get; set; }
    public string OrderId { get; set; }
    public string CreationTimestamp { get; set; }
    public int? LegNumber { get; set; }
    public bool? BestEx { get; set; }
    public string OrderType { get; set; }
    public string OrderStatus { get; set; }
    public ComdirectOrder[] SubOrders { get; set; }
    public string Side { get; set; }
    public string InstrumentId { get; set; }
    public string QuoteTicketId { get; set; }
    public string QuoteId { get; set; }
    public string VenueId { get; set; }
    public ComdirectAmount Quantity { get; set; }
    public string LimitExtension { get; set; }
    public string TradingRestriction { get; set; }
    public ComdirectAmount Limit { get; set; }
    public ComdirectAmount TriggerLimit { get; set; }
    public string TrailingLimitDistAbs { get; set; }
    public string TrailingLimitDistRel { get; set; }
    public string ValidityType { get; set; }
    public string Validity { get; set; }
    public ComdirectAmount OpenQuantity { get; set; }
    public ComdirectAmount CancelledQuantity { get; set; }
    public ComdirectAmount ExecutedQuantity { get; set; }
    public ComdirectAmount ExpectedValue { get; set; }
    public ComdirectExecution[] Executions { get; set; }
    public ComdirectInstrument Instrument { get; set; }
}

sealed class ComdirectExecution
{
    public string ExecutionId { get; set; }
    public int ExecutionNumber { get; set; }
    public ComdirectAmount ExecutedQuantity { get; set; }
    public ComdirectAmount ExecutionPrice { get; set; }
    public string ExecutionTimestamp { get; set; }
}

sealed class ComdirectErrorEnvelope
{
    public ComdirectErrorMessage[] Messages { get; set; }
    public string Error { get; set; }

    [JsonProperty("error_description")]
    public string ErrorDescription { get; set; }
}

sealed class ComdirectErrorMessage
{
    public string Severity { get; set; }
    public string Key { get; set; }
    public string Message { get; set; }
    public IDictionary<string, string> Args { get; set; }
    public string[] Origin { get; set; }
}
