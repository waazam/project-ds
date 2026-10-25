using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// R.H.'s way to the bunker's key (2026-10-15, the owner: "instead of finding numbers for the keypad on the bunker, lets add
/// 2 pieces of a key that need to be found by following arrows on trees using the blacklight ... somewhat obvious to use the
/// blacklight but not too obvious. and the arrows should not be too easy to follow but not impossibly hard"). It replaces
/// the four code numbers on the trees.
///
/// Two trails of arrows brushed in the blacklight's ink (<see cref="TreeNote.Arrow"/>, <see cref="UvInk.Arrow"/>), each
/// leaving the path at a fixed spot and running off through the woods, <see cref="TreesPerTrail"/> bare trunks
/// <see cref="StepMin"/>-<see cref="StepMax"/> m apart, never in a straight line (two or three real turns), every arrow
/// pointing at the next tree, to a hiding place with half of the key in a tin (<see cref="KeyPieceCache"/>):
/// <list type="bullet">
/// <item>half 1, off the walk from the camp to the cabin (Act 3, the first stretch with the lantern), in a hollow stump;</item>
/// <item>half 2, off the walk from the lookout to the bunker (Act 7), under the lintel stone of a cairn.</item>
/// </list>
/// The ink shows only under the beam and fades past ~6 m, so each next tree has to be looked for in the arrow's
/// direction; under the flame a trail is dead trees. The one sign in plain light is on the first tree of each, by the path:
/// an old smear of the paint and a knife's notch (<see cref="TreeNote.Trace"/>). A few decoy trees with marks that aren't
/// his stand round each start and by each end. R.H.'s camp note tells of the key and the light (<see cref="Camp.NoteText"/>);
/// near the first arrow, with the blacklight off, one nudge under the picture names its key, once.
/// The trails are laid by trail distance between the landmarks and by the ground (off the path, the stream, the clear zones,
/// the edge), seeded, so they are the same every game.
///
/// Kept from the numbers: the scattered ground marks along both stretches and the bare footprints from the lookout to the
/// bunker's door. Each half found turns the stalker's clicking up a notch (Urgency) until the door is open. Group
/// "survey_lot" (the name is kept: the bunker and the tests look it up).
/// </summary>
[GlobalClass]
public partial class SurveyLot : Node3D
{
	/// <summary>Where each trail leaves the path, as a fraction of its stretch: camp -> cabin, then lookout -> bunker.</summary>
	[Export] public float[] BranchFractions = { 0.45f, 0.5f };
	[Export] public int TreesPerTrail = 7;
	[Export] public float StepMin = 11f, StepMax = 15f;
	/// <summary>(2026-10-23, the owner: "finding the key to the bunker is way too hard I looked everywhere and couldnt find it.
	/// add more blacklight clues") Decoy trees round the trails (off: they only hid the real ones).</summary>
	[Export] public bool DecoyTrees = false;
	/// <summary>For tests: the decoy trees, and how many UV marks lie on the ground (the footprints separately).</summary>
	public IReadOnlyList<TreeNote> Decoys => _decoys;
	private readonly List<TreeNote> _decoys = new();
	public int GroundMarks { get; private set; }
	public int Footprints { get; private set; }
	/// <summary>For tests: the footprint trail's start and end (world).</summary>
	public Vector3 FootprintsStart { get; private set; }
	public Vector3 FootprintsEnd { get; private set; }

	/// <summary>Every arrow tree, both trails in order.</summary>
	public IReadOnlyList<TreeNote> Notes => _notes;
	private readonly List<TreeNote> _notes = new();
	/// <summary>The arrow trees of trail <paramref name="piece"/> (1 or 2), from the path to the cache.</summary>
	public IReadOnlyList<TreeNote> Trail(int piece) { int i = _caches.FindIndex(c => c.Piece == piece); return i >= 0 ? _trails[i] : System.Array.Empty<TreeNote>(); }
	private readonly List<List<TreeNote>> _trails = new();
	/// <summary>The hiding place of half <paramref name="piece"/> (1 or 2), or null if it couldn't be laid.</summary>
	public KeyPieceCache Cache(int piece) => _caches.Find(c => c.Piece == piece);
	private readonly List<KeyPieceCache> _caches = new();
	/// <summary>The lean-to past the lookout (<see cref="RestShelter"/>).</summary>
	public RestShelter Shelter { get; private set; }
	/// <summary>For tests and the report: the trail metres each trail leaves the path at, and each trail's walk (m).</summary>
	public IReadOnlyList<float> TrailMetres => _trailMetres;
	private readonly List<float> _trailMetres = new();
	private readonly List<float> _trailLengths = new();
	/// <summary>Trail <paramref name="piece"/>'s walk, from the path past every tree to the cache (m; 0 if not laid).</summary>
	public float TrailLength(int piece) { int i = _caches.FindIndex(c => c.Piece == piece); return i >= 0 ? _trailLengths[i] : 0f; }
	private Entities.Stalker _stalker;
	private bool _stalkerSearched;

	/// <summary>Halves taken so far.</summary>
	public static int PiecesFound
	{
		get
		{
			if (StoryManager.Instance is not { } s) return 0;
			return (s.HasFlag(StoryManager.Flag.KeyPiece(1)) ? 1 : 0) + (s.HasFlag(StoryManager.Flag.KeyPiece(2)) ? 1 : 0);
		}
	}

	public override void _Process(double delta)
	{
		Nudge((float)delta);
		if (!_stalkerSearched) { _stalkerSearched = true; _stalker = GetTree().GetFirstNodeInGroup("stalker") as Entities.Stalker; }
		if (_stalker == null || !IsInstanceValid(_stalker)) return;
		bool open = StoryManager.Instance is { } s && (s.HasFlag(StoryManager.Flag.BunkerUnlocked) || s.Current >= Checkpoint.Act8BunkerEntered);
		_stalker.Urgency = open ? 0f : PiecesFound / 2f;
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
		if (terrain != null)
		{
			// the places a trail mustn't run through: the landmarks, and every clear zone already standing (the cabin's, the
			// bridge's, the props')
			var avoid = new List<(Vector3 p, float r)>();
			foreach (var n in new[] { camp, cabin, lookout, bunker }) if (n != null) avoid.Add((n.GlobalPosition, 22f));
			foreach (var n in GetTree().GetNodesInGroup("clear_zones")) if (n is ClearZone cz && cz.IsInsideTree()) avoid.Add((cz.GlobalPosition, cz.Radius + 4f));
			// (2026-10-24, the game-loop pass) the lean-to past the lookout: a fire to rest by before the bunker
			if (lookout != null && bunker != null)
			{
				// on the straight way from the lookout to the bunker (the compass's way: the trail wanders north first), a quarter
				// of the way along, a few steps to the side
				Vector3 lo = lookout.GlobalPosition, bu = bunker.GlobalPosition;
				Vector3 dir = Flat(bu - lo).Normalized();
				Vector3 right = dir.Cross(Vector3.Up).Normalized();
				Vector3 on = lo + Flat(bu - lo) * 0.27f;
				Vector3 at = terrain.GroundPoint((on + right * 6f).X, (on + right * 6f).Z);
				var xf = new Transform3D(Basis.LookingAt(right, Vector3.Up), at);   // (its fire, its +Z, toward the path)
				Shelter = new RestShelter { Name = "RestShelter", Transform = GetParent() is Node3D host ? host.GlobalTransform.AffineInverse() * xf : xf };
				GetParent().CallDeferred(Node.MethodName.AddChild, Shelter);   // (the level's still setting up its children)
				var level = GetParent();
				Callable.From(() => FilmGhost.HauntHollow(level)).CallDeferred();   // (what only the camera sees)
				avoid.Add((at, 10f));
				// its clearing now, before the woods are scattered (they read the clear zones in their own deferred build)
				var clear = new ClearZone { Name = "ShelterClear", Radius = 5f };
				AddChild(clear);
				clear.GlobalPosition = at;
				GD.Print($"[story] the lean-to past the lookout: at {at.X:0.0},{at.Y:0.0},{at.Z:0.0}, facing the way to the bunker");
			}
			for (int piece = 1; piece <= 2; piece++)
			{
				float frac = piece - 1 < BranchFractions.Length ? BranchFractions[piece - 1] : 0.5f;
				float s = piece == 1 ? Mathf.Lerp(sCamp, sCabin, frac) : Mathf.Lerp(sLookout, sBunker, frac);
				if (LayTrail(terrain, piece, s, avoid, out var trees, out var cache, out float branchS))
				{
					_trailMetres.Add(branchS);
					BuildTrail(terrain, piece, trees, cache, branchS);
					foreach (var t in trees) avoid.Add((t, 14f));
					avoid.Add((cache, 14f));
				}
				else GD.PushWarning($"[story] key trail {piece}: no way off the path found near trail m {s:0}");
			}
			// marks scattered along both stretches, now and then, a little off the path
			var rngS = new RandomNumberGenerator { Seed = 4477 };
			foreach (var (a, b) in new[] { (sCamp + 10f, sCabin - 10f), (sLookout + 10f, sBunker - 6f) })
				for (float s = a; s < b; s += rngS.RandfRange(9f, 17f))
				{
					Vector3 p = terrain.TrailPoint(s, out Vector3 t);
					Vector3 right = t.Cross(Vector3.Up).Normalized();
					GroundMark(terrain, p + right * rngS.RandfRange(-3.5f, 3.5f), rngS);
				}
			// bare footprints in the ink, from the lookout to the bunker's door: someone walked it, barefoot
			BuildFootprints(terrain, sLookout + 4f, sBunker - 1.5f);
		}
		var where = new List<string>();
		for (int i = 0; i < _caches.Count; i++) where.Add($"{_caches[i].Piece}: off trail m {_trailMetres[i]:0}, {_trails[i].Count} arrows, {_trailLengths[i]:0} m walk, cache at {_caches[i].GlobalPosition.X:0},{_caches[i].GlobalPosition.Z:0}");
		GD.Print($"[story] the key's arrow trails: {string.Join("; ", where)} ({_decoys.Count} decoy trees, {GroundMarks} ground marks, {Footprints} footprints)");
	}

	private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
	private static Vector3 Turn(Vector3 h, float a) => new Basis(Vector3.Up, a) * h;

	/// <summary>Finds a trail: a first tree a few metres off the path at about <paramref name="s"/>, then each next tree a
	/// step on with the heading turning now and then (two or three real turns of 35-60 degrees, alternating), every tree
	/// clear of the path, the stream, the edge, the <paramref name="avoid"/> places and its own trail's earlier trees, on
	/// ground not too steep; the cache a few metres past the last. Seeded: the same every game.</summary>
	private bool LayTrail(ForestTerrain terrain, int piece, float s, List<(Vector3 p, float r)> avoid, out List<Vector3> trees, out Vector3 cache, out float branchS)
	{
		trees = new List<Vector3>();
		cache = Vector3.Zero;
		branchS = s;
		var play = terrain.PlayRect.Grow(-22f);
		bool Ok(Vector3 p, float minTrail, List<Vector3> mine, Vector3? from)
		{
			if (!play.HasPoint(new Vector2(p.X, p.Z))) return false;
			terrain.SampleFields(p.X, p.Z, out float dT, out _, out float dS, out float dR);
			if (dT < minTrail || dS < 7f || dR < 12f) return false;
			if (terrain.SampleBranch(p.X, p.Z, out _) < 6f) return false;
			foreach (var (q, r) in avoid) if (Flat(q - p).Length() < r) return false;
			for (int i = 0; i < mine.Count - 1; i++) if (Flat(mine[i] - p).Length() < 9f) return false;
			if (from is { } f)
			{
				float h0 = terrain.HeightAt(f.X, f.Z), h1 = terrain.HeightAt(p.X, p.Z);
				if (Mathf.Abs(h1 - h0) / Mathf.Max(1f, Flat(p - f).Length()) > 0.45f) return false;
				// the way between mustn't cut back across the path or the stream either
				Vector3 m = (f + p) * 0.5f;
				terrain.SampleFields(m.X, m.Z, out float mT, out _, out float mS, out _);
				if (mT < minTrail - 2f || mS < 5f) return false;
			}
			return true;
		}
		for (int attempt = 0; attempt < 60; attempt++)
		{
			var rng = new RandomNumberGenerator { Seed = (ulong)(7100 + piece * 1000 + attempt * 37) };
			float sAt = s + (attempt < 6 ? 0f : rng.RandfRange(-25f, 25f));
			float side = ((attempt + piece) & 1) == 0 ? 1f : -1f;
			Vector3 p = terrain.TrailPoint(sAt, out Vector3 t);
			Vector3 fwd = Flat(t).Normalized();
			Vector3 off = fwd.Cross(Vector3.Up).Normalized() * side;
			var mine = new List<Vector3>();
			// the first tree: a few metres off the path's edge, the trace on it toward the path
			Vector3 first = p + off * rng.RandfRange(5.5f, 7f) + fwd * rng.RandfRange(-1.5f, 1.5f);
			if (!Ok(first, 4.5f, mine, null)) continue;
			mine.Add(first);
			// away from the path, slanting on along it or back
			Vector3 heading = Turn(off, side * rng.RandfRange(-0.5f, 0.5f));
			// which steps turn: two or three of them, never two in a row, alternating left and right
			var turns = new float[TreesPerTrail];
			int nTurns = rng.RandiRange(2, 3);
			float sign = rng.Randf() < 0.5f ? -1f : 1f;
			var picks = new List<int>();
			for (int tries = 0; tries < 40 && picks.Count < nTurns; tries++)
			{
				int k = rng.RandiRange(1, TreesPerTrail - 1);
				if (picks.Contains(k) || picks.Contains(k - 1) || picks.Contains(k + 1)) continue;
				picks.Add(k);
			}
			picks.Sort();
			foreach (int k in picks) { turns[k] = sign * Mathf.DegToRad(rng.RandfRange(35f, 60f)); sign = -sign; }
			bool failed = false;
			int realTurns = 0;
			for (int i = 1; i < TreesPerTrail && !failed; i++)
			{
				Vector3 prev = mine[^1];
				bool placed = false;
				// as planned first, then the other way, then gentler, before giving up on this attempt
				foreach (float a in new[] { turns[i], -turns[i], turns[i] * 0.5f, turns[i] == 0f ? 0.6f : 0f, turns[i] == 0f ? -0.6f : 0f })
				{
					Vector3 h = Turn(heading, a + rng.RandfRange(-0.12f, 0.12f));
					Vector3 c = prev + h * rng.RandfRange(StepMin, StepMax);
					if (!Ok(c, 9f, mine, prev)) continue;
					c.Y = terrain.HeightAt(c.X, c.Z);
					mine.Add(c);
					heading = h;
					if (Mathf.Abs(a) >= Mathf.DegToRad(30f)) realTurns++;
					placed = true;
					break;
				}
				failed = !placed;
			}
			if (failed || realTurns < 2) continue;
			// the cache: a little past the last tree, a little to one side
			Vector3 last = mine[^1];
			bool cached = false;
			foreach (float a in new[] { 0.25f, -0.25f, 0.6f, -0.6f })
			{
				Vector3 c = last + Turn(heading, a) * rng.RandfRange(6.5f, 8.5f);
				if (!Ok(c, 9f, mine, last)) continue;
				// (2026-10-23, the owner: the cairn hung in the air) on the ground here, not at the last tree's height
				c.Y = terrain.HeightAt(c.X, c.Z);
				cache = c;
				cached = true;
				break;
			}
			if (!cached) continue;
			first.Y = terrain.HeightAt(first.X, first.Z);
			mine[0] = first;
			trees = mine;
			terrain.TrailDistance(first.X, first.Z, out branchS);
			GD.Print($"[story] key trail {piece}: laid on attempt {attempt + 1} ({realTurns} turns)");
			return true;
		}
		return false;
	}

	/// <summary>Builds a laid trail: the arrow trees (each faces the one before it, or the path for the first, its arrow turned
	/// toward the next), the cache, clear zones round them, a couple of decoy trees by the start and one by the end.</summary>
	private void BuildTrail(ForestTerrain terrain, int piece, List<Vector3> at, Vector3 cachePos, float branchS)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(5300 + piece * 71) };
		Vector3 pathPoint = terrain.TrailPoint(branchS, out _);
		var trail = new List<TreeNote>();
		float walk = Flat(at[0] - pathPoint).Length();
		for (int i = 0; i < at.Count; i++)
		{
			Vector3 from = i == 0 ? pathPoint : at[i - 1];
			Vector3 next = i + 1 < at.Count ? at[i + 1] : cachePos;
			if (i > 0) walk += Flat(at[i] - at[i - 1]).Length();
			// the trunk's +Z toward the one coming from the last tree (or the path); the arrow a little way round from there
			var basis = Basis.LookingAt(Flat(at[i] - from), Vector3.Up);
			float markAngle = rng.RandfRange(-0.4f, 0.4f);
			// the arrow's turn: where the next tree lies, as seen by someone facing the mark
			Vector3 d = basis * new Vector3(Mathf.Sin(markAngle), 0, Mathf.Cos(markAngle));
			Vector3 look = -Flat(d).Normalized();
			Vector3 right = look.Cross(Vector3.Up).Normalized();
			Vector3 to = Flat(next - at[i]).Normalized();
			float turn = -Mathf.Atan2(to.Dot(right), to.Dot(look));
			var note = new TreeNote
			{
				Name = $"Arrow{piece}_{i + 1}", Label = $"a{piece}{i + 1}", Arrow = true, ArrowTurn = turn, Trace = i == 0, Notch = i == at.Count / 2, MarkAngle = markAngle,
				TrunkRadius = rng.RandfRange(0.26f, 0.34f), TrunkHeight = rng.RandfRange(5.5f, 8f), NoteHeight = rng.RandfRange(1.45f, 1.7f),
			};
			AddChild(note);
			note.GlobalTransform = new Transform3D(basis, at[i]);
			trail.Add(note);
			_notes.Add(note);
			var cz = new ClearZone { Name = $"Clear{piece}_{i + 1}", Radius = 2.2f, ClearFoliage = true };
			AddChild(cz);
			cz.GlobalPosition = at[i];
		}
		walk += Flat(cachePos - at[^1]).Length();
		_trails.Add(trail);
		_trailLengths.Add(walk);

		// the hiding place, its open side toward the last tree
		var cache = new KeyPieceCache { Name = $"KeyHalf{piece}", Piece = piece, Hide = piece == 1 ? KeyPieceCache.Place.Stump : KeyPieceCache.Place.Cairn };
		AddChild(cache);
		cache.GlobalTransform = new Transform3D(Basis.LookingAt(Flat(cachePos - at[^1]), Vector3.Up), cachePos);
		_caches.Add(cache);
		var cc = new ClearZone { Name = $"ClearCache{piece}", Radius = 2.6f, ClearFoliage = true };
		AddChild(cc);
		cc.GlobalPosition = cachePos;

		// the way in the ink on the ground: up the path to the turn, and tree to tree to the cache (LeadMarks)
		LeadMarks(terrain, piece, branchS, at, cachePos);

		// decoys: two by the start, one by the end, marks that aren't his
		if (DecoyTrees)
		{
		var spots = new List<(Vector3 near, float rMin, float rMax)> { (at[0], 5f, 9f), (at[1], 5f, 9f), (at[^1], 5f, 8f) };
		int dn = 0;
		foreach (var (near, rMin, rMax) in spots)
		{
			for (int tries = 0; tries < 16; tries++)
			{
				float a = rng.RandfRange(0f, Mathf.Tau), r = rng.RandfRange(rMin, rMax);
				Vector3 p = near + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
				terrain.SampleFields(p.X, p.Z, out float dT, out _, out float dS, out _);
				if (dT < 3.5f || dS < 4f) continue;
				bool clear = Flat(cachePos - p).Length() > 3.5f;
				foreach (var o in at) if (Flat(o - p).Length() < 4f) clear = false;
				if (!clear) continue;
				p.Y = terrain.HeightAt(p.X, p.Z);
				var decoy = new TreeNote { Name = $"Decoy{piece}_{dn}", Label = $"d{piece}{dn}", Digit = 4, Decoy = true, MarkAngle = rng.RandfRange(-1.5f, 1.5f), TrunkRadius = rng.RandfRange(0.24f, 0.36f), TrunkHeight = rng.RandfRange(5.5f, 8f) };
				AddChild(decoy);
				decoy.GlobalTransform = new Transform3D(Basis.LookingAt(Flat(p - near) + new Vector3(0.001f, 0, 0), Vector3.Up), p);
				_decoys.Add(decoy);
				var dz = new ClearZone { Name = $"ClearD{piece}{dn}", Radius = 1.6f, ClearFoliage = false };
				AddChild(dz);
				dz.GlobalPosition = p;
				dn++;
				break;
			}
		}
		}
		// a few marks on the ground round the start, well apart
		var marks = new List<Vector3>();
		for (int g = 0; g < 3; g++)
		{
			for (int tries = 0; tries < 12; tries++)
			{
				float a = rng.RandfRange(0f, Mathf.Tau), r = rng.RandfRange(1.5f, 8f);
				Vector3 gp = at[0] + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
				bool clear = Flat(at[0] - gp).Length() >= 0.9f;
				foreach (var o in marks) if (Flat(o - gp).Length() < 2.5f) clear = false;
				if (!clear) continue;
				marks.Add(gp);
				GroundMark(terrain, gp, rng);
				break;
			}
		}
	}

	// ------------------------------------------------------------------ the nudge

	/// <summary>Saved once shown (once in a game).</summary>
	public const string HintArrowFlag = "hint_arrow_blacklight";
	private float _hintWait;

	/// <summary>(2026-10-15) The one nudge: standing near either trail's first tree (the trace on it in plain light) with the
	/// flame on, once R.H.'s note is read and the lantern's own lesson at the camp is over, and before that half is found:
	/// a line under the picture names the blacklight's key. Never over another line, the note, or a cutscene.</summary>
	private void Nudge(float dt)
	{
		if (StoryManager.Instance is not { } s || UI.Subtitle.Instance is not { } sub) return;
		if (s.HasFlag(HintArrowFlag) || !s.HasFlag(Camp.NoteReadFlag) || s.HasFlag(StoryManager.Flag.BunkerUnlocked)) return;
		if (!(s.HasFlag(Player.Lantern.FirstRelitFlag) || s.Current >= Checkpoint.Act5CabinEntered)) return;
		if (GetTree().GetFirstNodeInGroup("player") is not Player.PlayerController player) return;
		bool near = false;
		for (int piece = 1; piece <= _trails.Count; piece++)
		{
			if (s.HasFlag(StoryManager.Flag.KeyPiece(_caches[piece - 1].Piece)) || _trails[piece - 1].Count == 0) continue;
			var t = _trails[piece - 1][0];
			if (Flat(player.GlobalPosition - t.GlobalPosition).Length() < 9f) near = true;
		}
		bool hasLantern = player.Inventory?.HasLantern ?? false;
		bool clear = near && hasLantern && !Player.Lantern.Uv.on && !sub.CurrentlyShowing && !(player.PlayerInput?.Modal ?? false)
			&& !(player.PlayerInput?.Scripted ?? false) && Cutscene.InputLocks == 0;
		_hintWait = clear ? _hintWait + dt : 0f;
		if (_hintWait < 2.5f) return;
		s.SetFlag(HintArrowFlag);
		string key = UI.KeyHint.Blacklight;
		_ = sub.Show($"Old paint on that tree.   {key}: the blacklight.", 0.8f, 5f, 1.2f);
		GD.Print($"[story] the nudge: old paint on the trail's first tree ({key})");
	}

	// ------------------------------------------------------------------ the ground

	/// <summary>(2026-10-23, more blacklight clues) The way to a cache drawn on the ground in the ink, so the beam finds it:
	/// arrows down the middle of the path for the last 36 m before the turn (pointing on along it), a big one on the turn
	/// itself pointing off into the woods, then a line of arrows every few metres from the path to the first tree and tree to
	/// tree to the cache, and a ring of them round the cache pointing in. The trees' own arrows still say which way.</summary>
	private void LeadMarks(ForestTerrain terrain, int piece, float branchS, List<Vector3> at, Vector3 cachePos)
	{
		var path = new List<Transform3D>();
		var line = new List<Transform3D>();
		Transform3D Mark(Vector3 p, Vector3 dir)
		{
			p.Y = terrain.HeightAt(p.X, p.Z);
			// (the mask's top lies toward the decal's -Z: turned half round, it points the way)
			var b = GroundBasis(terrain, p, Mathf.Atan2(dir.X, dir.Z) + Mathf.Pi);
			return new Transform3D(b, p + b.Y * 0.02f);
		}
		for (float s = branchS - 36f; s < branchS - 2f; s += 8f)   // (2026-10-23, the owner: "tone them down 50%": every 8 m, was 4)
		{
			Vector3 p = terrain.TrailPoint(s, out Vector3 t);
			path.Add(Mark(p, Flat(t).Normalized()));
		}
		Vector3 turn = terrain.TrailPoint(branchS, out _);
		var big = new List<Transform3D> { Mark(turn, Flat(at[0] - turn).Normalized()) };
		var stops = new List<Vector3> { turn };
		stops.AddRange(at);
		stops.Add(cachePos);
		for (int i = 0; i + 1 < stops.Count; i++)
		{
			Vector3 a = stops[i], b = stops[i + 1], d = Flat(b - a);
			float len = d.Length();
			if (len < 0.5f) continue;
			d /= len;
			for (float x = 1.6f; x < len - 1.2f; x += 4f) line.Add(Mark(a + d * x, d));   // (every 4 m, was 2)
		}
		var ring = new List<Transform3D>();
		for (int k = 0; k < 4; k++)   // (four, was eight)
		{
			float ang = k / 4f * Mathf.Tau;
			Vector3 o = new(Mathf.Cos(ang), 0, Mathf.Sin(ang));
			ring.Add(Mark(cachePos + o * (piece == 2 ? 3.2f : 1.7f), -o));   // (the cairn is big: the ring out round its foot)
		}
		UvInk.Scatter(this, $"LeadPath{piece}", path, new Vector2(0.7f, 0.92f), UvInk.Arrow(1), UvInk.Green, 2.4f);
		UvInk.Scatter(this, $"LeadTurn{piece}", big, new Vector2(1.4f, 1.85f), UvInk.Arrow(0), UvInk.Green, 2.6f);
		UvInk.Scatter(this, $"LeadLine{piece}", line, new Vector2(0.6f, 0.8f), UvInk.Arrow(2), UvInk.Cyan, 2.4f);
		UvInk.Scatter(this, $"LeadRing{piece}", ring, new Vector2(0.55f, 0.72f), UvInk.Arrow(1), UvInk.Pale, 2.4f);
		LeadMarkCount += path.Count + big.Count + line.Count + ring.Count;
	}
	/// <summary>For tests: the arrows laid on the ground leading to the caches.</summary>
	public int LeadMarkCount { get; private set; }

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
	private void GroundMark(ForestTerrain terrain, Vector3 p, RandomNumberGenerator rng)
	{
		p.Y = terrain.HeightAt(p.X, p.Z);
		int kind = rng.RandiRange(0, 9);
		// a hand on the ground: something went on all fours here (the thing's, clawed), or someone fell and dragged
		HandKind hk = rng.RandiRange(0, 4) switch { 0 => HandKind.Claw, 1 => HandKind.ClawDragged, 2 => HandKind.Dragged, 3 => HandKind.Splayed, _ => HandKind.ClawSplayed };
		bool clawed = hk is HandKind.Claw or HandKind.ClawDragged or HandKind.ClawSplayed;
		Texture2D tex = kind switch { 0 or 1 => UvInk.Hand(hk, rng.RandiRange(0, 5)), 2 or 3 => UvInk.Foot(), 4 => UvInk.Digit(rng.RandiRange(0, 9), true), _ => UvInk.Smear(rng.RandiRange(0, 20)) };
		float size = kind switch { 2 or 3 => 0.28f, 0 or 1 => clawed ? rng.RandfRange(0.3f, 0.38f) : rng.RandfRange(0.21f, 0.25f), _ => rng.RandfRange(0.3f, 0.6f) };
		// (2026-10-13) the decal's basis is the ground's slope there (it was a flat card laid over it, set after)
		UvInk.OnGround(this, p, GroundBasis(terrain, p, rng.RandfRange(0f, Mathf.Tau)), new Vector2(size, kind is 2 or 3 ? size * 2f : size), tex,
			rng.Randf() < 0.35f ? UvInk.Green : UvInk.Pale, 1.4f, rng.Randf() < 0.5f);
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
			// (2026-10-23: + pi, toes forward: the mask's top lies toward the decal's -Z, and they walked heel first)
			float yaw = Mathf.Atan2(fwd.X, fwd.Z) + Mathf.Pi + rng.RandfRange(-0.12f, 0.12f) + (l ? 0.08f : -0.08f);
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
}
