using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Micro-surface detail (the owner's texture pass): a small, repeating detail map laid over a surface's own
/// texture, so wood shows its grain, plaster its trowel marks, iron its pitting, cloth its weave, close up,
/// for next to no memory. Twelve kinds, cut from Poly Haven's textures (CC0; `assets/textures/detail`, made
/// by `tools/Textures/make_detail.py`): each an albedo multiplier (only the fine grain kept, the broad colour
/// taken out, near white so nothing darkens) with the game's own grime baked into it, and a normal map for
/// a faint relief. Kept subtle on purpose (the owner): the PS2 horror look, not a modern one: the grain is
/// 256 px and soft, the relief shallow.
///
/// Materials made by the texture factories name their kind by their key (<see cref="ApplyByKey"/>); every
/// other mesh in the world is swept as it enters the tree (<see cref="DetailSweep"/>) and classed by its
/// node's name. The trees, the foliage and the ground have their own shaders, which sample the bark, leaf
/// and forest-floor details themselves.
/// </summary>
public static class DetailKit
{
	public enum Kind { Wood, Plaster, Concrete, Metal, Fabric, Leather, Stone, Marble, Bark, Foliage, Ground, Grime, Snow, Ice }


	private static readonly Dictionary<Kind, (Texture2D albedo, Texture2D normal)> _tex = new();
	private static Texture2D _flat;

	public static Texture2D Albedo(Kind k) => Get(k).albedo;
	public static Texture2D Normal(Kind k) => Get(k).normal;

	private static (Texture2D albedo, Texture2D normal) Get(Kind k)
	{
		if (_tex.TryGetValue(k, out var t)) return t;
		string n = k.ToString().ToLowerInvariant();
		var albedo = GD.Load<Texture2D>($"res://assets/textures/detail/{n}_albedo.png");
		// the game's own grime (stains, specks, pitting, hairline scratches) baked over the grain
		if (albedo?.GetImage() is { } img && ProcTextures.Grime().GetImage() is { } grime)
		{
			img = (Image)img.Duplicate();
			img.Decompress();
			img.Convert(Image.Format.Rgb8);
			grime.Decompress();
			int gw = grime.GetWidth(), gh = grime.GetHeight();
			for (int y = 0; y < img.GetHeight(); y++)
				for (int x = 0; x < img.GetWidth(); x++)
				{
					Color a = img.GetPixel(x, y), g = grime.GetPixel(x % gw, y % gh);
					img.SetPixel(x, y, new Color(a.R * g.R, a.G * g.G, a.B * g.B));
				}
			img.GenerateMipmaps();
			albedo = ImageTexture.CreateFromImage(img);
		}
		t = (albedo, GD.Load<Texture2D>($"res://assets/textures/detail/{n}_normal.png"));
		_tex[k] = t;
		return t;
	}

	/// <summary>A flat normal map (for materials that had none, so the detail's normal has one to blend into).</summary>
	private static Texture2D Flat => _flat ??= ImageTexture.CreateFromImage(FlatImage());

	private static Image FlatImage()
	{
		var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgb8);
		img.Fill(new Color(0.5f, 0.5f, 1f));
		return img;
	}

	/// <summary>Lays <paramref name="k"/>'s detail over <paramref name="m"/> (once; opaque materials only).</summary>
	public static void Apply(StandardMaterial3D m, Kind k, float relief = 0.3f)
	{
		if (m == null || m.Transparency != BaseMaterial3D.TransparencyEnum.Disabled || m.HasMeta("detail_kind")) return;
		m.SetMeta("detail_kind", (int)k);
		m.DetailEnabled = true;
		m.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mul;
		m.DetailUVLayer = BaseMaterial3D.DetailUV.UV1;
		m.DetailAlbedo = Albedo(k);
		m.DetailNormal = Normal(k);
		if (!m.NormalEnabled)
		{
			m.NormalEnabled = true;
			m.NormalTexture = Flat;
			m.NormalScale = relief;
		}
	}

	public static void ApplyByKey(StandardMaterial3D m, string key) => Apply(m, Classify(key) ?? Kind.Grime);

	/// <summary>Gives one of the game's own shaders (terrain, trees, foliage: they sample `detail_tex`) its detail.</summary>
	public static ShaderMaterial Hook(ShaderMaterial m, Kind k)
	{
		m?.SetShaderParameter("detail_tex", Albedo(k));
		return m;
	}

	private static readonly (string[] words, Kind kind)[] Words =
	{
		(new[] { "icicle", "ice_", "frozen", "glaze" }, Kind.Ice),
		(new[] { "snow", "drift" }, Kind.Snow),
		(new[] { "leather", "seat", "sofa", "couch", "armchair", "saddle", "book", "ledger", "journal", "album" }, Kind.Leather),
		(new[] { "fabric", "cloth", "velvet", "curtain", "drape", "sheet", "rug", "carpet", "tapestry", "blanket", "cushion", "linen", "canvas", "felt", "bag", "tape" }, Kind.Fabric),
		(new[] { "bark", "trunk", "stump", "log", "branch", "root" }, Kind.Bark),
		(new[] { "leaf", "leaves", "fern", "grass", "moss", "ivy", "bush", "plant", "vine", "foliage", "reed" }, Kind.Foliage),
		(new[] { "marble", "medallion" }, Kind.Marble),
		(new[] { "iron", "steel", "metal", "rust", "rail", "pipe", "chain", "grate", "grating", "rivet", "bolt", "hinge", "brass", "chrome", "tin", "wire", "cage", "valve", "radiator", "boiler", "lamp", "stair_s", "plate" }, Kind.Metal),
		(new[] { "plaster", "wallpaper", "paper", "render", "stucco", "ceiling" }, Kind.Plaster),
		(new[] { "concrete", "cement", "bunker", "tunnel" }, Kind.Concrete),
		(new[] { "stone", "brick", "rock", "ashlar", "cobble", "slate", "granite", "tomb", "column", "vault", "cliff", "boulder", "gravel" }, Kind.Stone),
		(new[] { "ground", "dirt", "mud", "soil", "earth", "floor_forest", "path", "trail", "terrain" }, Kind.Ground),
		(new[] { "wood", "plank", "board", "deck", "timber", "oak", "pine", "table", "chair", "desk", "shelf", "shelves", "cabinet", "door", "crate", "box", "pew", "bench", "beam", "post", "frame", "ladder", "stair", "drawer", "bed", "wardrobe", "barrel", "cabin", "fence", "sign" }, Kind.Wood),
	};

	/// <summary>A surface's kind from a texture key or a node name (null if nothing matches).</summary>
	public static Kind? Classify(string name)
	{
		if (string.IsNullOrEmpty(name)) return null;
		string n = name.ToLowerInvariant();
		foreach (var (words, kind) in Words)
			foreach (var w in words)
				if (n.Contains(w)) return kind;
		return null;
	}
}
