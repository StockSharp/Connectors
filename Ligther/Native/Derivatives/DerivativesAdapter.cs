namespace StockSharp.Ligther.Native.Derivatives;

using StockSharp.Ligther.Native.Common;

sealed class DerivativesAdapter : LigtherSectionAdapter
{
	public DerivativesAdapter(LigtherMessageAdapter owner)
		: base(owner, BoardCodes.LigtherDerivatives)
	{
	}

	public override LigtherSections Section => LigtherSections.Derivatives;
	protected override string SectionName => "Derivatives";
	protected override bool IsSpotSection => false;
	protected override SecurityTypes SectionSecurityType => SecurityTypes.Future;
}
