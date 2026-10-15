using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-15, the owner: every model built in Blender) The Hollow's, the station's and the library's fixtures modelled in
/// Blender (tools/Blender/fixtures.py → assets/models/furniture/fx_*.glb) and put in through <see cref="FurnitureKit"/>.
/// Each is drawn in its old C# builder's own local frame, so the builder adds it with the transform it used; the builder's
/// code stays as the fallback when the model isn't there. The code tinted parts with vertex colours (a rusted top plate,
/// a darker ticking); a model's parts come in white, so those tints become materials of their own here.
/// </summary>
public static class FixtureModels
{
	private static readonly Dictionary<(Material, Color), Material> _tints = new();

	/// <summary><paramref name="m"/> with its albedo multiplied by <paramref name="c"/> (cached; anything not a
	/// StandardMaterial3D is returned as it is).</summary>
	public static Material Tint(Material m, Color c)
	{
		if (m is not StandardMaterial3D sm) return m;
		if (_tints.TryGetValue((m, c), out var t)) return t;
		var d = (StandardMaterial3D)sm.Duplicate();
		d.AlbedoColor = new Color(sm.AlbedoColor.R * c.R, sm.AlbedoColor.G * c.G, sm.AlbedoColor.B * c.B, sm.AlbedoColor.A);   // (a scaled tint's alpha is scaled too: kept)
		d.ResourceName = (sm.ResourceName ?? "fx") + "_tint";
		return _tints[(m, c)] = d;
	}

	private static StandardMaterial3D _dark;
	/// <summary>The dark inside of an open tin.</summary>
	public static Material Dark => _dark ??= new StandardMaterial3D { ResourceName = "fx_dark", AlbedoColor = new Color(0.06f, 0.055f, 0.05f), Roughness = 0.9f };

	/// <summary>Whether every one of <paramref name="models"/> is built (a piece in two parts goes in whole or not at all).</summary>
	public static bool Have(params string[] models)
	{
		foreach (var m in models) if (!FurnitureKit.Has(m)) return false;
		return true;
	}
}
