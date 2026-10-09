namespace StockSharp.Binance.Native;

/// <summary>
/// Says whether a depth update continues the book built so far.
/// </summary>
/// <remarks>
/// Binance numbers every change of a book. A spot update names the first and the last change it carries, and the
/// next one starts where it ended. A futures update also names the last change of the update before it. A
/// snapshot names the last change it holds, and is taken while updates flow, so the first update applied to it
/// is the one it was taken in the middle of.
/// </remarks>
/// <param name="isFutures">Whether updates are numbered the futures way.</param>
sealed class DepthSequence(bool isFutures)
{
	private bool _isFirst;

	/// <summary>
	/// The last change the book holds.
	/// </summary>
	public long LastUpdateId { get; private set; }

	/// <summary>
	/// Starts the book from a snapshot.
	/// </summary>
	/// <param name="snapshotUpdateId">The last change the snapshot holds.</param>
	public void Reset(long snapshotUpdateId)
	{
		LastUpdateId = snapshotUpdateId;
		_isFirst = true;
	}

	/// <summary>
	/// Takes an update into the book if it continues it.
	/// </summary>
	/// <param name="firstUpdateId">The first change the update carries.</param>
	/// <param name="lastUpdateId">The last change the update carries.</param>
	/// <param name="prevLastUpdateId">The last change of the update before it, where the stream names one.</param>
	/// <returns>
	/// <see langword="true"/> to apply the update; <see langword="null"/> to drop one the book already holds;
	/// <see langword="false"/> when changes between the book and the update never came, so the book is to be
	/// built anew from a snapshot.
	/// </returns>
	public bool? TryAdvance(long firstUpdateId, long lastUpdateId, long? prevLastUpdateId)
	{
		bool? step;

		if (isFutures && prevLastUpdateId is long prev)
		{
			if (prev == LastUpdateId)
				step = true;
			else if (_isFirst)
				step = lastUpdateId < LastUpdateId ? null : firstUpdateId <= LastUpdateId;
			else
				step = prev > LastUpdateId ? false : null;
		}
		else
			step = lastUpdateId <= LastUpdateId ? null : firstUpdateId <= LastUpdateId + 1;

		if (step != true)
			return step;

		LastUpdateId = lastUpdateId;
		_isFirst = false;

		return true;
	}
}
