namespace StockSharp.Hyperliquid.Native;

readonly struct L1Signature(string r, string s, int v)
{
	public string R { get; } = r;
	public string S { get; } = s;
	public int V { get; } = v;
}
