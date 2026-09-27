using System.Collections.Generic;
using System.Threading;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// R.H.'s four numbers on the trees: the bunker door's code, one digit per tree in order, two on the
/// way from the camp to the cabin and two on the way from the lookout to the bunker. After the owner's
/// blacklight mechanic, they are brushed in ink that only the lantern's blacklight shows
/// (<see cref="TreeNote"/>, <see cref="UvInk"/>): a few metres off the path, on any side of the trunk.
/// Round each real one stand decoy trees with marks that aren't his, the ground round them is marked
/// too, and more marks are scattered along both stretches, so the player has to look closely. Bare
/// footprints in the same ink lead from the lookout to the bunker's door. Placement is by trail distance
/// between the landmarks (<see cref="NoteFractions"/>), so the scene's node transform doesn't matter.
/// The camp note tells of the ink, and the HUD tracker shows from there.
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
	[Export] public float SideOffset = 3.4f;
	/// <summary>Decoy trees round each real one.</summary>
	[Export] public int DecoysPerNote = 3;
	/// <summary>For tests: the decoy trees, and how many UV marks lie on the ground (the footprints separately).</summary>
	public IReadOnlyList<TreeNote> Decoys => _decoys;
	private readonly List<TreeNote> _decoys = new();
	public int GroundMarks { get; private set; }
	public int Footprints { get; private set; }
	/// <summary>For tests: the footprint trail's start and end (world).</summary>
	public Vector3 FootprintsStart { get; private set; }
	public Vector3 FootprintsEnd { get; private set; }

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
			var rng = new RandomNumberGenerator { Seed = (ulong)(4400 + i * 17) };
			// the number is a little way round the trunk from the path side: it has to be looked for
			float markAngle = (rng.Randf() < 0.5f ? -1f : 1f) * rng.RandfRange(0.5f, 1.2f);
			var note = new TreeNote { Name = $"Note{i + 1}", Label = $"{i + 1}", Digit = Code[i] - '0', MarkAngle = markAngle };
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
				// the decoy trees round it, off the path, facing it the same way
				for (int d = 0; d < DecoysPerNote; d++)
				{
					float a = rng.RandfRange(0f, Mathf.Tau), r = rng.RandfRange(3.2f, 6.5f);
					Vector3 dp = at + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
					if (terrain.RouteDistance(dp.X, dp.Z) < 2.4f) dp = at + right * (side * r);   // never on the path
					dp.Y = terrain.HeightAt(dp.X, dp.Z);
					var decoy = new TreeNote { Name = $"Decoy{i + 1}_{d}", Label = $"d{i}{d}", Digit = Code[i] - '0', Decoy = true, MarkAngle = rng.RandfRange(-1.5f, 1.5f), TrunkRadius = rng.RandfRange(0.24f, 0.36f), TrunkHeight = rng.RandfRange(5.5f, 8f) };
					AddChild(decoy);
					decoy.GlobalTransform = new Transform3D(Basis.LookingAt(Flat(dp - lookFrom), Vector3.Up), dp);
					_decoys.Add(decoy);
					var dz = new ClearZone { Name = $"ClearD{i}{d}", Radius = 1.6f, ClearFoliage = false };
					AddChild(dz);
					dz.GlobalPosition = dp;
				}
				// marks on the ground round them
				for (int g = 0; g < 7; g++)
				{
					float a = rng.RandfRange(0f, Mathf.Tau), r = rng.RandfRange(0.8f, 5.5f);
					Vector3 gp = at + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
					GroundMark(terrain, gp, rng, Code[i] - '0');
				}
			}
			// Restore: read in the saved story.
			int n = i + 1;
			if (StoryManager.Instance is { } sm && sm.HasFlag(StoryManager.Flag.CodeDigit(n))) note.MarkRead();
			note.FirstRead += () => OnNoteRead(n);
		}
		if (terrain != null)
		{
			// marks scattered along both stretches, now and then, a little off the path
			var rngS = new RandomNumberGenerator { Seed = 4477 };
			foreach (var (a, b) in new[] { (sCamp + 10f, sCabin - 10f), (sLookout + 10f, sBunker - 6f) })
				for (float s = a; s < b; s += rngS.RandfRange(9f, 17f))
				{
					Vector3 p = terrain.TrailPoint(s, out Vector3 t);
					Vector3 right = t.Cross(Vector3.Up).Normalized();
					GroundMark(terrain, p + right * rngS.RandfRange(-3.5f, 3.5f), rngS, -1);
				}
			// bare footprints in the ink, from the lookout to the bunker's door: someone walked it, barefoot
			BuildFootprints(terrain, sLookout + 4f, sBunker - 1.5f);
		}
		GD.Print($"[story] the blacklight numbers on the trees at trail m: {string.Join(", ", _trailMetres.ConvertAll(m => m.ToString("0")))} ({_decoys.Count} decoy trees, {GroundMarks} ground marks, {Footprints} footprints)");
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

	/// <summary>Lies flat on the ground's slope there.</summary>
	private static Basis GroundBasis(ForestTerrain terrain, Vector3 p, float yaw)
	{
		float e = 0.4f;
		float hx = terrain.HeightAt(p.X + e, p.Z) - terrain.HeightAt(p.X - e, p.Z), hz = terrain.HeightAt(p.X, p.Z + e) - terrain.HeightAt(p.X, p.Z - e);
		Vector3 n = new Vector3(-hx, 2f * e, -hz).Normalized();
		Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
		Vector3 x = n.Cross(fwd).Normalized(), z = x.Cross(n).Normalized();
		return new Basis(x, n, z);
	}

	/// <summary>One mark on the ground: a smear, a hand, a single print, a struck-out number.</summary>
	private void GroundMark(ForestTerrain terrain, Vector3 p, RandomNumberGenerator rng, int avoidDigit)
	{
		p.Y = terrain.HeightAt(p.X, p.Z);
		int kind = rng.RandiRange(0, 9);
		Texture2D tex = kind switch { 0 or 1 => UvInk.Hand(), 2 or 3 => UvInk.Foot(), 4 => UvInk.Digit((avoidDigit + 5 + rng.RandiRange(0, 3)) % 10, true), _ => UvInk.Smear(rng.RandiRange(0, 20)) };
		float size = kind is 2 or 3 ? 0.28f : rng.RandfRange(0.3f, 0.6f);
		var mi = UvInk.OnGround(this, p, rng.RandfRange(0f, Mathf.Tau), new Vector2(size, kind is 2 or 3 ? size * 2f : size), tex, rng.Randf() < 0.35f ? UvInk.Green : UvInk.Pale);
		mi.GlobalTransform = new Transform3D(GroundBasis(terrain, p, rng.RandfRange(0f, Mathf.Tau)), p + Vector3.Up * 0.02f);
		GroundMarks++;
	}

	/// <summary>Footprints along the trail between two trail distances: a slightly wandering walk, left and right.</summary>
	private void BuildFootprints(ForestTerrain terrain, float s0, float s1)
	{
		var left = new List<Transform3D>();
		var rightFeet = new List<Transform3D>();
		var rng = new RandomNumberGenerator { Seed = 4488 };
		const float stride = 0.72f, half = 0.13f;
		bool l = true;
		for (float s = s0; s < s1; s += stride * rng.RandfRange(0.9f, 1.1f))
		{
			Vector3 p = terrain.TrailPoint(s, out Vector3 t);
			Vector3 fwd = Flat(t).Normalized();
			Vector3 side = fwd.Cross(Vector3.Up).Normalized();
			float wander = 0.7f * Mathf.Sin(s * 0.09f) + 0.25f * Mathf.Sin(s * 0.31f + 1f);
			Vector3 at = p + side * (wander + (l ? -half : half));
			at.Y = terrain.HeightAt(at.X, at.Z);
			float yaw = Mathf.Atan2(fwd.X, fwd.Z) + rng.RandfRange(-0.12f, 0.12f) + (l ? 0.08f : -0.08f);
			var basis = GroundBasis(terrain, at, yaw);
			// a left foot is the right one mirrored
			if (l) basis = basis * Basis.FromScale(new Vector3(-1f, 1f, 1f));
			(l ? left : rightFeet).Add(new Transform3D(basis, at + basis.Y * 0.02f));
			if (s == s0) FootprintsStart = at;
			FootprintsEnd = at;
			l = !l;
		}
		UvInk.Scatter(this, "FootprintsL", left, new Vector2(0.13f, 0.27f), UvInk.Foot(), UvInk.Cyan, 1.3f);
		UvInk.Scatter(this, "FootprintsR", rightFeet, new Vector2(0.13f, 0.27f), UvInk.Foot(), UvInk.Cyan, 1.3f);
		Footprints = left.Count + rightFeet.Count;
	}

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
