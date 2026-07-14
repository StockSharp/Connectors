namespace StockSharp.Ligther.Native.Spot;

using StockSharp.Ligther.Native.Common;

sealed class SpotAdapter : LigtherSectionAdapter
{
	public SpotAdapter(LigtherMessageAdapter owner)
		: base(owner, BoardCodes.LigtherSpot)
	{
	}

	public override LigtherSections Section => LigtherSections.Spot;
	protected override string SectionName => "Spot";
	protected override bool IsSpotSection => true;
	protected override SecurityTypes SectionSecurityType => SecurityTypes.CryptoCurrency;
}
