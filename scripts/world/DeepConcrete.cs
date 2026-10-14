using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-14c, the graphics pass, Acts 15-21) Old stained concrete for the deep places: the round room's drum (Act 20),
/// the little room above it and the long stair's concrete (Act 21), the sewer's bulkhead (Act 17). They were the pale
/// board-marked "clean" concrete at 0.7-0.8 with no relief: under the bulbs and the lantern one flat, bright tone. This is
/// Poly Haven's Concrete Wall 006 (stains, pours, pits, tie holes; its normal and roughness maps), world-projected so it
/// runs on round a drum and up a stair without seams, under a tint per place. Each call makes its own material.
/// </summary>
public static class DeepConcrete
{
	public static StandardMaterial3D Stained(string name, Color tint, float perMetre = 0.5f, float relief = 1.3f, float specular = 0.18f)
	{
		var m = new StandardMaterial3D
		{
			ResourceName = name, AlbedoTexture = StairwellParts.StairwellTextures.CleanConcrete, AlbedoColor = tint, Roughness = 1f,
			MetallicSpecular = specular, VertexColorUseAsAlbedo = true, Uv1Triplanar = true, Uv1WorldTriplanar = true,
			Uv1TriplanarSharpness = 6f, Uv1Scale = Vector3.One * perMetre,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		};
		return SurfaceSets.Scan(m, "concrete_wall_006", 1f, relief, tint);
	}
}
