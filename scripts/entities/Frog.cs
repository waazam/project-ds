using Godot;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// Act 1 shot list: a small frog (about 8 cm) sitting on a wet rock at the edge of the creek,
/// a few metres upstream of the footbridge. Green-brown with darker blotches and a pale
/// throat that pulses; now and then it shifts its weight. A close-up subject: the photo
/// counts from 0.6 to 5 m.
///
/// Places itself: finds where the trail crosses the stream, walks <see cref="Upstream"/>
/// metres up it, and sets the rock half in the water at the <see cref="Side"/> bank's edge.
/// Purely decorative (no collision), never leaves.
/// </summary>
[GlobalClass]
public partial class Frog : Node3D
{
	/// <summary>Metres up the stream from the trail crossing.</summary>
	[Export] public float Upstream = 3.2f;
	/// <summary>Which bank, relative to the stream's direction of flow (+1 right, -1 left).</summary>
	[Export] public int Side = 1;
	[Export] public float ModelScale = 1.15f;
	[Export] public int Seed = 3;

	private Node3D _frog, _throat;
	private float _t, _pulseTimer, _pulseOn, _shiftTimer;
	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready()
	{
		_rng.Seed = (ulong)(Seed * 131 + 7);
		TopLevel = true;
		PlaceOnBank();
		BuildRock(out float rockTop);
		_frog = new Node3D { Name = "Frog", Position = new Vector3(0.02f, rockTop - 0.004f, 0.01f), Scale = Vector3.One * ModelScale,
			// side-on to the bank (so it shows in profile from the bank and the bridge), a little nose-up
			Rotation = new Vector3(-0.12f, Mathf.Pi * 0.5f + _rng.RandfRange(-0.4f, 0.4f), 0) };
		AddChild(_frog);
		BuildFrog();
		AddChild(new PhotoSubject
		{
			Name = "PhotoSubject",
			Id = "frog",
			LookPoints = new[] { _frog.Position + new Vector3(0, 0.03f, 0) },
			MinDistance = 0.6f,
			MaxDistance = 5f,
			ConeDegrees = 12f,
			OwnerPath = "..",
		});
		_pulseTimer = 0.5f;
		_shiftTimer = _rng.RandfRange(4f, 9f);
	}

	private void PlaceOnBank()
	{
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain == null || terrain.Stream == null || terrain.Stream.Points.Count < 2) return;
		if (!terrain.TryGetStreamCrossing(out Vector3 cross, out _, out _)) return;
		Vector3 o = terrain.GlobalPosition;
		var stream = terrain.Stream;
		stream.Closest(new Vector2(cross.X - o.X, cross.Z - o.Z), out float s0);
		float s = s0 - Upstream;
		Vector2 p = stream.At(s, out Vector2 t);
		Vector2 nrm = new Vector2(-t.Y, t.X).Normalized() * Side;
		float water = terrain.WaterLevel(s);
		// the bank's edge: where the ground rises out of the water
		float d = 0.25f;
		for (; d < 6f; d += 0.1f)
		{
			Vector2 q = p + nrm * d;
			if (terrain.HeightAt(q.X + o.X, q.Y + o.Z) > water) break;
		}
		Vector2 at = p + nrm * (d - 0.15f);
		float yaw = Mathf.Atan2(nrm.X, nrm.Y);   // facing out over the water
		GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(0, yaw, 0)), new Vector3(at.X + o.X, water, at.Y + o.Z));
	}

	/// <summary>A dark, wet, flat-topped rock sitting in the water's edge; its top about 9 cm above the surface.</summary>
	private void BuildRock(out float top)
	{
		var k = new MeshKit();
		k.Mat(ProcTextures.RockMat);
		k.Color = new Color(0.85f, 0.87f, 0.84f);
		// the rock stands on the bed: same flat top, reaching down to the lowest ground under it
		// (the bank shelves off into the stream, so a squat rock would hang over the bed on the water side)
		float bed = -0.13f;
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null)
			for (int i = 0; i < 9; i++)
			{
				Vector3 lp = i == 0 ? Vector3.Zero : new Vector3(Mathf.Cos(Mathf.Tau * i / 8f) * 0.22f, 0, Mathf.Sin(Mathf.Tau * i / 8f) * 0.18f);
				Vector3 w = GlobalTransform * lp;
				bed = Mathf.Min(bed, terrain.HeightAt(w.X, w.Z) - GlobalPosition.Y - 0.03f);
			}
		const float rockTopY = 0.09f;   // blob centre -0.02 + half-height 0.11: unchanged
		k.Blob(new Vector3(0, (rockTopY + bed) * 0.5f, 0), new Vector3(0.3f, (rockTopY - bed) * 0.5f, 0.24f), Seed * 17 + 1, 0.12f, true, 1.5f, 0.4f);
		k.Color = new Color(0.45f, 0.47f, 0.45f);
		k.Blob(new Vector3(-0.26f, -0.06f, 0.14f), new Vector3(0.13f, 0.08f, 0.12f), Seed * 17 + 2, 0.15f, true, 1.5f, 0.4f);
		var mi = k.CommitTo(this, "Rock");
		// wet sheen: a glossier copy of the rock material
		var wet = (StandardMaterial3D)ProcTextures.RockMat.Duplicate();
		wet.Roughness = 0.35f;
		wet.MetallicSpecular = 0.55f;
		mi.MaterialOverride = wet;
		top = 0.085f;
	}

	private static Color C(float r, float g, float b) => new Color(r, g, b).SrgbToLinear();

	private void BuildFrog()
	{
		var mat = PropTextures.WetSkinMat;
		Color skin = C(0.5f, 0.56f, 0.24f), blotch = C(0.26f, 0.3f, 0.13f), pale = C(0.78f, 0.74f, 0.52f);
		Color eye = C(0.62f, 0.5f, 0.2f), black = C(0.04f, 0.04f, 0.03f);
		var k = new MeshKit();
		k.Mat(mat);
		k.Color = skin;
		k.Blob(new Vector3(0, 0.02f, 0.006f), new Vector3(0.022f, 0.016f, 0.032f), Seed + 1, 0.06f, false, 8f, 0.3f);   // body
		k.Blob(new Vector3(0, 0.025f, -0.027f), new Vector3(0.02f, 0.012f, 0.018f), Seed + 2, 0.05f, false, 8f, 0.3f);  // head
		k.Color = blotch;
		k.Blob(new Vector3(0.006f, 0.034f, 0.01f), new Vector3(0.008f, 0.004f, 0.01f), Seed + 3, 0.1f, false, 8f);
		k.Blob(new Vector3(-0.008f, 0.033f, -0.004f), new Vector3(0.007f, 0.004f, 0.008f), Seed + 4, 0.1f, false, 8f);
		// hind legs folded along the flanks, the feet splayed at the front of them
		k.Color = skin * 0.9f;
		foreach (float x in new[] { -1f, 1f })
		{
			k.Blob(new Vector3(x * 0.022f, 0.011f, 0.02f), new Vector3(0.011f, 0.009f, 0.02f), Seed + 5 + (int)x, 0.06f, false, 8f);
			k.Blob(new Vector3(x * 0.028f, 0.004f, 0.0f), new Vector3(0.009f, 0.003f, 0.014f), Seed + 8 + (int)x, 0.06f, false, 8f);
			// front legs
			k.Cylinder(new Vector3(x * 0.013f, 0.014f, -0.02f), new Vector3(x * 0.019f, 0.001f, -0.033f), 0.004f, 0.003f, 4, false, 8f);
			// eyes: gold bumps with a dark pupil
			k.Color = eye;
			k.Blob(new Vector3(x * 0.012f, 0.033f, -0.028f), new Vector3(0.0065f, 0.0065f, 0.0065f), Seed + 11 + (int)x, 0.02f, false, 8f);
			k.Color = black;
			k.Blob(new Vector3(x * 0.0155f, 0.034f, -0.03f), new Vector3(0.003f, 0.0035f, 0.003f), Seed + 14 + (int)x, 0.02f, false, 8f);
			k.Color = skin * 0.9f;
		}
		k.CommitTo(_frog, "Body", false);
		_throat = new Node3D { Name = "Throat", Position = new Vector3(0, 0.013f, -0.031f) };
		_frog.AddChild(_throat);
		var tk = new MeshKit();
		tk.Mat(mat);
		tk.Color = pale;
		tk.Blob(Vector3.Zero, new Vector3(0.013f, 0.007f, 0.011f), Seed + 20, 0.03f, false, 8f);
		tk.CommitTo(_throat, "Mesh", false);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_t += dt;
		// throat: a run of quick pulses, then a still spell
		_pulseTimer -= dt;
		if (_pulseTimer <= 0f)
		{
			if (_pulseOn > 0f) { _pulseOn = 0f; _pulseTimer = _rng.RandfRange(1.2f, 3.5f); }
			else { _pulseOn = 1f; _pulseTimer = _rng.RandfRange(1.5f, 3f); }
		}
		float swell = _pulseOn > 0f ? Mathf.Max(0f, Mathf.Sin(_t * Mathf.Tau * 2.6f)) : 0f;
		_throat.Scale = new Vector3(1f + 0.25f * swell, 1f + 0.9f * swell, 1f + 0.5f * swell);
		// now and then it shifts round a little on the rock
		_shiftTimer -= dt;
		if (_shiftTimer <= 0f)
		{
			_shiftTimer = _rng.RandfRange(5f, 12f);
			var r = _frog.Rotation;
			r.Y += _rng.RandfRange(-0.5f, 0.5f);
			_frog.Rotation = r;
		}
	}
}
