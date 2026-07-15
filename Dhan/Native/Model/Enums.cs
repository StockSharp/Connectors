namespace StockSharp.Dhan.Native.Model;

enum DhanExchangeSegments : byte
{
	Index = 0,
	NseEquity = 1,
	NseDerivatives = 2,
	NseCurrency = 3,
	BseEquity = 4,
	McxCommodity = 5,
	BseCurrency = 7,
	BseDerivatives = 8,
}

enum DhanFeedModes
{
	Quote = 17,
	Full = 21,
}
