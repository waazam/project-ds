using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Builds the paper the player can read: a note, a page, a card or a small
/// board, as a thin quad with a low-res procedural sheet texture (paper grain
/// and a few faint lines of scrawl that read as writing from a distance but
/// resolve into nothing), plus the <see cref="Readable"/> that opens the real
/// text. Every readable in the game goes through here, so they all look like
/// they belong to the same world.
///
/// The sheet lies in its local XY plane facing +Z, origin at its centre, so a
/// note pinned to a wall gets the wall's facing basis, and one lying on a table
/// gets a basis whose Z points up.
/// </summary>
public static class PaperKit
{
	public enum Look { Note, Lined, Card, Ledger, Board }

	private static readonly Dictionary<string, StandardMaterial3D> _mats = new();

	/// <summary>
	/// A readable sheet. <paramref name="size"/> is width x height in metres (a note is about
	/// 0.14 x 0.18, a card 0.12 x 0.08, an open ledger 0.34 x 0.24). Returns the Readable so
	/// the caller can set ReadFlag or subscribe to Read.
	/// </summary>
	public static Readable Sheet(Node3D parent, Vector3 localPos, Basis basis, Vector2 size, Look look,
		string title, string text, Readable.NoteStyle style, string prompt = "Read", int seed = 1)
	{
		var mesh = new MeshInstance3D
		{
			Name = "Paper",
			Mesh = new QuadMesh { Size = size },
			MaterialOverride = Material(look, seed),
			Transform = new Transform3D(basis, localPos),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(mesh);

		var readable = new Readable
		{
			Name = "Readable",
			Title = title,
			Text = text,
			Style = style,
			Prompt = prompt,
			PickRadius = Mathf.Max(0.3f, Mathf.Max(size.X, size.Y) * 0.9f),
			MaxDistance = 2.6f,
		};
		mesh.AddChild(readable);
		return readable;
	}

	/// <summary>A sheet that sits flat on a surface at <paramref name="localPos"/> (its centre), turned by <paramref name="yawDeg"/>.</summary>
	public static Readable Flat(Node3D parent, Vector3 localPos, float yawDeg, Vector2 size, Look look,
		string title, string text, Readable.NoteStyle style, string prompt = "Read", int seed = 1)
	{
		// Face up (+Y): rotate the +Z-facing quad down onto the surface, then yaw it.
		var b = Basis.FromEuler(new Vector3(-Mathf.Pi / 2f, Mathf.DegToRad(yawDeg), 0));
		return Sheet(parent, localPos + Vector3.Up * 0.004f, b, size, look, title, text, style, prompt, seed);
	}

	/// <summary>A sheet pinned to a vertical surface whose outward normal is <paramref name="outward"/> (flat, in the parent's space).</summary>
	public static Readable Pinned(Node3D parent, Vector3 localPos, Vector3 outward, Vector2 size, Look look,
		string title, string text, Readable.NoteStyle style, float tiltDeg = 0f, string prompt = "Read", int seed = 1)
	{
		Vector3 z = new Vector3(outward.X, 0, outward.Z).Normalized();
		if (z.LengthSquared() < 0.5f) z = Vector3.Back;
		Vector3 x = Vector3.Up.Cross(z).Normalized();
		var b = new Basis(x, Vector3.Up, z);
		if (tiltDeg != 0f) b = b.Rotated(z, Mathf.DegToRad(tiltDeg));
		return Sheet(parent, localPos + z * 0.006f, b, size, look, title, text, style, prompt, seed);
	}

	public static StandardMaterial3D Material(Look look, int seed)
	{
		string key = $"{look}:{seed}";
		if (_mats.TryGetValue(key, out var m)) return m;
		m = new StandardMaterial3D
		{
			AlbedoTexture = Texture(look, seed),
			Roughness = 1f,
			MetallicSpecular = 0.08f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		_mats[key] = m;
		return m;
	}

	/// <summary>32 x 40 px: paper grain, an edge darkening, faint scrawl lines. Lines are the only ink; there is no legible text.</summary>
	private static ImageTexture Texture(Look look, int seed)
	{
		const int w = 32, h = 40;
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + (int)look * 131) };
		Color paper = look switch
		{
			Look.Card => new Color(0.86f, 0.85f, 0.80f),
			Look.Ledger => new Color(0.78f, 0.74f, 0.60f),
			Look.Board => new Color(0.72f, 0.66f, 0.52f),
			Look.Lined => new Color(0.84f, 0.82f, 0.74f),
			_ => new Color(0.82f, 0.78f, 0.68f),
		};
		Color ink = new(0.22f, 0.20f, 0.24f);
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var px = new Color[w, h];
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float grain = 1f + (rng.Randf() - 0.5f) * 0.10f;
				// edges and corners a little darker and browner: handled paper
				float ex = Mathf.Min(x, w - 1 - x) / (float)w, ey = Mathf.Min(y, h - 1 - y) / (float)h;
				float edge = Mathf.Clamp(Mathf.Min(ex, ey) * 6f, 0f, 1f);
				var c = paper * grain;
				c = c.Lerp(c * new Color(0.82f, 0.74f, 0.62f), (1f - edge) * 0.6f);
				px[x, y] = c;
			}
		// Ruled lines
		if (look == Look.Lined || look == Look.Ledger)
			for (int y = 6; y < h - 3; y += 4)
				for (int x = 2; x < w - 2; x++) px[x, y] = px[x, y].Lerp(new Color(0.55f, 0.62f, 0.72f), 0.35f);
		// Scrawl: short broken dark runs on every other row, denser toward the top for a note
		int rows = look == Look.Card ? 3 : look == Look.Board ? 4 : 7;
		int y0 = look == Look.Card ? 10 : 7;
		for (int r = 0; r < rows; r++)
		{
			int y = y0 + r * 4 + (int)(rng.Randf() * 1.4f);
			if (y >= h - 3) break;
			int x = 3 + (int)(rng.Randf() * 3);
			int end = w - 3 - (int)(rng.Randf() * (r == rows - 1 ? 14 : 5));
			while (x < end)
			{
				int run = 2 + (int)(rng.Randf() * 4);
				float dark = 0.45f + rng.Randf() * 0.35f;
				for (int i = 0; i < run && x + i < end; i++)
				{
					px[x + i, y] = px[x + i, y].Lerp(ink, dark);
					if (rng.Randf() < 0.3f && y + 1 < h) px[x + i, y + 1] = px[x + i, y + 1].Lerp(ink, dark * 0.4f);
				}
				x += run + 1 + (int)(rng.Randf() * 2);
			}
		}
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++) img.SetPixel(x, y, px[x, y]);
		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}
}
