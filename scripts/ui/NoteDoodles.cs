using System.Text.RegularExpressions;
using Godot;

namespace ProjectDS.UI;

/// <summary>
/// (2026-10-24, the owner: "some drawings thrown in with the subject matter of the note dictating if a drawing would be
/// a fun addition") Which pen doodle a note gets, from what it says: each doodle (assets/ui/doodles, drawn by
/// tools/Textures/make_doodles.py) has words that call for it, strong ones worth 2 and weak ones 1, and the best
/// scorer wins if it scores 2 or more, so a note has to be about the thing, not just mention it in passing. Only the
/// hand-written get one (a typed log or a printed card doesn't); a Readable's or a page's own Doodle overrides it
/// ("none" for none).
/// </summary>
public static class NoteDoodles
{
	private static readonly (string name, string[] strong, string[] weak)[] Subjects =
	{
		("scribble_head", new[] { "monster", "monsters", "face", "faces", "teeth", "grin", "grinning", "smile", "smiling", "nightmare", "nightmares", "in my head" }, new[] { "thing", "scream", "screaming", "dream", "dreams" }),
		("eye", new[] { "watching", "watched", "watches", "eyes", "eye", "staring", "stared", "stare" }, new[] { "saw", "seen", "looking", "look" }),
		("antlers", new[] { "wendigo", "antler", "antlers", "skull", "deer", "hunger", "hungry", "starving" }, new[] { "bones", "bone", "eat", "ate", "meat" }),
		("stairs", new[] { "stairs", "staircase", "stairway", "steps" }, new[] { "climb", "climbed", "up" }),
		("figure", new[] { "stranger", "tall man", "the man", "figure", "someone standing", "standing there" }, new[] { "him", "someone", "tall" }),
		("trail_map", new[] { "map", "north", "compass", "trail", "path", "route", "bridge", "river", "lake" }, new[] { "miles", "mile", "left", "right", "east", "west", "south" }),
		("pines", new[] { "woods", "forest", "trees", "pines" }, new[] { "tree", "wind" }),
		("cabin", new[] { "cabin", "lodge", "home", "house" }, new[] { "roof", "porch" }),
		("moon", new[] { "moon", "stars", "midnight", "tonight" }, new[] { "night", "dark" }),
		("tally", new[] { "days", "count", "counted", "counting", "weeks" }, new[] { "since", "hours", "day", "week" }),
		("footprints", new[] { "tracks", "footprints", "prints", "barefoot", "followed", "following", "follow" }, new[] { "snow", "walked" }),
		("flame", new[] { "fire", "burn", "burned", "burnt", "burning", "smoke", "ashes", "matches" }, new[] { "warm", "heat" }),
		("camera", new[] { "camera", "photo", "photos", "photograph", "photographs", "picture", "pictures", "film" }, new[] { "develop", "developed" }),
		("spiral", new[] { "dizzy", "lost", "circles", "round and round", "again and again", "spiral" }, new[] { "again" }),
		("key", new[] { "key", "keys", "lock", "locked", "keycard" }, new[] { "door" }),
		("heart", new[] { "love", "miss you", "sorry", "forgive", "darling", "mom", "mum" }, new[] { "dear", "friend" }),
	};

	private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _cache = new();

	/// <summary>The doodle for a note, or null for none.</summary>
	public static Texture2D For(string text, string title, string own, Systems.Readable.NoteStyle style)
	{
		string name = own;
		if (string.IsNullOrEmpty(name))
		{
			if (style is Systems.Readable.NoteStyle.Typed or Systems.Readable.NoteStyle.Printed) return null;
			name = Pick(((title ?? "") + " " + (text ?? "")).ToLowerInvariant());
		}
		if (string.IsNullOrEmpty(name) || name == "none") return null;
		if (_cache.TryGetValue(name, out var t)) return t;
		t = ResourceLoader.Exists($"res://assets/ui/doodles/{name}.png") ? GD.Load<Texture2D>($"res://assets/ui/doodles/{name}.png") : null;
		_cache[name] = t;
		return t;
	}

	/// <summary>The best-scoring subject's doodle (2 or more), or "".</summary>
	public static string Pick(string lower)
	{
		string best = "";
		int bestScore = 1;
		foreach (var (name, strong, weak) in Subjects)
		{
			int score = 0;
			foreach (var w in strong) score += 2 * Count(lower, w);
			foreach (var w in weak) score += Count(lower, w);
			if (score > bestScore) { best = name; bestScore = score; }
		}
		return best;
	}

	private static int Count(string lower, string word) => Regex.Matches(lower, $@"\b{Regex.Escape(word)}\b").Count;
}
