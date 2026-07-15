namespace StockSharp.AngelOne.Native.Model;

enum AngelOneExchangeTypes : byte
{
	NseCash = 1,
	NseDerivatives = 2,
	BseCash = 3,
	BseDerivatives = 4,
	McxDerivatives = 5,
	NcdexDerivatives = 7,
	CurrencyDerivatives = 13,
}

enum AngelOneFeedModes : byte
{
	Ltp = 1,
	Quote = 2,
	SnapQuote = 3,
}
