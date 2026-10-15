using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-14h, the Hollow's interiors pass) The cabin's and the shed's own surfaces, so no two parts of them share one
/// wood. The survey found the cabin one orange-brown value from floor to ridge (logs, floor, rafters, furniture) and the
/// shed one wood on everything. Each part gets its own value and its own grain here:
/// <list type="bullet">
/// <item>the logs a weathered silver-brown, round (<see cref="CabinLogOut"/>, <see cref="CabinLogIn"/>), the chinking
/// between them a pale lime mortar (the palest thing on a wall);</item>
/// <item>the poles overhead smoked near black; the floor dark dirty boards (<see cref="CabinFloor"/>);</item>
/// <item>the furniture each its own: a stained shelf, a scrubbed pale crate, a dark cot frame, a grey wool blanket,
/// black cast iron for the stove;</item>
/// <item>the shed's inside bare rough-sawn boards (never painted: the red was only ever outside), an oil-black bench
/// top, a brown hardboard pegboard with its holes, steel and wooden-handled tools.</item>
/// </list>
/// The StandardMaterial3D ones go through <see cref="RoomSurface.Of"/> where they are a room's surfaces, so the
/// <see cref="RoomShade"/> bake (corners, seams, soot) and the weathering overlay land on them.
/// </summary>
public static class HollowLook
{
	private static readonly Dictionary<string, Material> _m = new();

	private static T Cache<T>(string key, System.Func<T> make) where T : Material
	{
		if (_m.TryGetValue(key, out var m)) return (T)m;
		var made = make();
		_m[key] = made;
		return made;
	}

	/// <summary>The round wood (hinoki_round: grain along V), weathered grey, without its normal map (MeshKit writes no
	/// tangents; the round geometry does the shading).</summary>
	private static StandardMaterial3D RoundWood(string name, Color tint)
	{
		var m = new StandardMaterial3D { ResourceName = name, VertexColorUseAsAlbedo = true, Roughness = 1f, MetallicSpecular = 0.15f };
		if (SurfaceSets.Tex("hinoki_round") is { } a)
		{
			m.AlbedoTexture = a;
			if (SurfaceSets.Tex("hinoki_round", "rough") is { } r) { m.RoughnessTexture = r; m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red; }
		}
		else m.AlbedoTexture = BuildingTextures.Log();
		m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
		m.AlbedoColor = tint;
		return m;
	}

	/// <summary>The logs' outside faces: silver-brown, weathered by the rain (mottled, not baked).</summary>
	public static Material CabinLogOut => Cache("h_log_out", () =>
		RoomSurface.Of(RoundWood("h_cabinlog_out", new Color(0.56f, 0.55f, 0.53f)), RoomSurface.Wood with { Desaturate = 0.8f, Mottle = 0.45f, Grime = 1f }, 31));
	/// <summary>The logs' inside faces: a step darker and browner (smoke, hands), baked by the room's shade.</summary>
	public static Material CabinLogIn => Cache("h_log_in", () =>
		RoomSurface.Of(RoundWood("h_cabinlog_in", new Color(0.5f, 0.49f, 0.47f)), RoomSurface.Wood with { Desaturate = 0.78f, Mottle = 0.4f, Streaks = 0.2f, Tide = 0.15f, Grime = 1.05f }, 32));
	/// <summary>The poles under the roof (rafters, ridge, purlin, tie beams): the same wood smoked near black.</summary>
	public static Material SmokedPole => Cache("h_pole", () =>
		RoomSurface.Of(RoundWood("h_smokedpole", new Color(0.24f, 0.21f, 0.19f)), RoomSurface.Wood with { Desaturate = 0.45f, Mottle = 0.3f, Grime = 0.8f }, 33));

	/// <summary>Lime mortar chinking between the logs: the palest, greyest thing on the walls.</summary>
	private static StandardMaterial3D Mortar(string name, Color tint)
	{
		var m = new StandardMaterial3D
		{
			ResourceName = name, VertexColorUseAsAlbedo = true, Roughness = 1f, MetallicSpecular = 0.1f,
			Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1TriplanarSharpness = 4f, Uv1Scale = Vector3.One / 0.9f,
		};
		SurfaceSets.Apply(m, "plaster", 0.5f);
		m.NormalEnabled = false;
		m.AlbedoColor = tint;
		return m;
	}
	public static Material ChinkOut => Cache("h_chink_out", () => RoomSurface.Of(Mortar("h_chink_out", new Color(0.7f, 0.68f, 0.62f)), RoomSurface.Plaster with { Desaturate = 0.3f, Streaks = 0.5f }, 34));
	public static Material ChinkIn => Cache("h_chink_in", () => RoomSurface.Of(Mortar("h_chink_in", new Color(0.6f, 0.58f, 0.52f)), RoomSurface.Plaster with { Desaturate = 0.3f, Streaks = 0.3f }, 35));

	/// <summary>The cabin's floor: wide old boards, near black with dirt and oil, grey where feet went.</summary>
	public static Material CabinFloor => BuildingTextures.PaintedBoards("h_cabinfloor", new Color(0.2f, 0.19f, 0.17f), 0.2f, 0.9f, vertical: false, scale: 0.5f, footY: -9f,
		footHeight: 0.1f, woodTint: new Color(0.36f, 0.33f, 0.3f), grime: 0.3f, topAlongZ: true) ?? (Material)BuildingTextures.FloorMat;
	/// <summary>The window's inside glass: filmed with dirt and smoke, dull (glossy, it threw back the room's lights as a glow).</summary>
	public static StandardMaterial3D GlassInside => (StandardMaterial3D)Cache("h_glass_in", () => new StandardMaterial3D { ResourceName = "h_glass_in", AlbedoColor = new Color(0.05f, 0.055f, 0.055f), Roughness = 0.6f, MetallicSpecular = 0.3f });

	/// <summary>The shelf: stained, oiled dark by hands, grain along.</summary>
	public static Material ShelfWood => BuildingTextures.PaintedBoards("h_shelf", new Color(0.3f, 0.28f, 0.25f), 0f, 1f, vertical: false, scale: 0.7f, footY: -9f,
		footHeight: 0.1f, woodTint: new Color(0.42f, 0.35f, 0.28f), grime: 0.6f, across: 0.12f) ?? (Material)PropTextures.PostMat;
	/// <summary>The cot's frame: hardwood, near black with age.</summary>
	public static Material CotWood => BuildingTextures.PaintedBoards("h_cot", new Color(0.3f, 0.28f, 0.25f), 0f, 1f, vertical: false, scale: 0.7f, footY: -9f,
		footHeight: 0.1f, woodTint: new Color(0.3f, 0.26f, 0.22f), grime: 0.5f, across: 0.12f) ?? (Material)PropTextures.PostMat;
	/// <summary>The crate: rough pale deal, scrubbed grey.</summary>
	public static Material CrateWood => BuildingTextures.PaintedBoards("h_crate", new Color(0.3f, 0.28f, 0.25f), 0f, 1f, vertical: false, scale: 0.5f, footY: -9f,
		footHeight: 0.1f, woodTint: new Color(0.74f, 0.7f, 0.62f), grime: 0.45f) ?? (Material)PropTextures.DeckMat;

	/// <summary>The stove's cast iron: near black, a dull sheen, rust where the damp got at it.</summary>
	public static Material StoveIron => Cache("h_iron", () =>
	{
		var m = new StandardMaterial3D { ResourceName = "h_stoveiron", VertexColorUseAsAlbedo = true, Roughness = 0.62f, MetallicSpecular = 0.45f, Metallic = 0.25f };
		if (SurfaceSets.Tex("ph_rusty_metal_02") is { } a) { m.AlbedoTexture = a; m.Uv1Scale = new Vector3(1.4f, 1.4f, 1f); }
		else m.AlbedoTexture = ProcTextures.Metal();
		m.AlbedoColor = new Color(0.26f, 0.24f, 0.23f);
		m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
		return RoomSurface.Of(m, RoomSurface.Stone with { Streaks = 0.1f, Tide = 0f, Desaturate = 0.3f }, 36);
	});

	/// <summary>The cot's blanket: an old grey army wool, gone brown at the edges.</summary>
	public static StandardMaterial3D Blanket => (StandardMaterial3D)Cache("h_blanket", () =>
	{
		var m = new StandardMaterial3D { ResourceName = "h_blanket", VertexColorUseAsAlbedo = true, Roughness = 1f, MetallicSpecular = 0.1f, AlbedoColor = new Color(0.3f, 0.29f, 0.26f) };
		if (SurfaceSets.Tex("linen") is { } a) { m.AlbedoTexture = a; m.Uv1Scale = new Vector3(3f, 3f, 1f); m.AlbedoColor = new Color(0.4f, 0.38f, 0.34f); }
		return m;
	});

	// ---------------- the shed

	/// <summary>The shed's walls from inside: bare rough-sawn boards, grey-brown, never painted.</summary>
	public static Material ShedInside => BuildingTextures.PaintedBoards("h_shed_in", new Color(0.3f, 0.28f, 0.25f), 0f, 1f, vertical: true, scale: 0.45f, footY: -0.1f,
		footHeight: 0.5f, woodTint: new Color(0.42f, 0.4f, 0.37f), grime: 0.75f) ?? (Material)BuildingTextures.BoardsMat;
	/// <summary>The shed's roof boards from below: dark, cobwebbed, grain across.</summary>
	public static Material ShedCeiling => BuildingTextures.PaintedBoards("h_shed_ceil", new Color(0.3f, 0.28f, 0.25f), 0f, 1f, vertical: false, scale: 0.45f, footY: -9f,
		footHeight: 0.1f, woodTint: new Color(0.34f, 0.31f, 0.28f), grime: 0.5f, topAlongZ: true) ?? (Material)BuildingTextures.BoardsMat;
	/// <summary>The bench top: thick boards black with oil, grease and years.</summary>
	public static Material BenchTop => BuildingTextures.PaintedBoards("h_bench", new Color(0.12f, 0.1f, 0.09f), 0.55f, 0.9f, vertical: false, scale: 0.55f, footY: -9f,
		footHeight: 0.1f, woodTint: new Color(0.36f, 0.31f, 0.26f), grime: 0.85f, topAlongZ: false) ?? (Material)PropTextures.DeckMat;
	/// <summary>The shed's floor: the cabin's boards, dustier and paler (sawdust and dried mud).</summary>
	public static Material ShedFloor => BuildingTextures.PaintedBoards("h_shed_floor", new Color(0.4f, 0.37f, 0.32f), 0.35f, 0.9f, vertical: false, scale: 0.5f, footY: -9f,
		footHeight: 0.1f, woodTint: new Color(0.5f, 0.46f, 0.4f), grime: 0.35f, topAlongZ: true) ?? (Material)BuildingTextures.FloorMat;

	/// <summary>Brown hardboard pegboard: a grid of holes an inch apart (64 px, 16 holes to the 0.4 m tile).</summary>
	public static StandardMaterial3D Pegboard => (StandardMaterial3D)Cache("h_peg", () =>
	{
		const int n = 64;
		var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);
		var rng = new RandomNumberGenerator { Seed = 7171 };
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float fx = (x % 4) - 1.5f, fy = (y % 4) - 1.5f;
				float d = Mathf.Sqrt(fx * fx + fy * fy);
				float hole = d < 0.9f ? 1f : (d < 1.3f ? 0.45f : 0f);
				float grain = 0.92f + 0.12f * rng.Randf();
				var c = new Color(0.3f, 0.23f, 0.17f) * grain;
				c = c.Lerp(new Color(0.03f, 0.025f, 0.02f), hole);
				img.SetPixel(x, y, c);
			}
		img.GenerateMipmaps();
		var m = new StandardMaterial3D
		{
			ResourceName = "h_pegboard", AlbedoTexture = ImageTexture.CreateFromImage(img), VertexColorUseAsAlbedo = true,
			Roughness = 0.9f, MetallicSpecular = 0.2f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		return m;
	});

	/// <summary>Tool steel: grey, a little bright on the worn edges, rust in the pits.</summary>
	public static StandardMaterial3D ToolSteel => (StandardMaterial3D)Cache("h_steel", () =>
	{
		var m = new StandardMaterial3D { ResourceName = "h_toolsteel", VertexColorUseAsAlbedo = true, Roughness = 0.45f, Metallic = 0.6f, MetallicSpecular = 0.6f, AlbedoColor = new Color(0.46f, 0.45f, 0.43f) };
		if (SurfaceSets.Tex("ph_rusty_metal_03") is { } a) { m.AlbedoTexture = a; m.Uv1Scale = new Vector3(3f, 3f, 1f); m.AlbedoColor = new Color(0.7f, 0.68f, 0.66f); }
		m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
		return m;
	});

	/// <summary>Tool handles: ash and hickory gone dark with hands (tinted per tool by vertex colour).</summary>
	public static StandardMaterial3D Handle => (StandardMaterial3D)Cache("h_handle", () =>
	{
		var m = new StandardMaterial3D { ResourceName = "h_handle", VertexColorUseAsAlbedo = true, Roughness = 0.55f, MetallicSpecular = 0.35f, AlbedoColor = new Color(0.62f, 0.46f, 0.3f) };
		if (SurfaceSets.Tex("oak") is { } a) { m.AlbedoTexture = a; m.Uv1Scale = new Vector3(4f, 4f, 1f); }
		return m;
	});

	/// <summary>Dipped grips (pliers, a screwdriver's): an old oxblood rubber.</summary>
	public static StandardMaterial3D Grip => BuildingTextures.Plain("h_grip", new Color(0.32f, 0.08f, 0.06f), 0.6f);
}
