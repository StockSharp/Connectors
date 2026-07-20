namespace StockSharp.CowProtocol.Native.Model;

sealed class CowProtocolQuoteRequest
{
    [JsonProperty("sellToken")]
    public string SellToken { get; init; }
    [JsonProperty("buyToken")]
    public string BuyToken { get; init; }
    [JsonProperty("receiver")]
    public string Receiver { get; init; }
    [JsonProperty("from")]
    public string From { get; init; }
    [JsonProperty("kind")]
    public CowProtocolOrderKinds Kind { get; init; }
    [JsonProperty("sellAmountAfterFee")]
    public string SellAmountAfterFee { get; init; }
    [JsonProperty("buyAmountAfterFee")]
    public string BuyAmountAfterFee { get; init; }
    [JsonProperty("validFor")]
    public uint ValidFor { get; init; }
    [JsonProperty("appData")]
    public string AppData { get; init; }
    [JsonProperty("appDataHash")]
    public string AppDataHash { get; init; }
    [JsonProperty("sellTokenBalance")]
    public CowProtocolTokenBalances SellTokenBalance { get; init; }
    [JsonProperty("buyTokenBalance")]
    public CowProtocolTokenBalances BuyTokenBalance { get; init; }
    [JsonProperty("priceQuality")]
    public CowProtocolPriceQualities PriceQuality { get; init; }
    [JsonProperty("signingScheme")]
    public CowProtocolSigningSchemes SigningScheme { get; init; }
}

sealed class CowProtocolOrderParameters
{
    [JsonProperty("sellToken")]
    public string SellToken { get; init; }
    [JsonProperty("buyToken")]
    public string BuyToken { get; init; }
    [JsonProperty("receiver")]
    public string Receiver { get; init; }
    [JsonProperty("sellAmount")]
    public string SellAmount { get; init; }
    [JsonProperty("buyAmount")]
    public string BuyAmount { get; init; }
    [JsonProperty("validTo")]
    public uint ValidTo { get; init; }
    [JsonProperty("appData")]
    public string AppData { get; init; }
    [JsonProperty("appDataHash")]
    public string AppDataHash { get; init; }
    [JsonProperty("feeAmount")]
    public string FeeAmount { get; init; }
    [JsonProperty("gasAmount")]
    public string GasAmount { get; init; }
    [JsonProperty("gasPrice")]
    public string GasPrice { get; init; }
    [JsonProperty("sellTokenPrice")]
    public string SellTokenPrice { get; init; }
    [JsonProperty("kind")]
    public CowProtocolOrderKinds Kind { get; init; }
    [JsonProperty("partiallyFillable")]
    public bool IsPartiallyFillable { get; init; }
    [JsonProperty("sellTokenBalance")]
    public CowProtocolTokenBalances SellTokenBalance { get; init; }
    [JsonProperty("buyTokenBalance")]
    public CowProtocolTokenBalances BuyTokenBalance { get; init; }
    [JsonProperty("signingScheme")]
    public CowProtocolSigningSchemes SigningScheme { get; init; }
}

sealed class CowProtocolQuoteResponse
{
    [JsonProperty("quote")]
    public CowProtocolOrderParameters Quote { get; init; }
    [JsonProperty("from")]
    public string From { get; init; }
    [JsonProperty("expiration")]
    public string Expiration { get; init; }
    [JsonProperty("id")]
    public long? Id { get; init; }
    [JsonProperty("verified")]
    public bool IsVerified { get; init; }
    [JsonProperty("protocolFeeBps")]
    public string ProtocolFeeBps { get; init; }
}

sealed class CowProtocolOrderCreation
{
    [JsonProperty("sellToken")]
    public string SellToken { get; init; }
    [JsonProperty("buyToken")]
    public string BuyToken { get; init; }
    [JsonProperty("receiver")]
    public string Receiver { get; init; }
    [JsonProperty("sellAmount")]
    public string SellAmount { get; init; }
    [JsonProperty("buyAmount")]
    public string BuyAmount { get; init; }
    [JsonProperty("validTo")]
    public uint ValidTo { get; init; }
    [JsonProperty("appData")]
    public string AppData { get; init; }
    [JsonProperty("appDataHash")]
    public string AppDataHash { get; init; }
    [JsonProperty("feeAmount")]
    public string FeeAmount { get; init; }
    [JsonProperty("kind")]
    public CowProtocolOrderKinds Kind { get; init; }
    [JsonProperty("partiallyFillable")]
    public bool IsPartiallyFillable { get; init; }
    [JsonProperty("sellTokenBalance")]
    public CowProtocolTokenBalances SellTokenBalance { get; init; }
    [JsonProperty("buyTokenBalance")]
    public CowProtocolTokenBalances BuyTokenBalance { get; init; }
    [JsonProperty("signingScheme")]
    public CowProtocolSigningSchemes SigningScheme { get; init; }
    [JsonProperty("signature")]
    public string Signature { get; init; }
    [JsonProperty("from")]
    public string From { get; init; }
    [JsonProperty("quoteId")]
    public long? QuoteId { get; init; }
    [JsonProperty("fullBalanceCheck")]
    public bool IsFullBalanceCheck { get; init; }
}

sealed class CowProtocolOrder
{
    [JsonProperty("sellToken")]
    public string SellToken { get; init; }
    [JsonProperty("buyToken")]
    public string BuyToken { get; init; }
    [JsonProperty("receiver")]
    public string Receiver { get; init; }
    [JsonProperty("sellAmount")]
    public string SellAmount { get; init; }
    [JsonProperty("buyAmount")]
    public string BuyAmount { get; init; }
    [JsonProperty("validTo")]
    public uint ValidTo { get; init; }
    [JsonProperty("appData")]
    public string AppData { get; init; }
    [JsonProperty("appDataHash")]
    public string AppDataHash { get; init; }
    [JsonProperty("feeAmount")]
    public string FeeAmount { get; init; }
    [JsonProperty("kind")]
    public CowProtocolOrderKinds Kind { get; init; }
    [JsonProperty("partiallyFillable")]
    public bool IsPartiallyFillable { get; init; }
    [JsonProperty("sellTokenBalance")]
    public CowProtocolTokenBalances SellTokenBalance { get; init; }
    [JsonProperty("buyTokenBalance")]
    public CowProtocolTokenBalances BuyTokenBalance { get; init; }
    [JsonProperty("signingScheme")]
    public CowProtocolSigningSchemes SigningScheme { get; init; }
    [JsonProperty("signature")]
    public string Signature { get; init; }
    [JsonProperty("creationDate")]
    public string CreationDate { get; init; }
    [JsonProperty("class")]
    public string OrderClass { get; init; }
    [JsonProperty("owner")]
    public string Owner { get; init; }
    [JsonProperty("uid")]
    public string Uid { get; init; }
    [JsonProperty("executedSellAmount")]
    public string ExecutedSellAmount { get; init; }
    [JsonProperty("executedSellAmountBeforeFees")]
    public string ExecutedSellAmountBeforeFees { get; init; }
    [JsonProperty("executedBuyAmount")]
    public string ExecutedBuyAmount { get; init; }
    [JsonProperty("executedFeeAmount")]
    public string ExecutedFeeAmount { get; init; }
    [JsonProperty("executedFee")]
    public string ExecutedFee { get; init; }
    [JsonProperty("executedFeeToken")]
    public string ExecutedFeeToken { get; init; }
    [JsonProperty("invalidated")]
    public bool IsInvalidated { get; init; }
    [JsonProperty("status")]
    public CowProtocolOrderStatuses Status { get; init; }
    [JsonProperty("settlementContract")]
    public string SettlementContract { get; init; }
}

sealed class CowProtocolApiTrade
{
    [JsonProperty("blockNumber")]
    public long BlockNumber { get; init; }
    [JsonProperty("logIndex")]
    public int LogIndex { get; init; }
    [JsonProperty("orderUid")]
    public string OrderUid { get; init; }
    [JsonProperty("owner")]
    public string Owner { get; init; }
    [JsonProperty("sellToken")]
    public string SellToken { get; init; }
    [JsonProperty("buyToken")]
    public string BuyToken { get; init; }
    [JsonProperty("sellAmount")]
    public string SellAmount { get; init; }
    [JsonProperty("sellAmountBeforeFees")]
    public string SellAmountBeforeFees { get; init; }
    [JsonProperty("buyAmount")]
    public string BuyAmount { get; init; }
    [JsonProperty("txHash")]
    public string TransactionHash { get; init; }
}

sealed class CowProtocolCancellationRequest
{
    [JsonProperty("signature")]
    public string Signature { get; init; }
    [JsonProperty("signingScheme")]
    public CowProtocolSigningSchemes SigningScheme { get; init; }
}

sealed class CowProtocolApiError
{
    [JsonProperty("errorType")]
    public string ErrorType { get; init; }
    [JsonProperty("description")]
    public string Description { get; init; }
}
