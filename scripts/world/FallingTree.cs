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
				if (_t >= CreakSeconds) { State = Phase.Fall; _w = 0.04f; Event?.Invoke("crack"); }
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
		for (float a = 0.7f; a < 1.75f; a += 0.01f)
			for (float s = 2f; s <= length * 0.97f; s += 0.75f)
			{
				float u = s / length;
				Vector3 p = foot + dir * (s * Mathf.Sin(a)) + Vector3.Up * (s * Mathf.Cos(a));
				if (p.Y - Mathf.Lerp(footClear, tipClear, u) <= terrain.HeightAt(p.X, p.Z)) return a;
			}
		return 1.75f;
	}

	/// <summary>The heavy fall's sounds and the ground's shudder, played for a tree (sounds at its foot, mid-trunk, and
	/// where it lands); the shake gentle (the owner's motion comfort).</summary>
	public static void Sound(Node3D owner, string ev, Vector3 foot, Vector3 mid, Vector3 land, int seed)
	{
		int v(int n) => 1 + Mathf.PosMod(seed, n);
		switch (ev)
		{
			case "creak":
				Systems.StoryBeat.PlayAt(owner, $"res://assets/audio/sfx/tree_fall_creak_{v(3):00}.wav", "Events", owner.ToLocal(foot + Vector3.Up * 2f), 6f, 14f, 140f);
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

	/// <summary>A low burst of dust and needles where it landed, settling.</summary>
	private static void Dust(Node3D owner, Vector3 at)
	{
		var mat = new ParticleProcessMaterial
		{
			Direction = Vector3.Up, Spread = 80f, InitialVelocityMin = 0.8f, InitialVelocityMax = 2.6f, Gravity = new Vector3(0, -0.6f, 0),
			DampingMin = 1.2f, DampingMax = 2.2f, ScaleMin = 0.8f, ScaleMax = 1.8f,
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(3f, 0.2f, 3f),
			Color = new Color(0.28f, 0.25f, 0.2f, 0.4f),
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
			Amount = 28, Lifetime = 3.2f, OneShot = true, Explosiveness = 0.9f, ProcessMaterial = mat, DrawPass1 = quad,
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
	public bool Down => _m.State == TreeFallMotion.Phase.Down;
	public bool Falling => _m.State is TreeFallMotion.Phase.Creak or TreeFallMotion.Phase.Fall or TreeFallMotion.Phase.Settle;
	public TreeFallMotion Motion => _m;

	private readonly TreeFallMotion _m = new();
	private Node3D _pivot;
	private StaticBody3D _stand, _lie;
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
		FallDir = new Vector3(FallDir.X, 0, FallDir.Z).Normalized();
		_m.Length = Height;
		_m.End = _terrain != null ? TreeFallMotion.RestAngle(_terrain, GlobalPosition, FallDir, Height, TrunkR + 0.6f, 0.9f) : Mathf.Pi * 0.5f;
		_pivot = new Node3D { Name = "Pivot" };
		AddChild(_pivot);
		_pivot.AddChild(new MeshInstance3D { Name = "Tree", Mesh = Mesh, Rotation = new Vector3(0, Seed * 1.3f, 0) });
		// standing: the trunk
		_stand = new StaticBody3D { Name = "Standing", CollisionLayer = 1, CollisionMask = 0 };
		_stand.AddChild(new CollisionShape3D { Position = Vector3.Up * 3f, Shape = new CylinderShape3D { Radius = TrunkR, Height = 6f } });
		AddChild(_stand);
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

	private void Landed()
	{
		_stand.CollisionLayer = 0;
		_lie.CollisionLayer = 1;
		// never pinned under it: a player inside its length is put out beside it
		if (GetTree().GetFirstNodeInGroup("player") is PlayerController p)
		{
			var axisDir = (FallDir * Mathf.Sin(_m.End) + Vector3.Up * Mathf.Cos(_m.End)).Normalized();
			Vector3 rel = p.GlobalPosition - GlobalPosition;
			float along = rel.Dot(FallDir);
			Vector3 side = Vector3.Up.Cross(FallDir).Normalized();
			float off = rel.Dot(side);
			if (along > -1f && along < Height && Mathf.Abs(off) < 2.2f)
			{
				var to = p.GlobalPosition + side * ((off >= 0 ? 1f : -1f) * 2.6f - off);
				to.Y = (_terrain?.HeightAt(to.X, to.Z) ?? to.Y) + 0.05f;
				p.GlobalPosition = to;
			}
		}
	}

	public override void _Process(double delta)
	{
		if (_m.State is TreeFallMotion.Phase.Standing or TreeFallMotion.Phase.Down) return;
		_m.Step((float)delta);
		Pose();
	}

	private void Pose()
	{
		var axis = Vector3.Up.Cross(FallDir).Normalized();
		_pivot.Basis = new Basis(axis, _m.Angle);
	}
}
