namespace StockSharp.Bitget.Native;

class BookInfo<TOrderBook>
{
	public readonly Lock Sync = new();
	public bool IsRestoring;
	public readonly List<(long firstId, long lastId, TOrderBook book)> Increments = [];

	public int Depth { get; }

	public BookInfo(int depth)
	{
		if (depth <= 0)
			throw new ArgumentOutOfRangeException(nameof(depth), depth, LocalizedStrings.InvalidValue);

		Depth = depth;
	}

	public void AddIncrement(long firstId, long lastId, TOrderBook book)
	{
		if (Increments.Count > 1000)
			return;

		Increments.Add((firstId, lastId, book));
	}

	public void FinishRestore()
	{
		IsRestoring = false;
		Increments.Clear();
	}
}