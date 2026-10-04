using Godot;

namespace ProjectDS.World;

/// <summary>
/// Candle wax that the light gets into (2026-10-04, the owner: "make everything stand out more"): the light scattered
/// under its surface and shining warm through its thin edges, so a candle reads as wax beside its flame, not as painted
/// plaster.
/// </summary>
public static class WaxMaterial
{
	public static StandardMaterial3D Make(Color albedo, float roughness, string name = "wax") => new()
	{
		ResourceName = name, AlbedoColor = albedo, Roughness = roughness,
		SubsurfScatterEnabled = true, SubsurfScatterStrength = 0.45f, SubsurfScatterSkinMode = false,
		SubsurfScatterTransmittanceEnabled = true, SubsurfScatterTransmittanceColor = new Color(1f, 0.72f, 0.42f),
		SubsurfScatterTransmittanceDepth = 0.08f, SubsurfScatterTransmittanceBoost = 0.25f,
	};
}
