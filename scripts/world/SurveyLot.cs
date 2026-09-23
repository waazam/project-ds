using System.Collections.Generic;
using System.Threading;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// R.H.'s four notes on the trees (Dan, 2026-09-22: notes nailed to trunks beside the path, the
/// number written on each, instead of survey stakes): the bunker door's code, one digit per note in
/// order, two on the way from the camp to the cabin and two on the way from the lookout to the
/// bunker, a stride off the path edge and facing it (<see cref="TreeNote"/>). Placement is by trail
/// distance between the landmarks (<see cref="NoteFractions"/>), so the scene's node transform does
/// not matter. The camp note mentions the four-number door, so the HUD tracker shows from there.
///
/// Tracking: every digit read is saved as <see cref="StoryManager.Flag.CodeDigit"/>(n) at once and
/// the digit is shown big on screen for a moment; <see cref="Known"/> is the "4 _ 2 _" the HUD shows
/// (<see cref="UI.CodeLockOverlay"/>) and the dial pre-fills. Each digit found turns the stalker's
/// clicking up a notch (Urgency) until the door is open. Group "survey_lot" (the name the bunker's
/// dial and the HUD look up; kept).
/// </summary>
[GlobalClass]
public partial class SurveyLot : Node3D
{
	/// <summary>The four-digit code, one digit per note in order.</summary>
	[Export] public string Code = "4729";
	/// <summary>Where each note's tree stands, as a fraction of its stretch: 0-1 of camp->cabin for the first two,
	/// 0-1 of lookout->bunker for the last two.</summary>
	[Export] public float[] NoteFractions = { 0.35f, 0.7f, 0.35f, 0.7f };
	/// <summary>Metres off the path edge (alternating sides).</summary>
	[Export] public float SideOffset = 1.6f;

	public IReadOnlyList<TreeNote> Notes => _notes;
	private readonly List<TreeNote> _notes = new();
	private Entities.Stalker _stalker;
	private bool _stalkerSearched;

	/// <summary>Notes read so far, for the pressure on the player.</summary>
	public int ReadCount { get { int n = 0; foreach (var s in _notes) if (s.Read) n++; return n; } }
	/// <summary>The code as the player knows it: found digits in place, "_" for the rest ("4_2_").</summary>
	public string Known
	{
		get
		{
			var c = new char[Code.Length];
			for (int i = 0; i < Code.Length; i++) c[i] = i < _notes.Count && _notes[i].Read ? Code[i] : '_';
			return new string(c);
		}
	}
	/// <summary>For tests: the trail metres each note's tree was placed at.</summary>
	public IReadOnlyList<float> TrailMetres => _trailMetres;
	private readonly List<float> _trailMetres = new();

	/// <summary>Every digit found turns the stalker's clicking up a notch (see <see cref="Entities.Stalker.Urgency"/>),
	/// until the door is open; then it lets go.</summary>
	public override void _Process(double delta)
	{
		if (!_stalkerSearched) { _stalkerSearched = true; _stalker = GetTree().GetFirstNodeInGroup("stalker") as Entities.Stalker; }
		if (_stalker == null || !IsInstanceValid(_stalker)) return;
		bool open = StoryManager.Instance is { } s && (s.HasFlag(StoryManager.Flag.BunkerUnlocked) || s.Current >= Checkpoint.Act8BunkerEntered);
		_stalker.Urgency = open || _notes.Count == 0 ? 0f : ReadCount / (float)_notes.Count;
	}

	public override void _Ready()
	{
		AddToGroup("survey_lot");
		// The stretches, in trail metres: camp -> cabin and lookout -> bunker.
		var terrain = GroundSnap.FindTerrain(this);
		float S(Node3D n, float fallback) { if (n == null || terrain == null) return fallback; terrain.TrailDistance(n.GlobalPosition.X, n.GlobalPosition.Z, out float s); return s; }
		var scene = GetTree().CurrentScene;
		var camp = scene?.FindChild("Camp", true, false) as Node3D;
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Node3D;
		var lookout = GetTree().GetFirstNodeInGroup("fire_lookout_marker") as Node3D;
		var bunker = GetTree().GetFirstNodeInGroup("bunker_marker") as Node3D;
		float sCamp = S(camp, 60f), sCabin = S(cabin, 430f), sLookout = S(lookout, 720f), sBunker = S(bunker, 860f);
		for (int i = 0; i < 4 && i < Code.Length; i++)
		{
			float frac = i < NoteFractions.Length ? NoteFractions[i] : 0.5f;
			float s = i < 2 ? Mathf.Lerp(sCamp, sCabin, frac) : Mathf.Lerp(sLookout, sBunker, frac);
			_trailMetres.Add(s);
			var note = new TreeNote { Name = $"Note{i + 1}", Label = $"{i + 1}", Digit = Code[i] - '0' };
			AddChild(note);
			_notes.Add(note);
			if (terrain != null)
			{
				Vector3 p = terrain.TrailPoint(s, out Vector3 t);
				Vector3 right = t.Cross(Vector3.Up).Normalized();
				float side = (i & 1) == 0 ? 1f : -1f;
				Vector3 at = p + right * (SideOffset * side);
				at.Y = terrain.HeightAt(at.X, at.Z);
				// The sheet (the trunk's +Z) faces the path, turned a little toward the walker coming up the trail.
				Vector3 lookFrom = p - t.Normalized() * 3f;
				note.GlobalTransform = new Transform3D(Basis.LookingAt(Flat(at - lookFrom), Vector3.Up), at);
				var cz = new ClearZone { Name = $"Clear{i + 1}", Radius = 2.2f, ClearFoliage = true };
				AddChild(cz);
				cz.GlobalPosition = at;
			}
			// Restore: read in the saved story.
			int n = i + 1;
			if (StoryManager.Instance is { } sm && sm.HasFlag(StoryManager.Flag.CodeDigit(n))) note.MarkRead();
			note.FirstRead += () => OnNoteRead(n);
		}
		GD.Print($"[story] the notes on the trees at trail m: {string.Join(", ", _trailMetres.ConvertAll(m => m.ToString("0")))}");
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

	private void OnNoteRead(int n)
	{
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(StoryManager.Flag.CodeDigit(n))) return;
		s.SetFlag(StoryManager.Flag.CodeDigit(n));
		GD.Print($"[story] code digit {n} read: {Known}");
		// The number, big, for a moment: it is his note, not a thought.
		_ = StoryBeat.Caption(this, Code[n - 1].ToString(), 0.1f, 0.9f, 0.5f, CancellationToken.None);
	}
}
