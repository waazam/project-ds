using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.HallwayParts;

/// <summary>
/// (2026-10-14h, the old-and-abandoned pass) Act 15's hall as an old institutional service corridor: its materials. The walls are
/// <c>corridor_wall.gdshader</c> (two-tone paint on block, chipped, stained); here are the floor's vinyl tile, the doors'
/// wired glass, the rail, the extinguisher cabinets' red, the signs' enamel, and the floor strips' housings.
/// </summary>
public static class CorridorTextures
{
	private static readonly Dictionary<string, Texture2D> _tex = new();

	private static Texture2D Make(string key, int w, int h, Func<int, int, Color> f)
	{
		if (_tex.TryGetValue(key, out var t)) return t;
		var tg = Systems.TexGen.Start();
		Image img = Systems.TexCache.Load("Corridor_" + key);
		if (img == null)
		{
			img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
					img.SetPixel(x, y, f(x, y));
			img.GenerateMipmaps();
			Systems.TexCache.Save("Corridor_" + key, img);
		}
		t = ImageTexture.CreateFromImage(img);
		Systems.TexGen.Stop(tg);
		_tex[key] = t;
		return t;
	}

	private static float Hash(int x, int y, int seed)
	{
		uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
	}

	/// <summary>Old vinyl composition tile, 30 cm, eight to a side (2.4 m a repeat): off-white and a grey-green in a
	/// broken checker, flecked, each tile its own shade and wear, the joints dark with dirt.</summary>
	public static Texture2D VinylTile => Make("vct", 512, 512, (x, y) =>
	{
		int tx = x / 64, ty = y / 64, lx = x % 64, ly = y % 64;
		bool alt = ((tx + ty) & 1) == 0 && Hash(tx, ty, 77) > 0.18f;
		Color b = alt ? new Color(0.5f, 0.52f, 0.45f) : new Color(0.66f, 0.63f, 0.55f);
		b *= 0.88f + 0.2f * Hash(tx, ty, 5);
		// the flecks through it
		float fl = Hash(x, y, 9);
		if (fl > 0.93f) b *= 0.72f;
		else if (fl < 0.05f) b = b.Lerp(new Color(0.8f, 0.78f, 0.7f), 0.5f);
		// wear: duller and darker toward a tile's middle-ish, a corner chipped
		float d = Mathf.Min(Mathf.Min(lx, 63 - lx), Mathf.Min(ly, 63 - ly));
		if (d < 1.2f) b *= 0.45f;
		else if (d < 2.5f) b *= 0.8f;
		if (Hash(tx, ty, 31) > 0.82f && lx + ly < 10) b = new Color(0.16f, 0.13f, 0.1f);   // a corner broken off, the black adhesive
		return b;
	});

	/// <summary>Georgian wired glass: a square wire mesh in a dull, dirty pane (dark: nothing lit behind it).</summary>
	public static Texture2D WiredGlass => Make("wired_glass", 64, 64, (x, y) =>
	{
		float g = 0.09f + 0.05f * Hash(x / 4, y / 4, 3);
		var c = new Color(g * 0.9f, g, g * 0.95f);
		if (x % 16 == 0 || y % 16 == 0) c = new Color(0.32f, 0.3f, 0.27f);
		if (Hash(x, y, 8) > 0.97f) c *= 1.6f;
		return c;
	});

	private static StandardMaterial3D _floor, _rail, _cove, _channel, _shard, _red, _enamel, _glass, _door, _plate, _paper, _lampShade, _sign;

	/// <summary>The hall's floor: the vinyl tile, world-projected, with a little grit in its relief.</summary>
	public static StandardMaterial3D FloorMat => _floor ??= SurfaceSets.Relief(new StandardMaterial3D
	{
		ResourceName = "hw_vinyl_floor", AlbedoTexture = VinylTile, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One / 2.4f,
		Roughness = 0.75f, MetallicSpecular = 0.4f, VertexColorUseAsAlbedo = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
	}, "concrete_b", 0.35f);

	/// <summary>The bump rail: old varnished hardwood, dark and scuffed.</summary>
	public static StandardMaterial3D RailMat => _rail ??= SurfaceSets.Hinoki(new StandardMaterial3D
	{
		ResourceName = "hw_bump_rail", VertexColorUseAsAlbedo = true,
	}, new Color(0.42f, 0.31f, 0.22f), 9f, 0.8f);

	/// <summary>The black rubber cove base along the walls' foot, perished.</summary>
	public static StandardMaterial3D CoveMat => _cove ??= new StandardMaterial3D
	{
		ResourceName = "hw_cove_base", AlbedoColor = new Color(0.07f, 0.065f, 0.06f), Roughness = 0.85f, MetallicSpecular = 0.25f, VertexColorUseAsAlbedo = true,
	};

	/// <summary>The floor strips' channels: old steel, gone dull and dirty.</summary>
	public static StandardMaterial3D ChannelMat => _channel ??= SurfaceSets.Scan(new StandardMaterial3D
	{
		ResourceName = "hw_strip_channel_old", Metallic = 0.45f, Roughness = 0.75f, VertexColorUseAsAlbedo = true,
		Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 2f,
	}, "rusty_metal_03", 1f, 0.6f, new Color(0.3f, 0.28f, 0.24f));

	/// <summary>A broken piece of a strip's lens lying over it: yellowed, filthy plastic.</summary>
	public static StandardMaterial3D ShardMat => _shard ??= new StandardMaterial3D
	{
		ResourceName = "hw_strip_shard", AlbedoColor = new Color(0.36f, 0.33f, 0.2f), Roughness = 0.6f, MetallicSpecular = 0.5f, VertexColorUseAsAlbedo = true,
	};

	/// <summary>The fire cabinets' and extinguishers' red, chipped to the steel.</summary>
	public static StandardMaterial3D RedMat => _red ??= SurfaceSets.Scan(new StandardMaterial3D
	{
		ResourceName = "hw_cabinet_red", Roughness = 0.7f, Metallic = 0.2f, VertexColorUseAsAlbedo = true,
		Uv1Triplanar = true, Uv1WorldTriplanar = false, Uv1Scale = Vector3.One * 1.6f,
	}, "rusty_metal_02", 1f, 0.7f, new Color(0.62f, 0.14f, 0.1f));

	/// <summary>Enamel signs: cream, crazed, rust at the screw holes.</summary>
	public static StandardMaterial3D EnamelMat => _enamel ??= SurfaceSets.Scan(new StandardMaterial3D
	{
		ResourceName = "hw_sign_enamel", Roughness = 0.45f, Metallic = 0.1f, VertexColorUseAsAlbedo = true,
		Uv1Triplanar = true, Uv1WorldTriplanar = false, Uv1Scale = Vector3.One * 2.5f,
	}, "rusty_metal_03", 1f, 0.4f, new Color(0.78f, 0.75f, 0.64f));

	/// <summary>The doors' vision panels: wired glass, dirty, nothing lit behind.</summary>
	public static StandardMaterial3D GlassMat => _glass ??= new StandardMaterial3D
	{
		ResourceName = "hw_wired_glass", AlbedoTexture = WiredGlass, Uv1Scale = new Vector3(3f, 5f, 1f), Roughness = 0.12f, MetallicSpecular = 0.9f,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	};

	/// <summary>The service doors: old institutional paint over steel, its rust coming through.</summary>
	public static StandardMaterial3D DoorMat => _door ??= SurfaceSets.Scan(new StandardMaterial3D
	{
		ResourceName = "hw_service_door", Roughness = 0.8f, MetallicSpecular = 0.35f, VertexColorUseAsAlbedo = true,
		Uv1Triplanar = true, Uv1WorldTriplanar = false, Uv1TriplanarSharpness = 8f, Uv1Scale = Vector3.One * 1.3f,
	}, "rusty_metal_02", 1f, 0.8f, new Color(0.46f, 0.5f, 0.44f));

	/// <summary>Brass-ish kick and push plates, number plates, gone brown.</summary>
	public static StandardMaterial3D PlateMat => _plate ??= new StandardMaterial3D
	{
		ResourceName = "hw_brass_plate", AlbedoColor = new Color(0.42f, 0.36f, 0.22f), Metallic = 0.7f, Roughness = 0.55f, VertexColorUseAsAlbedo = true,
	};

	/// <summary>Paper on the floor: old forms, yellowed, trodden.</summary>
	public static StandardMaterial3D PaperMat => _paper ??= new StandardMaterial3D
	{
		ResourceName = "hw_floor_paper", AlbedoColor = new Color(0.6f, 0.57f, 0.47f), Roughness = 0.95f, VertexColorUseAsAlbedo = true,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};

	/// <summary>The hanging lamps' shades: old enamel, chipped through to rust.</summary>
	public static StandardMaterial3D LampShadeMat => _lampShade ??= SurfaceSets.Scan(new StandardMaterial3D
	{
		ResourceName = "hw_lamp_enamel", Roughness = 0.55f, Metallic = 0.25f, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		Uv1Triplanar = true, Uv1WorldTriplanar = false, Uv1Scale = Vector3.One * 3f,
	}, "rusty_metal_03", 1f, 0.5f, new Color(0.5f, 0.5f, 0.43f));

	/// <summary>The signs' and the plates' backing: dark painted steel.</summary>
	public static StandardMaterial3D SignBackMat => _sign ??= new StandardMaterial3D
	{
		ResourceName = "hw_sign_back", AlbedoColor = new Color(0.12f, 0.12f, 0.11f), Roughness = 0.7f, Metallic = 0.3f, VertexColorUseAsAlbedo = true,
	};
}
