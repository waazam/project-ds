using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The photo surfaces (Poly Haven, CC0; `assets/textures/surfaces`, made by `tools/Textures/make_surfaces.py`)
/// for the texture factories to draw with: the lodge's parquet, dark wood, stone, plaster, leather, velvet,
/// linen and carpet weave, and the church's limestone, brick, cobbles, plaster, oak and planks.
///
/// A factory samples one at its own (u, v) as it draws a tile, so the photo repeats exactly with its
/// pattern: <see cref="Sample"/> takes the colour, <see cref="Grain"/> only the light and dark of it (around 1),
/// to lay under a drawn pattern. <see cref="Relief"/> gives the material the photo's normal map at the same
/// repeat. Everything is cached; a missing file samples as plain white (and 1 for the grain) so nothing breaks.
/// </summary>
public static class SurfaceKit
{
	private sealed class Photo
	{
		public int W, H;
		public Color[] Px;
		public float Mean = 1f;
	}

	private static readonly Dictionary<string, Photo> _photos = new();
	private static readonly Dictionary<string, Texture2D> _normals = new();

	private static Photo Get(string name)
	{
		if (_photos.TryGetValue(name, out var p)) return p;
		p = new Photo { W = 1, H = 1, Px = new[] { Colors.White } };
		var tex = GD.Load<Texture2D>($"res://assets/textures/surfaces/{name}_albedo.png");
		if (tex?.GetImage() is { } img)
		{
			img = (Image)img.Duplicate();
			img.Decompress();
			img.Convert(Image.Format.Rgb8);
			p.W = img.GetWidth(); p.H = img.GetHeight();
			p.Px = new Color[p.W * p.H];
			double sum = 0;
			for (int y = 0; y < p.H; y++)
				for (int x = 0; x < p.W; x++)
				{
					var c = img.GetPixel(x, y);
					p.Px[y * p.W + x] = c;
					sum += Lum(c);
				}
			p.Mean = Mathf.Max(0.02f, (float)(sum / p.Px.Length));
		}
		else GD.PushWarning($"[surfaces] missing {name}");
		_photos[name] = p;
		return p;
	}

	private static float Lum(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

	/// <summary>The photo's colour at (u, v), tile-wrapped, bilinear. <paramref name="repeat"/> tiles it that many
	/// times across the caller's tile.</summary>
	public static Color Sample(string name, float u, float v, float repeat = 1f)
	{
		var p = Get(name);
		float x = Mathf.PosMod(u * repeat, 1f) * p.W - 0.5f, y = Mathf.PosMod(v * repeat, 1f) * p.H - 0.5f;
		int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
		float fx = x - x0, fy = y - y0;
		Color At(int xi, int yi) => p.Px[((yi % p.H + p.H) % p.H) * p.W + ((xi % p.W + p.W) % p.W)];
		return At(x0, y0).Lerp(At(x0 + 1, y0), fx).Lerp(At(x0, y0 + 1).Lerp(At(x0 + 1, y0 + 1), fx), fy);
	}

	/// <summary>The photo's grain at (u, v): its brightness over its own average (about 1), eased toward 1 by
	/// <paramref name="strength"/> (0 = flat, 1 = all of it).</summary>
	public static float Grain(string name, float u, float v, float strength, float repeat = 1f)
	{
		var p = Get(name);
		float g = Lum(Sample(name, u, v, repeat)) / p.Mean;
		return Mathf.Lerp(1f, Mathf.Clamp(g, 0.2f, 2.2f), strength);
	}

	/// <summary>The photo's colour, with its brightness normalised to <paramref name="level"/> on average, keeping
	/// <paramref name="keep"/> of its own colour (the rest grey), and tinted.</summary>
	public static Color Tinted(string name, float u, float v, Color tint, float level, float keep = 1f, float repeat = 1f)
	{
		var p = Get(name);
		var c = Sample(name, u, v, repeat);
		float l = Lum(c);
		c = new Color(l, l, l).Lerp(c, keep) * (level / p.Mean);
		return new Color(c.R * tint.R, c.G * tint.G, c.B * tint.B);
	}

	public static Texture2D NormalMap(string name)
	{
		if (_normals.TryGetValue(name, out var t)) return t;
		t = GD.Load<Texture2D>($"res://assets/textures/surfaces/{name}_normal.png");
		_normals[name] = t;
		return t;
	}

	/// <summary>Gives <paramref name="m"/> the photo's relief (call before DetailKit, which keeps a normal map it finds).</summary>
	public static void Relief(StandardMaterial3D m, string name, float strength = 0.6f)
	{
		var n = NormalMap(name);
		if (m == null || n == null) return;
		m.NormalEnabled = true;
		m.NormalTexture = n;
		m.NormalScale = strength;
	}
}
