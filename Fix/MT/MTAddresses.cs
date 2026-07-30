namespace StockSharp.Fix.MT;

using System.Net;

static class MTAddresses
{
	public static readonly EndPoint MT4 = "127.0.0.1:23000".To<EndPoint>();
	public static readonly EndPoint MT5 = "127.0.0.1:23001".To<EndPoint>();
}
