namespace StockSharp.InteractiveBrokers;

/// <summary>
/// The filter result of scanner starting via <see cref="ScannerMarketDataMessage"/>.
/// </summary>
public class ScannerResult : Cloneable<ScannerResult>, IAsyncPersistable
{
	/// <summary>
	/// Security ID.
	/// </summary>
	public SecurityId SecurityId { get; set; }

	/// <summary>
	/// Rank.
	/// </summary>
	public int Rank { get; set; }

	/// <summary>
	/// Distance.
	/// </summary>
	public string Distance { get; set; }

	/// <summary>
	/// Benchmark.
	/// </summary>
	public string Benchmark { get; set; }

	/// <summary>
	/// Projection.
	/// </summary>
	public string Projection { get; set; }

	/// <summary>
	/// The combined instrument description.
	/// </summary>
	public string Legs { get; set; }

	/// <summary>
	/// Create a copy of <see cref="ScannerResult"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override ScannerResult Clone() => (ScannerResult)MemberwiseClone();

	Task IAsyncPersistable.LoadAsync(SettingsStorage storage, CancellationToken cancellationToken)
	{
		if (storage.ContainsKey(nameof(SecurityId)))
			SecurityId = storage.GetValue<string>(nameof(SecurityId)).ToSecurityId();

		Rank = storage.GetValue<int>(nameof(Rank));
		Distance = storage.GetValue<string>(nameof(Distance));
		Benchmark = storage.GetValue<string>(nameof(Benchmark));
		Projection = storage.GetValue<string>(nameof(Projection));
		Legs = storage.GetValue<string>(nameof(Legs));

		return Task.CompletedTask;
	}

	Task IAsyncPersistable.SaveAsync(SettingsStorage storage, CancellationToken cancellationToken)
	{
		if (SecurityId != default)
			storage.SetValue(nameof(SecurityId), SecurityId.ToStringId());

		storage.SetValue(nameof(Rank), Rank);
		storage.SetValue(nameof(Distance), Distance);
		storage.SetValue(nameof(Benchmark), Benchmark);
		storage.SetValue(nameof(Projection), Projection);
		storage.SetValue(nameof(Legs), Legs);

		return Task.CompletedTask;
	}
}