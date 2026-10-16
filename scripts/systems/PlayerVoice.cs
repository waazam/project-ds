using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.UI;

namespace ProjectDS.Systems;

/// <summary>
/// The player's own spoken lines (STORY.md Rev 5: short reactions aloud, like Ethan Winters). For now each line is
/// shown as text in the spoken style; when Dan records it, drop the clip in at
/// <c>res://assets/audio/voice/player/&lt;id&gt;.wav</c> (or .ogg/.mp3) and it plays with the text, dry and close on
/// the Player bus. Every line the player says goes through <see cref="Say"/> with an id from <see cref="Lines"/>,
/// so the recording list is this table.
/// </summary>
public static class PlayerVoice
{
	/// <summary>Every line, by id. Keep it in story order; docs/STORY.md lists the same lines.</summary>
	public static readonly IReadOnlyDictionary<string, string> Lines = new Dictionary<string, string>
	{
		["wake"] = "Where the fuck am I? ... Was I... floating? Up those... stairs?",
		["key_piece_1"] = "Half of a key.",
		["key_piece_2"] = "The other half of the key.",
		["who_are_you"] = "Who are you!",
		["dads_rowboat"] = "Dad's rowboat?",
		["thats_mine"] = "That's... mine.",
		["dad_what_happened"] = "Dad... what happened here?",
		["dad"] = "Dad?",
		["rest_now"] = "You can rest now, Dad.",
	};

	/// <summary>Where the recordings go, one file per id.</summary>
	public const string ClipDir = "res://assets/audio/voice/player/";

	/// <summary>The recorded clip for a line, or null while it is text only.</summary>
	public static string ClipFor(string id)
	{
		foreach (var ext in new[] { ".wav", ".ogg", ".mp3" })
			if (ResourceLoader.Exists(ClipDir + id + ext)) return ClipDir + id + ext;
		return null;
	}

	/// <summary>
	/// Say a line: its clip if there is one (non-positional, Player bus), and its text in the spoken style while play
	/// goes on. <paramref name="hold"/> is how long the text stays up (seconds); a recorded clip holds it at least as
	/// long as the clip runs.
	/// </summary>
	public static async Task Say(Node from, string id, float hold = 2.4f, CancellationToken ct = default)
	{
		if (!Lines.TryGetValue(id, out var text)) { GD.PushWarning($"[voice] no player line '{id}'"); return; }
		GD.Print($"[voice] player says {id}");
		var clip = ClipFor(id);
		if (clip != null && from != null && from.IsInsideTree())
		{
			var stream = GD.Load<AudioStream>(clip);
			var p = new AudioStreamPlayer { Name = "PlayerLine", Stream = stream, Bus = "Player", VolumeDb = 0f };
			from.GetTree().CurrentScene?.AddChild(p);
			p.Finished += p.QueueFree;
			p.Play();
			hold = Mathf.Max(hold, (float)stream.GetLength());
		}
		if (Subtitle.Instance != null) await Subtitle.Instance.Show("\"" + text + "\"", 0.2f, hold, 0.8f, ct);
	}
}
