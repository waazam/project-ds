using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Spider webs from photographs (the owner's pick, Resource Boy's spider web textures: royalty-free, no
/// attribution needed; the files may not be passed on by themselves, only as part of the game). Twelve
/// of them, cut down to 1024 px with the fine strands strengthened so they survive the smaller size
/// (<c>assets/textures/webs</c>): two orb webs, five corner webs, a sagging sheet, and four old tangles.
///
/// A web is a card: one quad, lit, double-sided, soft-edged, casting no shadow, a little glossy so the
/// lantern catches the strands. It replaces the old procedural radial texture, which read as geometry.
/// </summary>
public static class WebKit
{
	public enum Kind { Orb, Corner, Sheet, Tangle }

	private static readonly int[][] ByKind =
	{
		new[] { 0, 1 }, new[] { 2, 3, 4, 5, 6 }, new[] { 7 }, new[] { 8, 9, 10, 11 },
	};

	private static readonly Dictionary<int, StandardMaterial3D> _mats = new();

	/// <summary>The material for web texture <paramref name="i"/> (0..11), cached.</summary>
	public static StandardMaterial3D Mat(int i)
	{
		if (_mats.TryGetValue(i, out var m)) return m;
		m = new StandardMaterial3D
		{
			AlbedoTexture = GD.Load<Texture2D>($"res://assets/textures/webs/web_{i:00}.png"),
			AlbedoColor = new Color(0.8f, 0.79f, 0.75f, 0.85f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			// a little sheen for the lantern to catch, not so much that a coloured light dyes the strands
			Roughness = 0.55f,
			MetallicSpecular = 0.3f,
			RimEnabled = true, Rim = 0.2f, RimTint = 0f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		};
		_mats[i] = m;
		return m;
	}

	/// <summary>A web of the given kind, chosen by <paramref name="rng"/>.</summary>
	public static StandardMaterial3D Pick(Kind kind, RandomNumberGenerator rng)
	{
		var list = ByKind[(int)kind];
		return Mat(list[rng.RandiRange(0, list.Length - 1)]);
	}

	/// <summary>A web card facing <paramref name="normal"/>, <paramref name="size"/> across, turned by
	/// <paramref name="spin"/> about its normal.</summary>
	public static MeshInstance3D Card(Node3D parent, Material mat, Vector3 at, Vector3 normal, Vector2 size, float spin = 0f, string name = "Web")
	{
		var mi = new MeshInstance3D
		{
			Name = name, Mesh = new QuadMesh { Size = size }, MaterialOverride = mat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(mi);
		Vector3 n = normal.Normalized();
		Vector3 up = Mathf.Abs(n.Y) > 0.9f ? Vector3.Forward : Vector3.Up;
		mi.Transform = new Transform3D(Basis.LookingAt(-n, up) * new Basis(Vector3.Back, spin), at);
		return mi;
	}

	public static MeshInstance3D Card(Node3D parent, Kind kind, RandomNumberGenerator rng, Vector3 at, Vector3 normal, Vector2 size, float spin = 0f, string name = "Web")
		=> Card(parent, Pick(kind, rng), at, normal, size, spin, name);

	/// <summary>A web spun across a corner where two walls (and, if <paramref name="ceiling"/>, the ceiling)
	/// meet: a card set diagonally across the angle, its edges running into the walls. <paramref name="corner"/>
	/// is the corner's point (on the ceiling line, or at the height wanted); <paramref name="outA"/> and
	/// <paramref name="outB"/> point from the corner out along the two walls, into the room.</summary>
	public static MeshInstance3D Corner(Node3D parent, RandomNumberGenerator rng, Vector3 corner, Vector3 outA, Vector3 outB, float size, bool ceiling = true)
	{
		Vector3 a = outA.Normalized(), b = outB.Normalized();
		Vector3 n = (a + b + (ceiling ? Vector3.Down * 0.8f : Vector3.Zero)).Normalized();
		Vector3 at = corner + n * size * 0.28f;
		var kind = rng.Randf() < 0.55f ? Kind.Corner : rng.Randf() < 0.5f ? Kind.Tangle : Kind.Orb;
		return Card(parent, kind, rng, at, n, Vector2.One * size * rng.RandfRange(0.85f, 1.15f), rng.RandfRange(0f, Mathf.Tau), "CornerWeb");
	}

	/// <summary>Webs in the corners of a box-shaped room (floor y0 to ceiling y1, x and z between the given
	/// bounds): up in each top corner, and now and then one low down.</summary>
	public static void DressRoom(Node3D parent, RandomNumberGenerator rng, float x0, float x1, float z0, float z1, float y0, float y1, float size = 1.1f, float chance = 0.8f)
	{
		var corners = new[]
		{
			(new Vector3(x0, 0, z0), Vector3.Right, Vector3.Back), (new Vector3(x1, 0, z0), Vector3.Left, Vector3.Back),
			(new Vector3(x0, 0, z1), Vector3.Right, Vector3.Forward), (new Vector3(x1, 0, z1), Vector3.Left, Vector3.Forward),
		};
		foreach (var (c, a, b) in corners)
		{
			if (rng.Randf() < chance) Corner(parent, rng, c + Vector3.Up * y1, a, b, size, true);
			if (rng.Randf() < chance * 0.3f) Corner(parent, rng, c + Vector3.Up * (y0 + 0.05f), a, b, size * 0.6f, false);
		}
	}
}
