namespace StockSharp.Swissquote.Native;

internal sealed class SwissquoteCustomerOverview
{
	[JsonProperty("customerIdentification")]
	public string CustomerIdentification { get; set; }

	[JsonProperty("accountOverview")]
	public SwissquoteAccountInformation[] AccountOverview { get; set; }
}

internal sealed class SwissquoteCustomerPositionsResponse
{
	[JsonProperty("statement")]
	public SwissquoteStatement Statement { get; set; }

	[JsonProperty("customer")]
	public SwissquoteCustomerAccounts Customer { get; set; }
}

internal sealed class SwissquoteCustomerAccounts
{
	[JsonProperty("customerIdentification")]
	public string CustomerIdentification { get; set; }

	[JsonProperty("accountList")]
	public SwissquoteAccount[] AccountList { get; set; }
}

internal sealed class SwissquoteAccount
{
	[JsonProperty("accountInformation")]
	public SwissquoteAccountInformation AccountInformation { get; set; }

	[JsonProperty("positionList")]
	public SwissquotePosition[] PositionList { get; set; }
}

internal sealed class SwissquoteAccountInformation
{
	[JsonProperty("accountIdentification")]
	public string AccountIdentification { get; set; }

	[JsonProperty("accountIdentificationType")]
	public string AccountIdentificationType { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("accountReferenceCurrency")]
	public string AccountReferenceCurrency { get; set; }
}

internal sealed class SwissquotePosition
{
	[JsonProperty("identification")]
	public string Identification { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("amountOrUnits")]
	public SwissquoteAmountOrUnits AmountOrUnits { get; set; }

	[JsonProperty("prices")]
	public SwissquotePositionPrice[] Prices { get; set; }

	[JsonProperty("financialInstrument")]
	public SwissquoteFinancialInstrument FinancialInstrument { get; set; }
}

internal sealed class SwissquoteAmountOrUnits
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("creditDebitIndicator")]
	public string CreditDebitIndicator { get; set; }
}

internal sealed class SwissquotePositionPrice
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("amountType")]
	public string AmountType { get; set; }

	[JsonProperty("creditDebitIndicator")]
	public string CreditDebitIndicator { get; set; }

	[JsonProperty("priceType")]
	public string PriceType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }
}

internal sealed class SwissquoteFinancialInstrument
{
	[JsonProperty("financialInstrumentIdentification")]
	public SwissquoteInstrumentIdentification FinancialInstrumentIdentification { get; set; }

	[JsonProperty("financialInstrumentName")]
	public string FinancialInstrumentName { get; set; }

	[JsonProperty("dates")]
	public SwissquoteInstrumentDate[] Dates { get; set; }

	[JsonProperty("optionDetails")]
	public SwissquoteInstrumentOptionDetails OptionDetails { get; set; }

	[JsonProperty("contractSize")]
	public string ContractSize { get; set; }

	[JsonProperty("financialInstrumentPrices")]
	public SwissquoteInstrumentPrice[] FinancialInstrumentPrices { get; set; }

	[JsonProperty("inactiveIndicator")]
	public bool IsInactive { get; set; }

	[JsonProperty("financialInstrumentAttributeAdditionalDetails")]
	public string AssetClass { get; set; }
}

internal sealed class SwissquoteInstrumentDate
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }
}

internal sealed class SwissquoteInstrumentOptionDetails
{
	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("optionStyle")]
	public string OptionStyle { get; set; }
}

internal sealed class SwissquoteInstrumentPrice
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

internal sealed class SwissquoteStatement
{
	[JsonProperty("statementIdentification")]
	public string StatementIdentification { get; set; }

	[JsonProperty("statementDateTime")]
	public string StatementDateTime { get; set; }
}

internal sealed class SwissquoteTradingCapacityResponse
{
	[JsonProperty("buyingPower")]
	public SwissquoteBuyingPower BuyingPower { get; set; }
}

internal sealed class SwissquoteBuyingPower
{
	[JsonProperty("totalBuyingPowerAmount")]
	public SwissquoteCurrencyAmount TotalBuyingPowerAmount { get; set; }
}

internal sealed class SwissquoteCurrencyAmount
{
	[JsonProperty("value")]
	public string Value { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("creditDebitIndicator")]
	public string CreditDebitIndicator { get; set; }
}

internal sealed class SwissquoteTransactionsResponse
{
	[JsonProperty("statement")]
	public SwissquoteStatement Statement { get; set; }

	[JsonProperty("transactions")]
	public SwissquoteTransaction[] Transactions { get; set; }
}

internal sealed class SwissquoteTransaction
{
	[JsonProperty("customerIdentification")]
	public string CustomerIdentification { get; set; }

	[JsonProperty("orderIdentification")]
	public string OrderIdentification { get; set; }

	[JsonProperty("transactionIdentification")]
	public string TransactionIdentification { get; set; }

	[JsonProperty("placeOfTrade")]
	public SwissquotePlaceOfTrade PlaceOfTrade { get; set; }

	[JsonProperty("reversalIndicator")]
	public bool IsReversal { get; set; }

	[JsonProperty("dateList")]
	public SwissquoteTransactionDate[] DateList { get; set; }

	[JsonProperty("dateTimeList")]
	public SwissquoteTransactionDateTime[] DateTimeList { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("transactionSubtype")]
	public string TransactionSubtype { get; set; }

	[JsonProperty("triggeringFinancialInstrument")]
	public SwissquoteInstrumentIdentification TriggeringFinancialInstrument { get; set; }

	[JsonProperty("movementList")]
	public SwissquoteMovement[] MovementList { get; set; }

	[JsonProperty("prices")]
	public SwissquoteTransactionPrice[] Prices { get; set; }
}

internal sealed class SwissquoteTransactionDate
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("dateType")]
	public string DateType { get; set; }
}

internal sealed class SwissquoteTransactionDateTime
{
	[JsonProperty("dateTime")]
	public string DateTime { get; set; }

	[JsonProperty("dateType")]
	public string DateType { get; set; }
}

internal sealed class SwissquoteMovement
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("amountType")]
	public string AmountType { get; set; }

	[JsonProperty("creditDebitIndicator")]
	public string CreditDebitIndicator { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("movementType")]
	public string MovementType { get; set; }

	[JsonProperty("financialInstrument")]
	public SwissquoteInstrumentIdentification FinancialInstrument { get; set; }

	[JsonProperty("positionIdentification")]
	public string PositionIdentification { get; set; }

	[JsonProperty("accountDetails")]
	public SwissquoteAccountInformation AccountDetails { get; set; }
}

internal sealed class SwissquoteTransactionPrice
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("amountType")]
	public string AmountType { get; set; }

	[JsonProperty("priceType")]
	public string PriceType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}
