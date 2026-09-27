using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// One of R.H.'s four code numbers (Acts 3-7, along the Hollow path). The owner's blacklight mechanic
/// replaced the old paper notes: the digit is brushed big on a bare trunk off the path in ink that only
/// the lantern's blacklight shows (<see cref="UvInk"/>), and nothing else marks the tree. It can be on
/// any side of it, a little way round from the path side. Under the flame it's just a dead tree.
///
/// A decoy (<see cref="Decoy"/>) is the same kind of tree carrying marks that are not his number:
/// handprints, smears, tallies, an eye, a digit struck through. There are some round every real one,
/// so the player has to look closely.
///
/// Reading: the blacklight on the digit (its beam's cone on it, in range), within
/// <see cref="ReadDistance"/>, looking at it, and a clear line to it. <see cref="FirstRead"/> fires the
/// first time; <see cref="SurveyLot"/> keeps the tally, saves the flag and shows the digit big.
/// The trunk is built here, so the number always has a tree exactly where the path needs it; a
/// ClearZone keeps the scatter off it.
/// </summary>
public partial class TreeNote : Node3D
{
	[Export] public string Label = "1";
	[Export] public int Digit;
	[Export] public float TrunkHeight = 7f;
	[Export] public float TrunkRadius = 0.3f;
	/// <summary>Eye height of the sheet on the trunk.</summary>
	[Export] public float NoteHeight = 1.55f;
	[Export] public float WakeDistance = 45f;
	/// <summary>Within this, the sheet in view reads it (no lantern needed).</summary>
	[Export] public float ReadDistance = 6.5f;
	/// <summary>A decoy tree: marks, but no number of his.</summary>
	[Export] public bool Decoy;
	/// <summary>Which way round the trunk the number faces, radians from the path side (set by the lot).</summary>
	[Export] public float MarkAngle;
	/// <summary>Half-angle (degrees) of "looking at it".</summary>
	[Export] public float LookDegrees = 45f;
	/// <summary>The paper winks from this far, so the note is noticed from the path.</summary>
	[Export] public float NoticeDistance = 20f;

	/// <summary>Glow 0..1 of the read (for tests and the reveal state).</summary>
	public float Glow { get; private set; }
	/// <summary>The digit can be read right now.</summary>
	public bool Revealed => Glow > 0.6f;
	/// <summary>It has been read at least once (the lot counts these and saves the flag).</summary>
	public bool Read { get; private set; }
	/// <summary>Where the sheet is (world), for aiming at it.</summary>
	public Vector3 PlateWorld => ToGlobal(_sheetLocal);
	/// <summary>Raised once, the first time the digit reads.</summary>
	public event System.Action FirstRead;

	private Vector3 _sheetLocal;
	private StaticBody3D _trunkBody;
	private bool _missPrinted;
	private PlayerController _player;
	private Godot.Collections.Array<Rid> _exclude;
	private double _t;

	/// <summary>Restore: already read in the saved story (no event).</summary>
	public void MarkRead() => Read = true;

	public override void _Ready()
	{
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null) GlobalPosition = GlobalPosition with { Y = terrain.HeightAt(GlobalPosition.X, GlobalPosition.Z) };
		Build();
	}

	private void Build()
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(Label.GetHashCode() & 0x7fffffff) + 31 };
		float lean = Mathf.DegToRad(rng.RandfRange(-3f, 3f)), leanZ = Mathf.DegToRad(rng.RandfRange(-2.5f, 2.5f));
		var post = Basis.FromEuler(new Vector3(lean, 0, leanZ));

		// The trunk: a dead snag, flared at the root, tapering, a few broken stubs, bark like the forest's.
		var k = new MeshKit();
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.62f, 0.58f, 0.52f);
		k.Cylinder(post * new Vector3(0, -0.5f, 0), post * new Vector3(0, 0.6f, 0), TrunkRadius * 1.45f, TrunkRadius * 1.02f, 8, false, 1f);
		float[] ys = { 0.6f, TrunkHeight * 0.45f, TrunkHeight * 0.8f, TrunkHeight };
		for (int s = 0; s < 3; s++)
		{
			float r0 = TrunkRadius * Mathf.Lerp(1.02f, 0.18f, ys[s] / TrunkHeight), r1 = TrunkRadius * Mathf.Lerp(1.02f, 0.18f, ys[s + 1] / TrunkHeight);
			Vector3 wobble = new(rng.RandfRange(-0.08f, 0.08f), 0, rng.RandfRange(-0.08f, 0.08f));
			k.Cylinder(post * new Vector3(0, ys[s], 0), post * (new Vector3(0, ys[s + 1], 0) + wobble * s), r0, r1, 8, s == 2, 1f);
		}
		k.Color = new Color(0.5f, 0.46f, 0.4f);
		for (int i = 0; i < 4; i++)
		{
			float y = rng.RandfRange(2.6f, TrunkHeight - 0.8f), a = rng.RandfRange(0f, Mathf.Tau);
			Vector3 dir = new(Mathf.Cos(a), rng.RandfRange(-0.1f, 0.35f), Mathf.Sin(a));
			Vector3 root = post * new Vector3(0, y, 0) + dir * TrunkRadius * 0.6f;
			k.Cylinder(root, root + dir.Normalized() * rng.RandfRange(0.4f, 0.9f), 0.05f, 0.02f, 5);
		}
		k.CommitTo(this, "Trunk");
		_trunkBody = new StaticBody3D { Name = "TrunkBody", CollisionLayer = 1, CollisionMask = 0 };
		var body = _trunkBody;
		body.SetMeta("surface", "wood");
		AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, TrunkHeight * 0.5f, 0), Shape = new CylinderShape3D { Radius = TrunkRadius * 0.95f, Height = TrunkHeight } });

		// The ink: his digit (or, on a decoy, marks that aren't his), brushed round the trunk, invisible
		// until the blacklight falls on it; a smear or a print lower down on most trees, his or not.
		float rAt(float yy) => TrunkRadius * Mathf.Lerp(1.02f, 0.18f, yy / TrunkHeight);
		var ink = new Node3D { Name = "Ink", Basis = post };
		AddChild(ink);
		if (!Decoy)
		{
			float y = NoteHeight;
			UvInk.OnTrunk(ink, y, MarkAngle, rAt(y), 0.42f, 0.52f, UvInk.Digit(Mathf.Clamp(Digit, 0, 9)), UvInk.Cyan, 1.9f, false, rng.RandfRange(-0.04f, 0.04f));
			_sheetLocal = post * (new Vector3(Mathf.Sin(MarkAngle), 0, Mathf.Cos(MarkAngle)) * (rAt(y) + 0.02f) + Vector3.Up * y);
		}
		int marks = Decoy ? rng.RandiRange(1, 3) : rng.RandiRange(0, 1);
		for (int i = 0; i < marks; i++)
		{
			float y = rng.RandfRange(0.7f, 2.1f), a = MarkAngle + rng.RandfRange(-2.2f, 2.2f);
			if (!Decoy && Mathf.Abs(y - NoteHeight) < 0.5f) y = NoteHeight - 0.65f;
			var tex = UvInk.Decoy(rng.RandiRange(0, 13), Digit);
			float size = rng.RandfRange(0.3f, 0.5f);
			UvInk.OnTrunk(ink, y, a, rAt(y), size, size * 0.9f, tex, rng.Randf() < 0.3f ? UvInk.Green : UvInk.Pale, rng.RandfRange(1.1f, 1.7f), rng.Randf() < 0.5f, rng.RandfRange(-0.08f, 0.08f));
		}
		if (Decoy) _sheetLocal = post * (Vector3.Up * NoteHeight);
	}

	public override void _Process(double delta)
	{
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			if (_player == null) return;
			_exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
			if (_trunkBody != null) _exclude.Add(_trunkBody.GetRid());
		}
		float dt = (float)delta;
		_t += delta;
		float target = 0f;
		Vector3 sheet = PlateWorld;
		float playerDist = sheet.DistanceTo(_player.GlobalPosition);
		// The blacklight on it, close enough, looking at it, nothing in the way: the read.
		if (!Decoy && playerDist < WakeDistance && _player.CameraRig?.Camera is { } cam)
		{
			Vector3 eye = cam.GlobalPosition;
			Vector3 to = sheet - eye;
			float dist = to.Length();
			float angle = dist > 0.1f ? Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp((-cam.GlobalBasis.Z).Dot(to / dist), -1f, 1f))) : 0f;
			string miss = null;
			if (dist > 0.1f && dist <= ReadDistance)
			{
				if (Player.Lantern.UvOn(sheet) < 0.45f) miss = "not under the blacklight";
				else if (angle <= LookDegrees)
				{
					// Line of sight to the paper, ignoring the player and the note's own trunk (the sheet sits inside the
					// trunk collider's radius, so the ray must not be allowed to hit it); the ray stops just short of the paper.
					var q = PhysicsRayQueryParameters3D.Create(eye, sheet - to / dist * 0.05f, 1u);
					q.Exclude = _exclude;
					var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
					if (hit.Count == 0) target = 1f;
					else miss = $"ray hit {(hit["collider"].AsGodotObject() as Node)?.Name} at {((Vector3)hit["position"]).DistanceTo(eye):0.00} m";
				}
				else miss = $"angle {angle:0} deg (limit {LookDegrees})";
			}
			else if (dist <= ReadDistance + 2f) miss = $"dist {dist:0.00} m (limit {ReadDistance})";
			if (miss != null && !Read && !_missPrinted) { _missPrinted = true; GD.Print($"[note {Label}] near miss: {miss}"); }
		}
		Glow = Mathf.MoveToward(Glow, target, dt * (target > Glow ? 3f : 1.8f));
		if (Revealed && !Read) { Read = true; FirstRead?.Invoke(); }

	}
}
