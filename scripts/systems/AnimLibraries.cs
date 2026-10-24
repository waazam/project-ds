using System.Collections.Generic;
using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// Retargeted animation libraries (2026-10-24, the animation overhaul): clips made by tools/Blender/retarget.py from a
/// reference library onto a house rig, exported animation-only to <c>assets/animations/&lt;character&gt;/&lt;character&gt;_anims.glb</c>
/// and imported by Godot as an <see cref="AnimationLibrary"/> (the .import's importer is "animation_library"). Their
/// tracks name the bones by the same path as the character's own model (<c>&lt;Armature&gt;/Skeleton3D:&lt;bone&gt;</c>), so the
/// library mounts straight onto the model's AnimationPlayer next to its own clips; play them as "<c>&lt;lib&gt;/&lt;clip&gt;</c>".
/// </summary>
public static class AnimLibraries
{
	private static readonly Dictionary<string, AnimationLibrary> _cache = new();
	private static readonly Dictionary<string, Dictionary<string, (float seconds, float cycle)>> _meta = new();

	/// <summary>The library's sidecar (written beside the GLB by the retargeter: each clip's length, and for a gait how far
	/// one cycle carries the character at scale 1, in metres). Exported with the game (export_presets' include filter).</summary>
	public static Dictionary<string, (float seconds, float cycle)> Meta(string glbPath)
	{
		if (_meta.TryGetValue(glbPath, out var m)) return m;
		m = new();
		string side = glbPath[..glbPath.LastIndexOf('.')] + ".json";
		if (FileAccess.FileExists(side) && Json.ParseString(FileAccess.GetFileAsString(side)).AsGodotDictionary() is { } d)
			foreach (var key in d.Keys)
			{
				var e = d[key].AsGodotDictionary();
				m[key.AsString()] = ((float)e.GetValueOrDefault("seconds", 0f).AsDouble(), (float)e.GetValueOrDefault("cycle_m", 0f).AsDouble());
			}
		_meta[glbPath] = m;
		return m;
	}

	/// <summary>The library at <paramref name="path"/> (loaded once), or null if it isn't there.</summary>
	public static AnimationLibrary Load(string path)
	{
		if (_cache.TryGetValue(path, out var lib)) return lib;
		lib = ResourceLoader.Exists(path) ? GD.Load<AnimationLibrary>(path) : null;
		_cache[path] = lib;
		return lib;
	}

	/// <summary>Mounts the library on <paramref name="player"/> as <paramref name="name"/> (once). The clips named in
	/// <paramref name="loops"/> cycle. Returns false when there's no such library.</summary>
	public static bool Attach(AnimationPlayer player, string name, string path, params string[] loops)
	{
		if (player == null) return false;
		if (player.HasAnimationLibrary(name)) return true;
		var lib = Load(path);
		if (lib == null) return false;
		foreach (var clip in loops)
			if (lib.HasAnimation(clip)) lib.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.Linear;
		player.AddAnimationLibrary(name, lib);
		return true;
	}
}
