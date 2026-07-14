namespace StockSharp.Alor.Native.Model;

interface ISymbolObject
{
	string Symbol { get; set; }
	string BrokerSymbol { get; set; }
	string Exchange { get; set; }
}
