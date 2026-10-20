using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// How a tree comes down (Act 1, 2026-10-03; the owner: "The physics of them should drop them like a dead weight and
/// not have them floaty or rolling around, they need to seem heavy"): not left to the physics engine, which floats
/// and rolls them, but a hinge at the foot. First the creak: it leans a few degrees, groaning and ticking, as the hinge
/// wood gives. Then the crack, and it goes: a rod pivoting on its foot under gravity (its angular acceleration
/// 3g/2L sin of its lean, a little heavier than true), slow at first and faster and faster. It stops dead where it
/// meets the ground, with only a short, damped jolt back off it, and lies there.
/// </summary>
public sealed class TreeFallMotion
{
	public enum Phase { Standing, Creak, Fall, Settle, Down }
	public Phase State { get; private set; } = Phase.Standing;
	/// <summary>Its lean from upright (radians), and where it comes to rest.</summary>
	public float Angle { get; private set; }
	public float End;
	public float Length = 20f;
	public float CreakSeconds = 2.3f;
	/// <summary>"creak", "crack", "impact", "down".</summary>
	public Action<string> Event;
	/// <summary>Held at the end of its creak, leaning and groaning, instead of cracking (2026-10-13: a fall waits to be
	/// seen; the creak is what turns the player's head). Let go, it cracks at once.</summary>
	public bool Hold;
	/// <summary>Seconds since the creak began (it keeps counting while held).</summary>
	public float CreakTime => State == Phase.Creak ? _t : 0f;
	private float _t, _w;

	public void Start()
	{
		if (State != Phase.Standing) return;
		State = Phase.Creak;
		_t = 0f;
		Event?.Invoke("creak");
	}

	public void SetDown()
	{
		State = Phase.Down;
		Angle = End;
	}

	public void Step(float dt)
	{
		switch (State)
		{
			case Phase.Creak:
			{
				_t += dt;
				float u = Mathf.Clamp(_t / CreakSeconds, 0f, 1f);
				// leaning over a few degrees as the hinge wood gives, in fits
				Angle = 0.045f * u * u + 0.004f * Mathf.Sin(_t * 9f) * u;
				// held: it hangs there at its lean, giving a little in slow fits, until it's let go
				if (_t >= CreakSeconds && Hold) Angle += 0.006f * (0.5f - 0.5f * Mathf.Cos((_t - CreakSeconds) * 1.3f));
				if (_t >= CreakSeconds && !Hold) { State = Phase.Fall; _w = 0.04f; Event?.Invoke("crack"); }
				break;
			}
			case Phase.Fall:
			{
				// a rod pivoting on its foot (a touch heavier than true: it has to read as weight)
				float k = 3f * 9.8f / (2f * Mathf.Max(Length, 4f)) * 1.5f;
				_w += k * Mathf.Sin(Angle) * dt;
				Angle += _w * dt;
				if (Angle >= End)
				{
					Angle = End;
					State = Phase.Settle;
					_t = 0f;
					Event?.Invoke("impact");
				}
				break;
			}
			case Phase.Settle:
			{
				// dead weight: a short jolt back off the ground, damped out at once; no rolling
				_t += dt;
				Angle = End - 0.016f * Mathf.Exp(-_t * 9f) * Mathf.Abs(Mathf.Sin(_t * 26f));
				if (_t >= 0.45f) { Angle = End; State = Phase.Down; Event?.Invoke("down"); }
				break;
			}
		}
	}

	/// <summary>The lean at which a straight trunk from <paramref name="foot"/> along <paramref name="dir"/> (level) first
	/// meets the ground, given the terrain and the trunk's clearance along it.</summary>
	public static float RestAngle(ForestTerrain terrain, Vector3 foot, Vector3 dir, float length, float footClear, float tipClear)
	{
		// (past level, for one falling down a slope: it lies along the ground going away below it)
		// (its top decides where it rests, coming down on the ground at nine tenths of its height: the trunk, the crown and
		// the very tip may press into a rise rather than prop the whole tree up off it, the owner: "skillfully clipped through
		// the map so they look like they fall flat". With the upper half, the crown or the last tenth deciding, a rise under
		// them held the top up: 3.1 m, 0.99, 0.55)
		for (float a = 0.7f; a < 2.25f; a += 0.003f)   // (fine: a hundredth of a radian is 20 cm at a tall tip)
		{
			float s = length * 0.9f;
			Vector3 p = foot + dir * (s * Mathf.Sin(a)) + Vector3.Up * (s * Mathf.Cos(a));
			if (p.Y - Mathf.Lerp(footClear, tipClear, 0.9f) <= terrain.HeightAt(p.X, p.Z)) return a;
		}
		return 2.25f;
	}

	/// <summary>The heavy fall's sounds and the ground's shudder, played for a tree (sounds at its foot, mid-trunk, and
	/// where it lands); the shake gentle (the owner's motion comfort).</summary>
	public static void Sound(Node3D owner, string ev, Vector3 foot, Vector3 mid, Vector3 land, int seed)
	{
		int v(int n) => 1 + Mathf.PosMod(seed, n);
		switch (ev)
		{
			case "creak":
				if (owner is FallingTree { QuietCreak: true }) break;
				Systems.StoryBeat.PlayAt(owner, $"res://assets/audio/sfx/tree_fall_creak_{NextCreak():00}.wav", "Events", owner.ToLocal(foot + Vector3.Up * 2f),
					_groanRng.RandfRange(2f, 6f), 14f, 140f, _groanRng.RandfRange(0.88f, 1.08f));
				break;
			case "crack":
				Systems.StoryBeat.PlayAt(owner, $"res://assets/audio/sfx/tree_fall_crack_{v(2):00}.wav", "Events", owner.ToLocal(foot + Vector3.Up * 1.5f), 8f, 16f, 180f);
				Systems.StoryBeat.PlayAt(owner, $"res://assets/audio/sfx/tree_fall_rush_{v(2):00}.wav", "Events", owner.ToLocal(mid), 2f, 14f, 140f);
				break;
			case "impact":
				Systems.StoryBeat.PlayAt(owner, $"res://assets/audio/sfx/tree_fall_impact_{v(3):00}.wav", "Events", owner.ToLocal(land), 8f, 20f, 240f);
				Shudder(owner, land);
				Dust(owner, land);
				break;
		}
	}

	private static readonly RandomNumberGenerator _groanRng = new();
	private static Audio.RecentPicker _creakPick;
	public const int CreakTakes = 8;

	/// <summary>The next creak take (1-based), never one of the last three heard.</summary>
	private static int NextCreak() => 1 + _creakPick.Next(_groanRng, CreakTakes);

	/// <summary>A held tree groaning again while it waits to be seen (2026-10-13): another of the creak takes each time
	/// (never the one just heard, the owner's no-repeats rule), its pitch a little different, a little quieter the more
	/// it has groaned.</summary>
	public static void Groan(Node3D owner, Vector3 foot, int seed, int count)
	{
		int take = NextCreak();
		float db = 5f - Mathf.Min(count, 4) * 1.2f;
		Systems.StoryBeat.PlayAt(owner, $"res://assets/audio/sfx/tree_fall_creak_{take:00}.wav", "Events", owner.ToLocal(foot + Vector3.Up * 2.5f), db, 14f, 140f,
			_groanRng.RandfRange(0.86f, 1.06f));
	}

	/// <summary>The ground shudders under you, a little, by how near it came down.</summary>
	private static void Shudder(Node3D owner, Vector3 land)
	{
		if (owner.GetTree().GetFirstNodeInGroup("player") is not PlayerController p || p.CameraRig is not { } rig) return;
		float near = Mathf.Clamp(1f - p.GlobalPosition.DistanceTo(land) / 45f, 0f, 1f);
		if (near <= 0f) return;
		var rng = new RandomNumberGenerator { Seed = (ulong)Mathf.Abs(land.X * 100 + land.Z) };
		float amp = 0.035f * near;
		var tw = owner.CreateTween();
		tw.TweenMethod(Callable.From<float>(v =>
		{
			if (GodotObject.IsInstanceValid(rig)) rig.Shake = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), 0f) * amp * v * v;
		}), 1f, 0f, 0.45f);
		tw.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(rig)) rig.Shake = Vector3.Zero; }));
	}

	/// <summary>A low burst of dust and needles where it landed, settling (or, given a colour and a spread, another burst:
	/// the dead fir's earth tearing up with its roots, 2026-10-15).</summary>
	internal static void Dust(Node3D owner, Vector3 at, Color? color = null, float spread = 3f, int amount = 28, float lift = 2.6f)
	{
		var mat = new ParticleProcessMaterial
		{
			Direction = Vector3.Up, Spread = 80f, InitialVelocityMin = 0.8f, InitialVelocityMax = lift, Gravity = new Vector3(0, -0.6f, 0),
			DampingMin = 1.2f, DampingMax = 2.2f, ScaleMin = 0.8f, ScaleMax = 1.8f,
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(spread, 0.2f, spread),
			Color = color ?? new Color(0.28f, 0.25f, 0.2f, 0.4f),
		};
		var quad = new QuadMesh
		{
			Size = new Vector2(1.6f, 1.6f),
			Material = new StandardMaterial3D
			{
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
				AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/weather/smoke_sheet.png") is { } t ? t : null,
				ParticlesAnimHFrames = 4, ParticlesAnimVFrames = 4, ParticlesAnimLoop = false,
			},
		};
		mat.AnimSpeedMin = mat.AnimSpeedMax = 1f;
		var p = new GpuParticles3D
		{
			Amount = amount, Lifetime = 3.2f, OneShot = true, Explosiveness = 0.9f, ProcessMaterial = mat, DrawPass1 = quad,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		owner.AddChild(p);
		p.GlobalPosition = at + Vector3.Up * 0.4f;
		p.Emitting = true;
		owner.GetTree().CreateTimer(4.0).Timeout += () => { if (GodotObject.IsInstanceValid(p)) p.QueueFree(); };
	}
}

/// <summary>
/// One of Act 1's falling trees (2026-10-03): a fir standing beside the way to the stairs that comes down when it's
/// told to, along <see cref="FallDir"/>, and lies there as a wall (or across the way behind you). Its foot is its
/// position. While it stands, its trunk is solid; once down, the whole length of it is, too tall to climb over.
/// The scatter's trees are kept out of where it will lie.
/// </summary>
public partial class FallingTree : Node3D
{
	public Vector3 FallDir = Vector3.Forward;
	public Mesh Mesh;
	public float Height = 22f;
	public float TrunkR = 0.4f;
	public int Seed = 1;
	/// <summary>Comes down without its creak (2026-10-15, the owner: "we dont need to hear every tree creak"): only the
	/// crack, the rush and the landing.</summary>
	public bool QuietCreak;
	/// <summary>A point on the side of it the player belongs (the way they're kept to, 2026-10-19). Caught inside its length
	/// as it lands, they're put out on this side, never across it; unset, on the nearer side.</summary>
	public Vector3? KeepToward;
	public bool Down => _m.State == TreeFallMotion.Phase.Down;
	public bool Falling => _m.State is TreeFallMotion.Phase.Creak or TreeFallMotion.Phase.Fall or TreeFallMotion.Phase.Settle;
	public TreeFallMotion Motion => _m;

	private readonly TreeFallMotion _m = new();
	private Node3D _pivot;
	private StaticBody3D _stand, _lie;
	/// <summary>For tests: how high over the ground the trunk lies, nine tenths of the way to its tip, once down.</summary>
	public float RestTipHeight { get; private set; }
	private ForestTerrain _terrain;

	/// <summary>Keeps the scatter's trees out of its foot and its lie (call before the scatter builds).</summary>
	public void Reserve(Node parent, ForestTerrain terrain, Vector3 foot)
	{
		for (float s = 0f; s <= Height * 0.95f; s += 2.5f)
		{
			var cz = new ClearZone { Radius = s < 2f ? 1.6f : 1.4f + 1.4f * s / Height, ClearFoliage = false, Name = $"ClearFall{Seed}_{(int)s}" };
			parent.AddChild(cz);
			var p = foot + FallDir * s;
			cz.GlobalPosition = new Vector3(p.X, terrain.HeightAt(p.X, p.Z), p.Z);
		}
	}

	public override void _Ready()
	{
		_terrain = GroundSnap.FindTerrain(this);
		// (its fall and its lying collision are measured from where it stands now: placed after it readies, they were laid at
		// the origin's ground, and the player walked through the fallen trunk)
		if (GlobalPosition.IsEqualApprox(Vector3.Zero)) GD.PushWarning($"FallingTree {Name} readied at the origin: set its Position before AddChild");
		FallDir = new Vector3(FallDir.X, 0, FallDir.Z).Normalized();
		_m.Length = Height;
		// (down until the trunk itself lies on the ground, the boughs under it pressed into the earth: with room left for them it
		// came to rest a metre up, propped on its own branches; the owner saw it)
		_m.End = _terrain != null ? TreeFallMotion.RestAngle(_terrain, GlobalPosition, FallDir, Height, TrunkR + 0.04f, 0.12f) : Mathf.Pi * 0.5f;
		if (_terrain != null)
		{
			var tip = GlobalPosition + FallDir * (Height * 0.9f * Mathf.Sin(_m.End)) + Vector3.Up * (Height * 0.9f * Mathf.Cos(_m.End));
			RestTipHeight = tip.Y - _terrain.HeightAt(tip.X, tip.Z);
		}
		_pivot = new Node3D { Name = "Pivot" };
		AddChild(_pivot);
		_pivot.AddChild(new MeshInstance3D { Name = "Tree", Mesh = Mesh, Rotation = new Vector3(0, Seed * 1.3f, 0) });
		// standing: the trunk; on the hinge, so as it falls its solid leans with it (2026-10-19: it stood upright at the foot
		// while the trunk came down through the air beside it)
		_stand = new AnimatableBody3D { Name = "Standing", CollisionLayer = 1, CollisionMask = 0 };
		_stand.AddChild(new CollisionShape3D { Position = Vector3.Up * 3f, Shape = new CylinderShape3D { Radius = TrunkR, Height = 6f } });
		_pivot.AddChild(_stand);
		// down: its length, a chain of boxes too tall to climb, wider over the crown
		_lie = new StaticBody3D { Name = "Lying", CollisionLayer = 0, CollisionMask = 0 };
		_lie.SetMeta("surface", "wood");
		AddChild(_lie);
		var axisDir = (FallDir * Mathf.Sin(_m.End) + Vector3.Up * Mathf.Cos(_m.End)).Normalized();
		const float step = 2f;
		for (float s = 0f; s < Height * 0.92f; s += step)
		{
			Vector3 a = axisDir * s, b = axisDir * Mathf.Min(s + step, Height * 0.92f);
			Vector3 mid = (a + b) * 0.5f;
			Vector3 wmid = GlobalPosition + mid;
			float gy = _terrain?.HeightAt(wmid.X, wmid.Z) ?? GlobalPosition.Y;
			float u = s / Height;
			float width = u < 0.35f ? TrunkR * 2f + 0.4f : Mathf.Lerp(3.2f, 1.6f, (u - 0.35f) / 0.6f);
			float top = Mathf.Max(wmid.Y + width * 0.4f, gy + 1.35f) - GlobalPosition.Y;
			float bottom = gy - 0.4f - GlobalPosition.Y;
			var fwd = (b - a); fwd.Y = 0;
			if (fwd.LengthSquared() < 1e-4f) continue;
			_lie.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(width, top - bottom, fwd.Length() + 0.3f) },
				Transform = new Transform3D(Basis.LookingAt(fwd.Normalized(), Vector3.Up), new Vector3(mid.X, (top + bottom) * 0.5f, mid.Z)),
			});
		}
		_m.Event = ev =>
		{
			var land = GlobalPosition + axisDir * Height * 0.55f;
			TreeFallMotion.Sound(this, ev, GlobalPosition, GlobalPosition + Vector3.Up * Height * 0.5f, land, Seed);
			if (ev == "crack") Brace();
			if (ev == "impact") Landed();
		};
		Pose();
	}

	/// <summary>Down at once (a Continue past it).</summary>
	public void SetDown()
	{
		_m.SetDown();
		Landed();
		Pose();
	}

	public void Fall() => _m.Start();

	/// <summary>Starts its creak; held, it hangs at the end of it until <see cref="Release"/> (2026-10-13).</summary>
	public void Fall(bool hold, float creakSeconds)
	{
		_m.CreakSeconds = creakSeconds;
		_m.Hold = hold;
		_m.Start();
	}

	public void Release() => _m.Hold = false;
	public bool Held => _m.State == TreeFallMotion.Phase.Creak && _m.Hold;

	/// <summary>Where it will lie: its foot and nine tenths of the way to its top, laid on the ground.</summary>
	public (Vector3 a, Vector3 b) LieSegment => (GlobalPosition, GlobalPosition + FallDir * Height * 0.95f);

	/// <summary>Points along its trunk as it stands now (for whether it's seen): low, middle, high.</summary>
	public Vector3 TrunkPoint(float u)
	{
		var axis = Vector3.Up.Cross(FallDir).Normalized();
		return GlobalPosition + (new Basis(axis, _m.Angle) * Vector3.Up) * (Height * u);
	}

	/// <summary>The player is inside where it lies (along its length, within its width), and how far off its axis.</summary>
	private bool Inside(PlayerController p, out float off)
	{
		Vector3 rel = p.GlobalPosition - GlobalPosition;
		float along = rel.Dot(FallDir);
		off = rel.Dot(Vector3.Up.Cross(FallDir).Normalized());
		return along > -1f && along < Height && Mathf.Abs(off) < 2.2f;
	}

	private PlayerController Player => GetTree().GetFirstNodeInGroup("player") as PlayerController;

	/// <summary>As it cracks, where it will lie turns solid (2026-10-19): there was nothing there through the fall, and
	/// the player could step in under it and be put out on its far side as it landed. Not while they're in there.</summary>
	private void Brace()
	{
		if (_lie.CollisionLayer != 0) return;
		if (Player is { } p && Inside(p, out _)) return;
		_lie.CollisionLayer = 1;
	}

	private void Landed()
	{
		_stand.CollisionLayer = 0;
		// never pinned under it: a player inside its length is put out beside it, on their own side of it
		if (Player is { } p && Inside(p, out float off))
		{
			Vector3 side = Vector3.Up.Cross(FallDir).Normalized();
			float sign = off >= 0 ? 1f : -1f;
			if (KeepToward is { } keep)
			{
				float k = (keep - GlobalPosition).Dot(side);
				if (Mathf.Abs(k) > 0.3f) sign = k >= 0 ? 1f : -1f;
			}
			var to = p.GlobalPosition + side * (sign * 2.6f - off);
			to.Y = (_terrain?.HeightAt(to.X, to.Z) ?? to.Y) + 0.05f;
			p.GlobalPosition = to;
		}
		_lie.CollisionLayer = 1;
	}

	public override void _Process(double delta)
	{
		if (_m.State is TreeFallMotion.Phase.Standing or TreeFallMotion.Phase.Down) return;
		_m.Step((float)delta);
		// (inside it as it cracked: solid the moment they step out)
		if (_m.State == TreeFallMotion.Phase.Fall) Brace();
		Pose();
	}

	private void Pose()
	{
		var axis = Vector3.Up.Cross(FallDir).Normalized();
		_pivot.Basis = new Basis(axis, _m.Angle);
	}
}
