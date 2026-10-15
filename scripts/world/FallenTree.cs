using System.Collections.Generic;
using Godot;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.World;

/// <summary>
/// A big fir blown down across the main trail where it ends: the root plate torn up
/// on one side (with its crater), the trunk lying along the real ground, and the dead
/// crown heaped over the trail beyond, so the way on is plainly blocked. Past the root
/// plate, a rock outcrop and older deadfall run off to the left (<see cref="LeftBarrierLength"/>),
/// so the only way on is round the crown on the right, where the old path's stones
/// start toward the stairs (<see cref="FriendTrail"/>).
///
/// Built in world space from <see cref="Root"/>/<see cref="Tip"/> (world XZ) at
/// runtime; solid (trunk, plate and crown all collide) and keeps the scatter clear
/// around itself (ClearZones, created in _Ready before ForestScatter's deferred build).
///
/// It stands at first (the owner, 2026-10-03: "I would like that tree to originally be standing upright normally,
/// but then it falls down blocking the players path when they start getting close"): a dead fir at the trail's end,
/// its root plate under the ground. When the player comes within <see cref="FallTriggerDistance"/> it creaks, cracks
/// and comes down across the trail (<see cref="TreeFallMotion"/>), tearing its root plate up out of the ground, and
/// lies as it always did. Act1TreeFalls then brings the others down along the way to the stairs.
/// </summary>
[GlobalClass]
public partial class FallenTree : Node3D
{
	[Export] public Vector2 Root = new(7f, -466f);
	[Export] public Vector2 Tip = new(-16f, -472f);
	[Export] public float TrunkRadius = 0.75f;
	/// <summary>Fraction of the length (from the root) where the dead crown begins.</summary>
	[Export] public float CrownFrom = 0.45f;
	[Export] public int Seed = 19;

	private ForestTerrain _terrain;
	/// <summary>How near the player comes (m, level, to its foot) before it falls.</summary>
	[Export] public float FallTriggerDistance = 22f;   // (within sight in the trail end's fog: at 34 m it fell unseen)
	/// <summary>False: it lies as before, from the start (a preview, an older scene).</summary>
	[Export] public bool StartsStanding = true;
	private readonly TreeFallMotion _motion = new();
	private Node3D _fall, _crater, _debris;
	/// <summary>Where the trunk, standing, meets the ground (world).</summary>
	private Vector3 _standGround;
	private StaticBody3D _standBody, _lieBody;
	private Vector3 _pivot, _rotAxis;
	/// <summary>For the story and the tests: it has come down.</summary>
	public bool Fallen => _motion.State == TreeFallMotion.Phase.Down;
	public bool Falling => _motion.State is TreeFallMotion.Phase.Creak or TreeFallMotion.Phase.Fall or TreeFallMotion.Phase.Settle;
	public event System.Action Landed;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_terrain = GroundSnap.FindTerrain(this);
		if (_terrain == null) return;
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;

		var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		var axis = Trunk(out Vector3 dir, out Vector3 side);
		// everything that comes down with it hangs off one node, laid out as it lies; standing, that node is turned
		// up about the foot of the trunk
		_fall = new Node3D { Name = "Fall" };
		AddChild(_fall);
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		AddChild(body);
		_lieBody = body;

		// the hinge: on the ground at its foot; standing, the trunk straight up from it
		_pivot = new Vector3(Root.X, Ground(Root.X, Root.Y), Root.Y);
		_rotAxis = Vector3.Up.Cross(dir).Normalized();
		var trunkDir = (axis[^1] - _pivot).Normalized();
		_motion.End = Mathf.Acos(Mathf.Clamp(trunkDir.Dot(Vector3.Up), -1f, 1f));
		_motion.Length = axis[0].DistanceTo(axis[^1]);
		_motion.CreakSeconds = 2.8f;
		// (the butt lies propped on its plate, so standing, the trunk rises a little behind the hinge on the ground: the
		// hinge is the root plate's near edge, and the plate comes up out of the ground about it)
		var stood = new Transform3D(Basis.Identity, _pivot) * new Transform3D(new Basis(_rotAxis, -_motion.End), Vector3.Zero) * new Transform3D(Basis.Identity, -_pivot);
		Vector3 standFoot = stood * axis[0];

		// The base (2026-10-15, the owner: "make sure the falling tree's base doesn't look so fake"): the foot, its roots,
		// the root plate and the hole are made as it stands, against the real ground (a flare and roots like the woods'
		// firs, the plate under the ground), and turned down into the lie with the rest of it (stood's inverse).
		var k = new MeshKit();
		var craterK = new MeshKit();
		var debrisK = new MeshKit();
		_standGround = StandGround(axis, stood);
		BuildTrunk(k, axis, body, stood);
		BuildRootPlate(k, axis[0], dir, side, rng, body, craterK, stood);
		BuildCrown(k, axis, dir, side, rng, body);
		BuildDebris(debrisK, axis, dir);
		k.Color = Colors.White;
		k.CommitTo(_fall, "Mesh");
		_crater = new Node3D { Name = "Crater" };
		AddChild(_crater);
		craterK.CommitTo(_crater, "Mesh");
		_debris = new Node3D { Name = "Debris" };
		AddChild(_debris);
		debrisK.CommitTo(_debris, "Mesh", false);
		BuildClearZones(axis);
		if (LeftBarrierLength > 0f) BuildLeftBarrier(axis[0], dir, side);

		_standBody = new StaticBody3D { Name = "Standing", CollisionLayer = 0, CollisionMask = 0 };
		_standBody.AddChild(new CollisionShape3D { Position = new Vector3(standFoot.X, Ground(standFoot.X, standFoot.Z) + 4f, standFoot.Z), Shape = new CylinderShape3D { Radius = TrunkRadius + 0.1f, Height = 8f } });
		AddChild(_standBody);
		_motion.Event = ev =>
		{
			Vector3 mid = _pivot + Vector3.Up * _motion.Length * 0.5f, land = axis[axis.Count / 2];
			TreeFallMotion.Sound(this, ev, _pivot, mid, land, Seed);
			if (ev == "impact")
			{
				_standBody.CollisionLayer = 0;
				_lieBody.CollisionLayer = 1;
				ShowHole();
				_debris.Visible = true;
				// the plate thumps down on its edge: a puff of its earth
				TreeFallMotion.Dust(this, _pivot - dir * 0.6f + Vector3.Up * 0.3f, new Color(0.12f, 0.095f, 0.07f, 0.45f), 1.6f, 14, 1.6f);
				PutOutBeside();
				Landed?.Invoke();
				GD.Print("[story] Act 1: the dead fir came down across the trail");
			}
		};
		if (StartsStanding)
		{
			_lieBody.CollisionLayer = 0;
			_standBody.CollisionLayer = 1;
			_crater.Visible = false;
			_debris.Visible = false;
			PoseFall();
			SetProcess(true);
		}
		else _motion.SetDown();
	}

	/// <summary>Down at once (no creak, no sound): a restore, a preview.</summary>
	public void SetFallen()
	{
		_motion.SetDown();
		_standBody.CollisionLayer = 0;
		_lieBody.CollisionLayer = 1;
		_crater.Visible = true;
		_debris.Visible = true;
		PoseFall();
	}

	/// <summary>Brings it down now (the story, a preview).</summary>
	public void Fall() => _motion.Start();

	/// <summary>Never pinned under it (2026-10-13; the story never brings it down near them, this is the backstop): a
	/// player inside its length when it lands is put out beside it, on their own side, clear of the crown.</summary>
	private void PutOutBeside()
	{
		if (GetTree().GetFirstNodeInGroup("player") is not Node3D p) return;
		var (a, b) = LieSegment;
		var a2 = new Vector2(a.X, a.Z); var b2 = new Vector2(b.X, b.Z); var q = new Vector2(p.GlobalPosition.X, p.GlobalPosition.Z);
		var dir = (b2 - a2).Normalized();
		float along = (q - a2).Dot(dir);
		var side = new Vector2(-dir.Y, dir.X);
		float off = (q - a2).Dot(side);
		if (along < -1f || along > a2.DistanceTo(b2) + 2f || Mathf.Abs(off) > 3.6f) return;
		var to2 = q + side * ((off >= 0 ? 1f : -1f) * 4.2f - off);
		p.GlobalPosition = new Vector3(to2.X, Ground(to2.X, to2.Y) + 0.05f, to2.Y);
		GD.Print("[story] Act 1: the fir came down where the player stood; put out beside it");
	}

	/// <summary>Told when to come down by <see cref="Act1TreeFalls"/> (2026-10-13: it waits to be seen), not by distance
	/// alone.</summary>
	public bool Directed { get; set; }

	/// <summary>Starts the creak; held, it hangs leaning at the end of it, groaning, until <see cref="Release"/>.</summary>
	public void StartCreak(bool hold)
	{
		_motion.Hold = hold;
		_motion.Start();
	}

	public void Release() => _motion.Hold = false;
	public bool Held => _motion.State == TreeFallMotion.Phase.Creak && _motion.Hold;
	public float CreakTime => _motion.CreakTime;
	public bool Standing => _motion.State == TreeFallMotion.Phase.Standing;
	/// <summary>Its foot on the ground (the hinge).</summary>
	public Vector3 Foot => _pivot;
	/// <summary>Where it lies: root to tip, on the ground (world).</summary>
	public (Vector3 a, Vector3 b) LieSegment => (_pivot, new Vector3(Tip.X, _terrain?.HeightAt(Tip.X, Tip.Y) ?? _pivot.Y, Tip.Y));

	/// <summary>A point up its trunk as it stands (or leans, or falls) now, <paramref name="u"/> of the way up.</summary>
	public Vector3 TrunkPoint(float u)
	{
		var lie = LieSegment.b - _pivot;
		var now = new Basis(_rotAxis, _motion.Angle - _motion.End) * lie.Normalized();
		return _pivot + now * (_motion.Length * u);
	}

	private void PoseFall()
	{
		var to = new Transform3D(Basis.Identity, _pivot);
		_fall.Transform = to * new Transform3D(new Basis(_rotAxis, _motion.Angle - _motion.End), Vector3.Zero) * to.AffineInverse();
	}

	public override void _Process(double delta)
	{
		if (_motion.State == TreeFallMotion.Phase.Down) { SetProcess(false); return; }
		if (_motion.State == TreeFallMotion.Phase.Standing)
		{
			if (!Directed && GetTree().GetFirstNodeInGroup("player") is Node3D p
				&& new Vector2(p.GlobalPosition.X - Root.X, p.GlobalPosition.Z - Root.Y).Length() < FallTriggerDistance)
				_motion.Start();
			return;
		}
		_motion.Step((float)delta);
		PoseFall();
		// the hole shows once the plate is up off it (at the crack it was still in the ground, covering it), with a
		// burst of its earth
		if (!_crater.Visible && _motion.Angle > 0.32f) ShowHole();
	}

	/// <summary>
	/// Length (m) of the barrier past the root plate: a rock outcrop and a tangle of older deadfall
	/// running off to the left of the trail's end, solid, so the only way on is round the crown on
	/// the right (where the old path's stones start). 0 = none.
	/// </summary>
	[Export] public float LeftBarrierLength = 26f;

	private void BuildLeftBarrier(Vector3 butt, Vector3 dir, Vector3 side)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 11 + 5) };
		// Away from the crown along the trunk's line, starting just behind the plate; the far end bends
		// forward a little (into the woods), as outcrops follow the lie of the ground.
		Vector3 start = butt - dir * 1.2f;
		Vector3 Along(float d)
		{
			float bend = Mathf.SmoothStep(0.6f, 1f, d / LeftBarrierLength) * 5f;
			Vector3 p = start - dir * d - side * (bend + 0.8f * Mathf.Sin(d * 0.37f));
			p.Y = Ground(p.X, p.Z);
			return p;
		}
		var body = new StaticBody3D { Name = "Barrier", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "rock");
		AddChild(body);
		var k = new MeshKit();
		var rock = ProcTextures.MossRockMat;

		// the outcrop: boulders in a broken chain, big ones with smaller ones heaped against them
		var stones = new List<(Vector3 c, Vector3 r)>();
		for (float d = 0f; d <= LeftBarrierLength; d += rng.RandfRange(1.3f, 2.0f))
		{
			Vector3 p = Along(d) + side * rng.RandfRange(-0.6f, 0.6f);
			float r = rng.RandfRange(0.8f, 1.45f) * (d < 3f ? 1.2f : 1f);
			float sh = rng.RandfRange(1.2f, 1.55f);   // weathered, lichen-pale: it has to read in the gloom
			k.Color = new Color(sh, sh, sh * 0.97f);
			var radii = new Vector3(r * rng.RandfRange(0.9f, 1.3f), r * rng.RandfRange(0.75f, 0.95f), r);
			k.Mat(rock).Blob(p + Vector3.Up * r * 0.45f, radii, Seed * 31 + (int)(d * 10), 0.22f, true, 0.7f, 0.3f);
			stones.Add((p + Vector3.Up * r * 0.45f, radii));
			if (rng.Randf() < 0.6f)
			{
				float r2 = r * rng.RandfRange(0.45f, 0.7f);
				Vector3 q = p + side * rng.RandfRange(-1f, 1f) * r + dir * rng.RandfRange(-0.6f, 0.6f);
				q.Y = Ground(q.X, q.Z);
				k.Blob(q + Vector3.Up * r2 * 0.4f, new Vector3(r2 * 1.2f, r2 * 0.8f, r2), Seed * 37 + (int)(d * 10), 0.22f, true, 0.7f, 0.3f);
				stones.Add((q + Vector3.Up * r2 * 0.4f, new Vector3(r2 * 1.2f, r2 * 0.8f, r2)));
			}
		}

		// the highest thing a log end of radius rr can rest on at p: a boulder's top (sunk into it a little) or the ground
		float RestHeight(Vector3 p, float rr)
		{
			float h = Ground(p.X, p.Z) + rr * 0.75f;
			foreach (var (c, rad) in stones)
			{
				float dx = (p.X - c.X) / rad.X, dz = (p.Z - c.Z) / rad.Z;
				float t = dx * dx + dz * dz;
				if (t >= 0.8f) continue;   // the blobs are jittered: only their solid middle counts
				h = Mathf.Max(h, c.Y + rad.Y * 0.78f * Mathf.Sqrt(1f - t) + rr * 0.5f);
			}
			return h;
		}

		// the deadfall: older trunks, grey and broken, lying across and along the outcrop, propped on it
		k.Mat(ProcTextures.BarkMat);
		for (int i = 0; i < 9; i++)
		{
			float d0 = rng.RandfRange(0f, LeftBarrierLength - 3f);
			float len = rng.RandfRange(4f, 8f);
			Vector3 a = Along(d0) + side * rng.RandfRange(-1.8f, 1.8f);
			float ang = rng.RandfRange(-0.7f, 0.7f);   // mostly along the line, some across it
			Vector3 heading = (-dir * Mathf.Cos(ang) + side * Mathf.Sin(ang)).Normalized();
			Vector3 b = a + heading * len;
			a.Y = Ground(a.X, a.Z) + rng.RandfRange(0.3f, 1.3f);
			b.Y = Ground(b.X, b.Z) + rng.RandfRange(0.2f, 0.7f);
			float r = rng.RandfRange(0.2f, 0.38f);
			// Propped where the outcrop is under it, else lying on the ground: never held up by nothing.
			a.Y = Mathf.Min(a.Y, RestHeight(a, r));
			b.Y = Mathf.Min(b.Y, RestHeight(b, r * 0.55f));
			float g = rng.RandfRange(0.9f, 1.05f);   // silver-grey, like the fir
			k.Color = new Color(g, g * 0.95f, g * 0.9f);
			BunkerKit.Tube(k, new List<Vector3> { a, a.Lerp(b, 0.5f) + Vector3.Up * rng.RandfRange(-0.1f, 0.15f), b }, r, r * 0.55f, 6);
			// snapped limbs sticking up out of it
			for (int j = 0; j < 3; j++)
			{
				Vector3 from = a.Lerp(b, rng.RandfRange(0.15f, 0.9f));
				Vector3 to = from + new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(0.6f, 1.5f), rng.RandfRange(-0.6f, 0.6f));
				BunkerKit.Tube(k, new List<Vector3> { from, to }, rng.RandfRange(0.04f, 0.08f), 0.015f, 4);
			}
		}
		k.Color = Colors.White;
		k.CommitTo(this, "BarrierMesh");

		// Solid: a chain of boxes along the line, too tall to step over, thick enough not to squeeze through.
		const float step = 1.5f;
		for (float d = -0.5f; d < LeftBarrierLength + 1f; d += step)
		{
			Vector3 a = Along(Mathf.Max(0f, d)), b = Along(d + step);
			Vector3 mid = (a + b) * 0.5f;
			Vector3 fwd = b - a; fwd.Y = 0;
			if (fwd.LengthSquared() < 0.001f) continue;
			body.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(2.4f, 3f, fwd.Length() + 0.4f) },
				Transform = new Transform3D(Basis.LookingAt(fwd.Normalized(), Vector3.Up), mid + Vector3.Up * 1.2f),
			});
		}
		// keep the scatter's trees out of the rocks
		for (float d = 0f; d <= LeftBarrierLength; d += 3f)
		{
			var cz = new ClearZone { Radius = 2.2f, ClearFoliage = false, Name = $"ClearBarrier{(int)d}" };
			AddChild(cz);
			cz.GlobalPosition = Along(d);
		}
	}

	private float Ground(float x, float z) => _terrain.HeightAt(x, z);

	/// <summary>Trunk centreline, root to tip, resting on the ground along its length
	/// (propped a little higher at the root by the plate), tapering toward the tip.</summary>
	private List<Vector3> Trunk(out Vector3 dir, out Vector3 side)
	{
		var pts = new List<Vector3>();
		const int n = 12;
		for (int i = 0; i <= n; i++)
		{
			float t = (float)i / n;
			var p = Root.Lerp(Tip, t);
			float r = Mathf.Lerp(TrunkRadius, TrunkRadius * 0.3f, t);
			// Propped off the ground: high at the butt (the plate holds it up), still clear of the
			// ground where it crosses the trail (resting on its own snapped branches), down at the crown.
			float lift = Mathf.Lerp(1.1f, 0.15f, Mathf.SmoothStep(0f, 1f, t));
			pts.Add(new Vector3(p.X, Ground(p.X, p.Y) + r * 0.85f + lift, p.Y));
		}
		// Never float over a dip: ease sharp drops so the trunk sags across them like a real log would.
		for (int pass = 0; pass < 2; pass++)
			for (int i = 1; i < n; i++)
			{
				float mid = (pts[i - 1].Y + pts[i + 1].Y) * 0.5f;
				if (pts[i].Y < mid - 0.3f) pts[i] = new Vector3(pts[i].X, mid - 0.3f, pts[i].Z);
			}
		var d2 = (Tip - Root).Normalized();
		dir = new Vector3(d2.X, 0, d2.Y);
		side = dir.Cross(Vector3.Up).Normalized();
		return pts;
	}

	// ------------------------------------------------------------------ materials
	// (2026-10-15) The woods' own bark (it was the old drawn BarkMat, blotchy and red beside the firs round it), held
	// still: the breeze leans a tree by its height in the mesh's space, which for this one, laid out as it lies, is not
	// its own height.
	private static ShaderMaterial _barkMat;
	private static ShaderMaterial Bark
	{
		get
		{
			if (_barkMat != null) return _barkMat;
			_barkMat = (ShaderMaterial)ProcTextures.TreeBarkMat.Duplicate();
			_barkMat.SetShaderParameter("sway", false);
			return _barkMat;
		}
	}
	private static StandardMaterial3D _soilMat, _litterMat, _rawMat;
	/// <summary>Torn earth: the forest floor's dirt, mapped in the mesh's own space so it rides with the plate.</summary>
	private static StandardMaterial3D Soil => _soilMat ??= Projected(ProcTextures.Dirt(), 0.55f);
	/// <summary>The old floor on the plate's top: leaf litter and needles, riding with it.</summary>
	private static StandardMaterial3D Litter => _litterMat ??= Projected(ProcTextures.LeafLitter(), 0.6f);
	/// <summary>Raw wood where a root or limb snapped: pale end grain.</summary>
	private static StandardMaterial3D Raw => _rawMat ??= new StandardMaterial3D
	{
		AlbedoTexture = ProcTextures.EndGrain(), VertexColorUseAsAlbedo = true, Roughness = 1f, MetallicSpecular = 0.2f,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	};
	private static StandardMaterial3D Projected(Texture2D tex, float scale) => new()
	{
		AlbedoTexture = tex, VertexColorUseAsAlbedo = true, Roughness = 1f, MetallicSpecular = 0.1f,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		Uv1Triplanar = true, Uv1TriplanarSharpness = 2f, Uv1Scale = Vector3.One * scale,
	};
	private static Color Grey(float v) => new(v, v * 0.98f, v * 0.95f);

	/// <summary>A tube through <paramref name="rings"/> as ForestScatter.TrunkLoft makes the woods' trunks, but each ring
	/// laid out once, square to the tube there and turned the same way (<paramref name="across"/>, square to the bend):
	/// TrunkLoft frames each piece by its own run, which on a bent axis opened cracks between them.</summary>
	private static void Loft(MeshKit k, List<(Vector3 c, float r, Color col)> rings, int sides, Vector3 across)
	{
		int n = rings.Count;
		var ring = new Vector3[n][];
		var tan = new Vector3[n];
		for (int i = 0; i < n; i++)
		{
			Vector3 t = (rings[Mathf.Min(i + 1, n - 1)].c - rings[Mathf.Max(i - 1, 0)].c).Normalized();
			Vector3 x = across - t * across.Dot(t);
			if (x.LengthSquared() < 1e-6f) x = t.Cross(Mathf.Abs(t.Y) > 0.9f ? Vector3.Right : Vector3.Up);
			x = x.Normalized();
			Vector3 z = t.Cross(x).Normalized();
			tan[i] = t;
			ring[i] = new Vector3[sides + 1];
			for (int s = 0; s <= sides; s++)
			{
				float a = Mathf.Tau * s / sides;
				ring[i][s] = x * Mathf.Cos(a) + z * Mathf.Sin(a);
			}
		}
		float v0 = 0f;
		for (int i = 0; i < n - 1; i++)
		{
			var (c0, r0, col0) = rings[i];
			var (c1, r1, _) = rings[i + 1];
			float len = c0.DistanceTo(c1);
			float v1 = v0 + len * 0.5f;
			float slope = (r0 - r1) / Mathf.Max(len, 0.001f);
			for (int s = 0; s < sides; s++)
			{
				Vector3 d00 = ring[i][s], d01 = ring[i][s + 1], d10 = ring[i + 1][s], d11 = ring[i + 1][s + 1];
				Vector3 p00 = c0 + d00 * r0, p01 = c0 + d01 * r0, p10 = c1 + d10 * r1, p11 = c1 + d11 * r1;
				Vector3 n00 = (d00 + tan[i] * slope).Normalized(), n01 = (d01 + tan[i] * slope).Normalized();
				Vector3 n10 = (d10 + tan[i + 1] * slope).Normalized(), n11 = (d11 + tan[i + 1] * slope).Normalized();
				float u0 = (float)s / sides * 2f, u1 = (float)(s + 1) / sides * 2f;
				k.TriC(p00, p10, p11, n00, n10, n11, new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), col0, rings[i + 1].col, rings[i + 1].col);
				k.TriC(p00, p11, p01, n00, n11, n01, new Vector2(u0, v0), new Vector2(u1, v1), new Vector2(u1, v0), col0, rings[i + 1].col, col0);
			}
			v0 = v1;
		}
	}

	/// <summary>Where the trunk, as it stands, meets the ground.</summary>
	private Vector3 StandGround(List<Vector3> axis, Transform3D stood)
	{
		Vector3 s0 = stood * axis[0], up = (stood * axis[1] - s0).Normalized();
		float g = Ground(s0.X, s0.Z);
		return s0 + up * ((g - s0.Y) / Mathf.Max(up.Y, 0.2f));
	}

	private void BuildTrunk(MeshKit k, List<Vector3> axis, StaticBody3D body, Transform3D stood)
	{
		// One smooth tube as the woods' firs are (ForestScatter.FirMesh): the flare from under the ground, the trunk up
		// its bent axis, twelve sides, the bark running on unbroken. Made as it stands, so the flare is at the real ground.
		k.Mat(Bark);
		Vector3 f = _standGround, up = (stood * axis[1] - stood * axis[0]).Normalized();
		float R = TrunkRadius;
		var rings = new List<(Vector3 c, float r, Color col)>
		{
			(f - up * 0.5f, R * 1.42f, Grey(0.44f)),
			(f - up * 0.08f, R * 1.36f, Grey(0.47f)),
			(f + up * 0.28f, R * 1.17f, Grey(0.52f)),
			(f + up * 0.7f, R * 1.06f, Grey(0.56f)),
			(f + up * 1.25f, R * 1.01f, Grey(0.58f)),
		};
		int n = axis.Count - 1;
		for (int i = 1; i <= n; i++)
		{
			Vector3 p = stood * axis[i];
			if ((p - f).Dot(up) < 1.9f) continue;
			float t = (float)i / n;
			// dead a long while: greyer than the living firs' bark, a little paler up the trunk where it has weathered
			rings.Add((p, Mathf.Lerp(R, R * 0.3f, t), Grey(Mathf.Lerp(0.6f, 0.68f, t))));
		}
		k.Xf = stood.AffineInverse();
		Loft(k, rings, 12, _rotAxis);
		k.Xf = Transform3D.Identity;
		// the top, snapped off long ago: a splintered point, not an open tube
		var tdir = (axis[^1] - axis[0]).Normalized();
		var tside = tdir.Cross(Vector3.Up).Normalized();
		k.Color = Grey(0.66f);
		k.Cylinder(axis[^1], axis[^1] + tdir * 0.7f + Vector3.Up * 0.05f, R * 0.3f, 0.03f, 6, false, 1f);
		// broken branch stubs along the trunk, thin as the woods' firs' are, the ones on top sticking up, the lower
		// ones propping it on the ground
		var srng = new RandomNumberGenerator { Seed = (ulong)(Seed * 3 + 1) };
		k.Color = Grey(0.54f);
		for (int i = 1; i < axis.Count - 1; i++)
			for (int s = 0; s < 2; s++)
			{
				float t = (float)i / (axis.Count - 1);
				float r = Mathf.Lerp(TrunkRadius, TrunkRadius * 0.3f, t);
				float a = srng.RandfRange(0f, Mathf.Tau);
				var o = (tside * Mathf.Cos(a) + Vector3.Up * Mathf.Sin(a)).Normalized();
				var p = axis[i].Lerp(axis[i + 1], srng.Randf()) + o * r * 0.8f;
				// (the ones sticking up snapped short, blunt: long thin points along the top read as a row of spikes)
				float len = srng.RandfRange(0.4f, 1.3f) * (o.Y > 0.3f ? 0.45f : 1f);
				var end = p + (o + tdir * 0.3f).Normalized() * len;
				float g = Ground(end.X, end.Z);
				if (end.Y < g) end.Y = g;   // a prop into the ground
				float sr = srng.RandfRange(0.045f, 0.085f);
				BunkerKit.Tube(k, new List<Vector3> { p, end }, sr, o.Y > 0.3f ? sr * 0.55f : 0.02f, 5);
			}

		// Collision: a box per segment, tall enough that the trunk can't be stepped over.
		for (int i = 0; i < axis.Count - 1; i++)
		{
			var a = axis[i]; var b = axis[i + 1];
			float r = Mathf.Lerp(TrunkRadius, TrunkRadius * 0.3f, (i + 0.5f) / (axis.Count - 1));
			var mid = (a + b) * 0.5f;
			var fwd = (b - a).Normalized();
			var basis = Basis.LookingAt(fwd, Vector3.Up);
			float gy = Ground(mid.X, mid.Z);
			float top = Mathf.Max(mid.Y + r, gy + 1.3f);
			body.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(r * 2f + 0.2f, top - gy + 0.5f, a.DistanceTo(b) + 0.1f) },
				Transform = new Transform3D(basis, new Vector3(mid.X, (top + gy - 0.5f) * 0.5f, mid.Z)),
			});
		}
	}

	/// <summary>The upturned root plate: a ragged slab of earth and roots standing on edge at the butt, facing along the
	/// trunk, with the hole it tore out of the ground just behind it (its collision as it always was).</summary>
	private void BuildRootPlate(MeshKit k, Vector3 butt, Vector3 dir, Vector3 side, RandomNumberGenerator rng, StaticBody3D body, MeshKit craterK, Transform3D stood)
	{
		Vector3 c = butt - dir * 0.4f + Vector3.Up * 0.6f;
		var frame = new Basis(dir, Vector3.Up, side);   // right-handed: dir x up = side
		// (2026-10-15) the old plate's draws, so the crown after it comes out as it always did
		for (int i = 0; i < 16 * 7; i++) rng.Randf();

		// The root plate (2026-10-15; it was a smooth lens of earth, a 5 m shield on edge with a few twigs on its rim, and
		// standing, its top lay on the ground round the foot as a bare disc): made as the tree stands, a ragged slab of
		// earth under the ground round the foot, its top 10 cm down, its underside bellied deep under the trunk, roots
		// through it and stones in it, and the surface roots of the flare over it. Turned down into the lie, it stands on
		// its near edge (the hinge) with the old floor and the flare toward the crown and the torn earth toward the hole.
		var prng = new RandomNumberGenerator { Seed = (ulong)(Seed * 7 + 3) };
		Vector3 f = _standGround;
		const int sec = 22;
		float[] us = { 0f, 0.32f, 0.62f, 0.86f, 1f };
		int last = us.Length - 1;
		var rad = new float[sec];
		for (int i = 0; i < sec; i++) rad[i] = prng.RandfRange(1.3f, 2.35f);
		var raw0 = (float[])rad.Clone();
		for (int i = 0; i < sec; i++) rad[i] = (raw0[i] * 3f + raw0[(i + 1) % sec] + raw0[(i + sec - 1) % sec]) / 5f;   // ragged, not spiky
		var dep = new float[sec, us.Length];
		for (int i = 0; i < sec; i++)
			for (int j = 0; j < us.Length; j++)
				dep[i, j] = Mathf.Lerp(1.05f, 0.32f, us[j] * us[j]) * prng.RandfRange(0.8f, 1.2f);
		var lipOut = new float[sec];
		var lipDown = new float[sec];
		for (int i = 0; i < sec; i++) { lipOut[i] = prng.RandfRange(1.0f, 1.12f); lipDown[i] = prng.RandfRange(0.12f, 0.3f); }
		Vector3 Dir(int i) { float a = Mathf.Tau * (i % sec) / sec; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
		Vector3 At(Vector3 p, float below) { p.Y = Ground(p.X, p.Z) - below; return p; }
		Vector3 Top(int i, int j) => At(f + Dir(i) * rad[i % sec] * us[j], 0.1f);
		Vector3 Bot(int i, int j) => At(f + Dir(i) * rad[i % sec] * us[j] * (j == last ? 0.88f : 1f), 0.1f + dep[i % sec, j]);
		Vector3 Lip(int i) => At(f + Dir(i) * rad[i % sec] * lipOut[i % sec], 0.1f + lipDown[i % sec]);
		Vector3 core = f + Vector3.Down * 0.5f;

		void Face(MeshKit kit, Vector3 a, Vector3 b, Vector3 d, bool top)
		{
			var nn = (b - a).Cross(d - a);
			if (nn.LengthSquared() < 1e-10f) return;
			nn = nn.Normalized();
			if (top ? nn.Y < 0f : nn.Dot((a + b + d) / 3f - core) < 0f) nn = -nn;
			kit.Tri(a, b, d, nn, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		}
		k.Xf = stood.AffineInverse();
		// the top: the old forest floor, litter and needles (under the ground while it stands)
		k.Mat(Litter);
		for (int i = 0; i < sec; i++)
			for (int j = 0; j < last; j++)
			{
				k.Color = Grey(prng.RandfRange(0.62f, 0.75f));
				Face(k, Top(i, j), Top(i + 1, j), Top(i + 1, j + 1), true);
				Face(k, Top(i, j), Top(i + 1, j + 1), Top(i, j + 1), true);
			}
		// the underside and the torn rim: dark churned earth, wet and clotted
		k.Mat(Soil);
		for (int i = 0; i < sec; i++)
		{
			for (int j = 0; j < last; j++)
			{
				k.Color = new Color(0.56f, 0.47f, 0.38f) * prng.RandfRange(0.75f, 1.1f);
				Face(k, Bot(i, j), Bot(i + 1, j), Bot(i + 1, j + 1), false);
				Face(k, Bot(i, j), Bot(i + 1, j + 1), Bot(i, j + 1), false);
			}
			k.Color = new Color(0.6f, 0.5f, 0.4f) * prng.RandfRange(0.8f, 1.1f);
			Face(k, Top(i, last), Top(i + 1, last), Lip(i + 1), false);
			Face(k, Top(i, last), Lip(i + 1), Lip(i), false);
			Face(k, Lip(i), Lip(i + 1), Bot(i + 1, last), false);
			Face(k, Lip(i), Bot(i + 1, last), Bot(i, last), false);
		}
		// clods and stones caught in the roots, on the underside
		for (int s = 0; s < 14; s++)
		{
			int i = prng.RandiRange(0, sec - 1);
			float u = prng.RandfRange(0.25f, 0.95f);
			float d = Mathf.Lerp(1.05f, 0.32f, u * u);
			Vector3 p = At(f + Dir(i) * rad[i] * u, 0.1f + d * 0.9f);
			float r = prng.RandfRange(0.1f, 0.26f);
			bool stone = s % 3 == 0;
			k.Mat(stone ? ProcTextures.RockMat : Soil);
			k.Color = stone ? Grey(prng.RandfRange(0.55f, 0.75f)) : new Color(0.5f, 0.41f, 0.33f);
			k.Blob(p, new Vector3(r * 1.2f, r * 0.8f, r), Seed * 41 + s, 0.25f, true);
		}
		// clods hanging off the torn rim, breaking its outline
		k.Mat(Soil);
		for (int s = 0; s < 12; s++)
		{
			int i = prng.RandiRange(0, sec - 1);
			Vector3 p = At(f + Dir(i) * rad[i] * prng.RandfRange(0.95f, 1.08f), 0.1f + prng.RandfRange(0.25f, 0.45f));
			float r = prng.RandfRange(0.18f, 0.36f);
			k.Color = new Color(0.52f, 0.43f, 0.35f) * prng.RandfRange(0.8f, 1.1f);
			k.Blob(p, new Vector3(r * 1.25f, r * 0.85f, r), Seed * 43 + s, 0.28f, true);
		}
		// roots out of the underside and the rim, snapped where the ground held them (pale raw wood at the ends), hanging
		// as they will when it lies: "hang" is the way down will be then
		Vector3 hang = stood.Basis * Vector3.Down;
		Vector3 Under(Vector3 p) { float g = Ground(p.X, p.Z) - 0.14f; if (p.Y > g) p.Y = g; return p; }   // never up through the ground while it stands
		var rawEnds = new List<(Vector3 p, Vector3 d, float r)>();
		for (int s = 0; s < 34; s++)
		{
			int i = prng.RandiRange(0, sec - 1);
			bool rim = s % 2 == 0;
			float u = rim ? 0.95f : prng.RandfRange(0.15f, 0.8f);
			float d = Mathf.Lerp(1.05f, 0.32f, u * u);
			Vector3 from = At(f + Dir(i) * rad[i] * u * (rim ? 0.92f : 1f), 0.1f + (rim ? prng.RandfRange(0.15f, 0.3f) : d * 0.7f));
			Vector3 outDir = rim
				? (Dir(i) + Vector3.Down * prng.RandfRange(0.05f, 0.5f)).Normalized()
				: (Dir(i) * prng.RandfRange(0.2f, 0.9f) + Vector3.Down).Normalized();
			outDir = (outDir + new Vector3(prng.RandfRange(-0.3f, 0.3f), 0, prng.RandfRange(-0.3f, 0.3f))).Normalized();
			float len = prng.RandfRange(0.5f, rim ? 1.5f : 1f);
			float sag = prng.RandfRange(0.15f, 0.45f) * len;
			Vector3 mid = from + outDir * len * 0.45f + hang * sag * 0.4f + new Vector3(prng.RandfRange(-0.12f, 0.12f), prng.RandfRange(-0.1f, 0.05f), prng.RandfRange(-0.12f, 0.12f));
			Vector3 to = from + outDir * len * 0.85f + hang * sag;
			mid = Under(mid); to = Under(to);
			float r0 = prng.RandfRange(0.03f, rim ? 0.1f : 0.07f), r1 = r0 * prng.RandfRange(0.45f, 0.7f);
			k.Mat(Bark);
			k.Color = Grey(prng.RandfRange(0.4f, 0.5f));
			BunkerKit.Tube(k, new List<Vector3> { from, mid, to }, r0, r1, 5);
			rawEnds.Add((to, (to - mid).Normalized(), r1));
			// a few fine rootlets off it, hanging
			for (int h = 0; h < 2; h++)
			{
				Vector3 q = from.Lerp(to, prng.RandfRange(0.3f, 0.9f));
				Vector3 q1 = Under(q + (outDir * prng.RandfRange(0.05f, 0.25f) + hang * prng.RandfRange(0.2f, 0.45f) + new Vector3(prng.RandfRange(-0.15f, 0.15f), 0, prng.RandfRange(-0.15f, 0.15f))));
				BunkerKit.Tube(k, new List<Vector3> { q, q1 }, r1 * 0.45f, 0.006f, 3);
			}
		}
		// the flare's surface roots, over the plate: out of the trunk a little above the ground, diving back into it a
		// couple of metres out (ForestScatter.Roots, bigger, and following this ground)
		k.Mat(Bark);
		int nr = 7;
		float a0 = prng.RandfRange(0f, Mathf.Tau);
		float R = TrunkRadius * 1.3f;
		for (int i = 0; i < nr; i++)
		{
			float a = a0 + Mathf.Tau * i / nr + prng.RandfRange(-0.3f, 0.3f);
			Vector3 d = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			Vector3 sd = new(-d.Z, 0, d.X);
			// (into the plate before its rim: so lying, none sticks out past its edge as a spike)
			int ia = Mathf.PosMod(Mathf.RoundToInt(a / Mathf.Tau * sec), sec);
			float reach = Mathf.Min(R * prng.RandfRange(1.9f, 2.6f) + 0.3f, rad[ia] * 0.85f);
			float bend = prng.RandfRange(-0.25f, 0.25f);
			float rr = R * prng.RandfRange(0.3f, 0.4f);
			Vector3 Q(float s, float h) { var p = f + d * s + sd * bend * s * 0.3f; p.Y = Ground(p.X, p.Z) + h; return p; }
			Loft(k, new List<(Vector3, float, Color)>
			{
				(f + d * R * 0.45f + Vector3.Up * 0.55f, rr * 0.9f, Grey(0.48f)),
				(Q(R * 1.0f, 0.2f), rr, Grey(0.46f)),
				(Q(reach * 0.6f, 0.04f), rr * 0.62f, Grey(0.4f)),
				(Q(reach, -0.24f), rr * 0.3f, Grey(0.36f)),
			}, 6, sd);
		}
		// the snapped ends
		k.Mat(Raw);
		k.Color = new Color(0.92f, 0.84f, 0.7f);
		foreach (var (p, d, r) in rawEnds)
			k.Cylinder(p - d * 0.02f, p + d * 0.05f, r * 1.05f, r * 0.7f, 5, true, 1f);
		k.Xf = Transform3D.Identity;

		BuildHole(craterK, f, rad, prng);

		body.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(1.2f, 4f, 5.2f) },
			Transform = new Transform3D(frame, c + Vector3.Up * 0.2f),
		});
	}

	/// <summary>The hole the plate tore out of (2026-10-15; it was a smooth brown blob): where it stood, a dark scoop of
	/// churned earth (dark at the bottom, so it reads deep, though it can't go under the ground's own surface), a heaved
	/// lip round it with clods on it, and the roots the ground kept, snapped, their pale ends sticking up out of it.</summary>
	private void BuildHole(MeshKit k, Vector3 f, float[] rad, RandomNumberGenerator prng)
	{
		int sec = rad.Length;
		// (u along the radius, height over the ground, shade)
		var rings = new (float u, float h, float shade)[] { (0f, 0.03f, 0.13f), (0.4f, 0.035f, 0.17f), (0.75f, 0.05f, 0.25f), (0.97f, 0.09f, 0.36f), (1.1f, 0.2f, 0.5f), (1.38f, -0.04f, 0.6f) };
		var jit = new float[sec];
		for (int i = 0; i < sec; i++) jit[i] = prng.RandfRange(0.85f, 1.15f);
		Vector3 Dir(int i) { float a = Mathf.Tau * (i % sec) / sec; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
		Vector3 P(int i, int j)
		{
			var (u, h, _) = rings[j];
			var p = f + Dir(i) * rad[i % sec] * 0.95f * u;
			p.Y = Ground(p.X, p.Z) + h * (j == 4 ? jit[i % sec] : 1f);
			return p;
		}
		// sloping in toward the bottom, out down the lip's far side: shaded as a hollow
		Vector3 N(int i, int j)
		{
			float u = rings[j].u;
			return u < 1.05f ? (Vector3.Up - Dir(i) * 0.7f * u).Normalized() : u < 1.2f ? Vector3.Up : (Vector3.Up + Dir(i) * 0.4f).Normalized();
		}
		Color C(int i, int j) => new Color(0.5f, 0.42f, 0.34f) * rings[j].shade * 2f * (0.9f + 0.1f * jit[i % sec]);
		k.Mat(Soil);
		for (int i = 0; i < sec; i++)
			for (int j = 0; j < rings.Length - 1; j++)
			{
				Vector3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
				Vector3 na = N(i, j), nb = N(i + 1, j), nc = N(i + 1, j + 1), nd = N(i, j + 1);
				k.TriC(a, b, c, na, nb, nc, Vector2.Zero, Vector2.Zero, Vector2.Zero, C(i, j), C(i + 1, j), C(i + 1, j + 1));
				k.TriC(a, c, d, na, nc, nd, Vector2.Zero, Vector2.Zero, Vector2.Zero, C(i, j), C(i + 1, j + 1), C(i, j + 1));
			}
		// clods heaved up on the lip
		for (int s = 0; s < 18; s++)
		{
			int i = prng.RandiRange(0, sec - 1);
			Vector3 p = f + Dir(i) * rad[i] * 0.95f * prng.RandfRange(1.0f, 1.3f);
			float r = prng.RandfRange(0.1f, 0.26f);
			p.Y = Ground(p.X, p.Z) + 0.1f + r * 0.2f;
			k.Color = new Color(0.36f, 0.3f, 0.24f) * prng.RandfRange(0.8f, 1.15f);
			k.Blob(p, new Vector3(r * 1.3f, r * 0.7f, r), Seed * 53 + s, 0.25f, true);
		}
		// the roots the ground kept: from under the lip, up out of the bottom, snapped
		var ends = new List<(Vector3 p, Vector3 d, float r)>();
		for (int s = 0; s < 8; s++)
		{
			int i = prng.RandiRange(0, sec - 1);
			Vector3 o = f + Dir(i) * rad[i] * 0.95f * prng.RandfRange(1.15f, 1.35f);
			o.Y = Ground(o.X, o.Z) - 0.12f;
			Vector3 m = f + Dir(i) * rad[i] * 0.95f * 0.9f;
			m.Y = Ground(m.X, m.Z) + 0.12f;
			Vector3 e = f + Dir(i) * rad[i] * 0.95f * prng.RandfRange(0.45f, 0.7f) + new Vector3(prng.RandfRange(-0.2f, 0.2f), 0, prng.RandfRange(-0.2f, 0.2f));
			e.Y = Ground(e.X, e.Z) + prng.RandfRange(0.2f, 0.45f);
			float r = prng.RandfRange(0.04f, 0.09f);
			k.Mat(Bark);
			k.Color = Grey(prng.RandfRange(0.36f, 0.44f));
			BunkerKit.Tube(k, new List<Vector3> { o, m, e }, r, r * 0.7f, 5);
			ends.Add((e, (e - m).Normalized(), r * 0.7f));
		}
		k.Mat(Raw);
		k.Color = new Color(0.92f, 0.84f, 0.7f);
		foreach (var (p, d, r) in ends) k.Cylinder(p - d * 0.02f, p + d * 0.05f, r * 1.05f, r * 0.7f, 5, true, 1f);
	}

	/// <summary>The hole shows, with a burst of its earth (once, as the plate comes up off it).</summary>
	private void ShowHole()
	{
		if (_crater.Visible) return;
		_crater.Visible = true;
		TreeFallMotion.Dust(this, _standGround, new Color(0.11f, 0.085f, 0.06f, 0.5f), 1.6f, 22, 2.2f);
	}

	/// <summary>What the fall leaves on the ground (2026-10-15): limbs snapped off the crown and thrown, bark knocked off
	/// along the trunk, earth thrown from the hole. Shown when it lands.</summary>
	private void BuildDebris(MeshKit k, List<Vector3> axis, Vector3 dir)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 13 + 7) };
		Vector3 side = dir.Cross(Vector3.Up).Normalized();
		int n = axis.Count - 1;
		Vector3 Along(float t, float off)
		{
			float x = t * n; int i = Mathf.Clamp((int)x, 0, n - 1);
			Vector3 p = axis[i].Lerp(axis[i + 1], x - i) + side * off;
			p.Y = Ground(p.X, p.Z);
			return p;
		}
		float RAt(float t) => Mathf.Lerp(TrunkRadius, TrunkRadius * 0.3f, t);
		// broken limbs, grey and dead, lying on the ground either side
		k.Mat(Bark);
		for (int s = 0; s < 14; s++)
		{
			float t = rng.RandfRange(0.2f, 1f);
			float off = (rng.Randf() < 0.5f ? -1f : 1f) * (RAt(t) + rng.RandfRange(0.6f, t > CrownFrom ? 3.4f : 2.2f));
			Vector3 a = Along(t, off);
			float ang = rng.RandfRange(0f, Mathf.Tau), len = rng.RandfRange(0.6f, 1.7f);
			Vector3 h = new(Mathf.Cos(ang), 0, Mathf.Sin(ang));
			Vector3 b = a + h * len, m = a + h * len * 0.5f;
			float r = rng.RandfRange(0.025f, 0.055f);
			a.Y = Ground(a.X, a.Z) + r * 0.6f; b.Y = Ground(b.X, b.Z) + r * 0.5f; m.Y = Ground(m.X, m.Z) + r * 0.6f;
			k.Color = Grey(rng.RandfRange(0.55f, 0.68f));
			BunkerKit.Tube(k, new List<Vector3> { a, m, b }, r, r * 0.5f, 4);
			// a twig off it, up
			Vector3 tw = m + new Vector3(rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(0.2f, 0.45f), rng.RandfRange(-0.3f, 0.3f));
			BunkerKit.Tube(k, new List<Vector3> { m, tw }, r * 0.5f, 0.008f, 3);
		}
		// slabs of bark knocked off, beside the trunk
		for (int s = 0; s < 9; s++)
		{
			float t = rng.RandfRange(0.03f, 0.6f);
			Vector3 p = Along(t, (rng.Randf() < 0.5f ? -1f : 1f) * (RAt(t) + rng.RandfRange(0.25f, 1.2f)));
			float l = rng.RandfRange(0.18f, 0.4f);
			p.Y += 0.02f;
			k.Color = Grey(rng.RandfRange(0.5f, 0.62f));
			k.Xf = new Transform3D(new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)), Vector3.Zero);
			k.Blob(k.Xf.AffineInverse() * p, new Vector3(l, 0.025f, l * 0.45f), Seed * 61 + s, 0.2f, true);
			k.Xf = Transform3D.Identity;
		}
		// earth thrown out of the hole, behind it and to the sides
		k.Mat(Soil);
		Vector3 f = _standGround;
		for (int s = 0; s < 12; s++)
		{
			float ang = rng.RandfRange(-2.2f, 2.2f);
			Vector3 h = (-dir * Mathf.Cos(ang) + side * Mathf.Sin(ang)).Normalized();
			Vector3 p = f + h * rng.RandfRange(2.5f, 3.6f);
			float r = rng.RandfRange(0.07f, 0.17f);
			p.Y = Ground(p.X, p.Z) + r * 0.25f;
			k.Color = new Color(0.36f, 0.3f, 0.24f) * rng.RandfRange(0.8f, 1.15f);
			k.Blob(p, new Vector3(r * 1.3f, r * 0.7f, r), Seed * 67 + s, 0.25f, true);
		}
	}

	/// <summary>The dead crown: grey, needle-less branches heaped over the trail beyond,
	/// denser and more tangled toward the tip, too thick to push through.</summary>
	private void BuildCrown(MeshKit k, List<Vector3> axis, Vector3 dir, Vector3 side, RandomNumberGenerator rng, StaticBody3D body)
	{
		k.Mat(Bark);
		int n = axis.Count - 1;
		for (int i = Mathf.FloorToInt(n * CrownFrom); i <= n; i++)
		{
			float t = (float)i / n;
			Vector3 p = axis[i];
			int count = 5 + (int)(t * 4f);
			for (int b = 0; b < count; b++)
			{
				float a = rng.RandfRange(0f, Mathf.Tau);
				float len = Mathf.Lerp(3.2f, 1.6f, t) * rng.RandfRange(0.7f, 1.15f);
				// Branches splay round the trunk; those pointing into the ground are bent along it instead.
				var outDir = (side * Mathf.Cos(a) + Vector3.Up * Mathf.Sin(a) * 0.8f + dir * 0.55f).Normalized();
				var end = p + outDir * len;
				float g = Ground(end.X, end.Z) + 0.05f;
				if (end.Y < g) end.Y = g + rng.RandfRange(0f, 0.3f);
				var mid = p.Lerp(end, 0.5f) + Vector3.Up * rng.RandfRange(0.05f, 0.3f);
				float sh = rng.RandfRange(0.8f, 1f);   // silver-grey dead wood
				k.Color = Grey(sh * 0.68f);   // (2026-10-15: the woods' bark, as the trunk; it was the old drawn bark at 0.8 to 1)
				BunkerKit.Tube(k, new List<Vector3> { p, mid, end }, Mathf.Lerp(0.09f, 0.05f, t), 0.012f, 4);
				// a couple of twigs off each branch
				for (int tw = 0; tw < 2; tw++)
				{
					// on the branch as built (it bows up through mid), so the twig grows out of it
					float u = rng.RandfRange(0.45f, 0.85f);
					var from = u < 0.5f ? p.Lerp(mid, u * 2f) : mid.Lerp(end, (u - 0.5f) * 2f);
					var tdir = (outDir + new Vector3(rng.RandfRange(-0.8f, 0.8f), rng.RandfRange(-0.2f, 0.7f), rng.RandfRange(-0.8f, 0.8f))).Normalized();
					BunkerKit.Tube(k, new List<Vector3> { from, from + tdir * rng.RandfRange(0.4f, 0.9f) }, 0.022f, 0.006f, 3);
				}
			}
		}

		// Solid across the crown's spread, so the heap reads (and plays) as a wall of deadfall.
		int from0 = Mathf.FloorToInt(n * CrownFrom);
		for (int i = from0; i < n; i++)
		{
			var mid = (axis[i] + axis[i + 1]) * 0.5f;
			var fwd = (axis[i + 1] - axis[i]).Normalized();
			float gy = Ground(mid.X, mid.Z);
			body.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(4.6f, 2.4f, axis[i].DistanceTo(axis[i + 1]) + 0.2f) },
				Transform = new Transform3D(Basis.LookingAt(fwd, Vector3.Up), new Vector3(mid.X, gy + 0.9f, mid.Z)),
			});
		}
	}

	private void BuildClearZones(List<Vector3> axis)
	{
		for (int i = 0; i < axis.Count; i += 2)
		{
			var cz = new ClearZone { Radius = i == 0 ? 3.6f : 3f, ClearFoliage = false, Name = $"Clear{i}" };
			AddChild(cz);
			cz.GlobalPosition = axis[i];
		}
	}
}
