using System.Collections.Generic;
using Godot;
using ProjectDS.World.BossParts;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-14h, the old-and-abandoned pass) The pit's pipes as decades of neglect left them: the paint chipped to rust
/// (<see cref="BossTextures.Painted"/>), the lagging torn and hanging off in flaps, mineral drips grown under the
/// flanges and water dripping from them, and the old stencils in red paint. All from its own dice: the room's own
/// (which lay the pipes and pick the valves' order) are left as they were.
/// </summary>
public partial class BossRoom
{
	/// <summary>Every run of pipe laid (its line, its radius, its material, the wall's inward, its layer).</summary>
	private readonly List<(Vector3 centre, Vector3 along, float s0, float s1, float r, Material mat, Vector3 inward, int layer)> _runs = new();

	private void BuildPipeWear()
	{
		var rng = new RandomNumberGenerator { Seed = 18181 };
		var flaps = new MeshKit();
		var calcite = new MeshKit();
		calcite.Mat(new StandardMaterial3D { ResourceName = "pit_calcite", AlbedoColor = new Color(0.62f, 0.56f, 0.46f), Roughness = 0.6f, MetallicSpecular = 0.5f, VertexColorUseAsAlbedo = true });
		calcite.Color = Colors.White;
		var drips = new List<Vector3>();
		var flapMats = new Dictionary<Material, Material>();
		foreach (var (centre, along, s0, s1, r, mat, inward, layer) in _runs)
		{
			if (s1 - s0 < 1.2f) continue;
			bool lagged = mat == BossTextures.WrapMat(new Color(0.85f, 0.72f, 0.6f)) || mat == BossTextures.WrapMat(new Color(0.8f, 0.78f, 0.72f)) || mat == BossTextures.FoilMat;
			// the lagging: torn open here and there, a flap of it hanging down off the pipe
			if (lagged && r > 0.07f)
				for (float s = s0 + rng.RandfRange(0.5f, 4f); s < s1 - 0.4f; s += rng.RandfRange(3f, 9f))
				{
					if (rng.Randf() < 0.35f) continue;
					if (!flapMats.TryGetValue(mat, out var fm)) { fm = (Material)mat.Duplicate(); ((StandardMaterial3D)fm).CullMode = BaseMaterial3D.CullModeEnum.Disabled; flapMats[mat] = fm; }
					flaps.Mat(fm);
					flaps.Color = Colors.White * rng.RandfRange(0.55f, 0.85f);
					Vector3 p = centre + along * s;
					float w = rng.RandfRange(0.18f, 0.5f), len = rng.RandfRange(0.25f, 0.9f) * Mathf.Min(1f, r * 5f);
					// hung from the pipe's underside, swung out a little from the wall, twisted
					Vector3 top = p + Vector3.Down * r * 0.85f + inward * r * 0.3f;
					var rot = new Basis(along, rng.RandfRange(-0.35f, 0.15f)) * new Basis(Vector3.Up, rng.RandfRange(-0.25f, 0.25f));
					Vector3 down = rot * Vector3.Down, wide = rot * along;
					Vector3 n = wide.Cross(down).Normalized();
					Vector3 a = top - wide * w * 0.5f, b = top + wide * w * 0.5f;
					// two strips, the lower one bent further out, its end ragged
					Vector3 bend = (down + inward * 0.25f).Normalized() * len * 0.5f;
					flaps.Quad(a, b, b + down * len * 0.5f, a + down * len * 0.5f, n);
					flaps.Quad(a + down * len * 0.5f, b + down * len * 0.5f, b + down * len * 0.5f + bend * rng.RandfRange(0.6f, 1f), a + down * len * 0.5f + bend, n);
				}
			// under the flanges, where the joints weep: drips of mineral grown down, and water still dripping from some
			if (r > 0.06f)
				for (float s = s0 + 1.5f; s < s1 - 0.5f; s += rng.RandfRange(3f, 5f))
				{
					if (rng.Randf() < 0.55f) continue;
					Vector3 p = centre + along * (s + rng.RandfRange(-0.05f, 0.05f)) + Vector3.Down * r * 1.15f;
					int n = rng.RandiRange(1, 3);
					for (int i = 0; i < n; i++)
					{
						Vector3 q = p + along * rng.RandfRange(-0.06f, 0.06f) + inward * rng.RandfRange(-r * 0.4f, r * 0.4f);
						float l = rng.RandfRange(0.03f, 0.16f);
						calcite.Color = new Color(1f, 0.95f, 0.85f) * rng.RandfRange(0.6f, 1f);
						calcite.Cylinder(q + Vector3.Up * 0.02f, q + Vector3.Down * l, rng.RandfRange(0.008f, 0.02f), 0.0015f, 5, false);
					}
					if (drips.Count < 14 && rng.Randf() < 0.25f && p.Y > 1.5f) drips.Add(p);
				}
		}
		flaps.CommitTo(this, "LaggingFlaps", false);
		calcite.CommitTo(this, "Calcite", false);
		// the drips: a drop now and then off a joint, falling past the catwalk into the dark
		var dropMat = new StandardMaterial3D
		{
			ResourceName = "pit_drip", AlbedoColor = new Color(0.55f, 0.52f, 0.45f, 0.7f), Roughness = 0.1f, MetallicSpecular = 0.9f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
		};
		foreach (var p in drips)
			AddChild(new GpuParticles3D
			{
				Name = "Drip", Position = p, Amount = 3, Lifetime = 2.2, Randomness = 0.8f, Preprocess = 2.0,
				VisibilityAabb = new Aabb(new Vector3(-1, -26, -1), new Vector3(2, 28, 2)), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				ProcessMaterial = new ParticleProcessMaterial { Gravity = new Vector3(0, -9.8f, 0), InitialVelocityMin = 0f, InitialVelocityMax = 0.05f, Spread = 3f, Direction = Vector3.Down },
				DrawPass1 = new QuadMesh { Size = new Vector2(0.008f, 0.03f), Material = dropMat },
			});

		// the old stencils: red paint, faded, flaking, on the pipes and the frames (a little red that isn't light)
		string[] words = { "DANGER", "KEEP CLEAR", "No 3 SUMP", "DRAIN", "DANGER  HIGH PRESSURE", "NO ENTRY", "OUTFALL", "No 7 MAIN" };
		var font = new SystemFont { FontNames = new[] { "Stencil", "Arial Black", "Impact" } };
		for (int i = 0; i < words.Length; i++)
		{
			int w = i % 4;
			var (o, along, inw) = Wall(w);
			// between the valves, above them on the front pipes' faces
			float[] slots = { -8f, 0f, 8f, -15f, 15f };
			float s = slots[(i * 3 + w) % slots.Length] + rng.RandfRange(-1.2f, 1.2f);
			if (w == 2 && Mathf.Abs(s) < DoorClear + 1f) s += 3f;
			var l = new Label3D
			{
				Text = words[i], Font = font, FontSize = 64, PixelSize = 0.006f, OutlineSize = 0, Shaded = true,
				Modulate = new Color(0.5f, 0.09f, 0.06f, rng.RandfRange(0.55f, 0.8f)),
			};
			AddChild(l);
			l.Position = o + along * s + inw * 0.06f + Vector3.Up * rng.RandfRange(3.2f, 6.5f);
			l.Basis = Basis.LookingAt(-inw, Vector3.Up) * new Basis(Vector3.Forward, rng.RandfRange(-0.03f, 0.03f));
		}
	}
}

/// <summary>(2026-10-14h) A bare bulb in a wire cage on a long flex, swinging slowly: pushed by the air down here, never
/// still: its pool of light slides back and forth along the catwalk. (No shadows: an omni's six passes over the creature cost too much.)
/// Hangs from where it's put.</summary>
public partial class SwingingLamp : Node3D
{
	public float Length = 6f;
	private Node3D _pivot;
	private MeshInstance3D _flex;
	private float _t, _a = 0.12f, _b = 0.05f, _va, _vb;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1801 };

	public override void _Ready()
	{
		_pivot = new Node3D { Name = "Pivot" };
		AddChild(_pivot);
		_flex = new MeshInstance3D
		{
			Name = "Flex", Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.006f, Height = Length, RadialSegments = 4, Rings = 1 },
			Position = Vector3.Down * Length * 0.5f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = FailingBulb.Materials.Wire,
		};
		_pivot.AddChild(_flex);
		_pivot.AddChild(new FailingBulb
		{
			Name = "Bulb", Position = Vector3.Down * (Length + 0.1f), State = FailingBulb.Mode.Steady, Seed = 1802, Shadows = false,
			LightColor = new Color(1f, 0.7f, 0.4f), Energy = 1.1f, Range = 11f, Attenuation = 1.0f, Radius = 0.05f, ActiveRange = 80f,
		});
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_t += dt;
		// a pendulum each way (its own period from its length), a push of air now and then
		float g = 9.8f / Mathf.Max(1f, Length);
		_va += -g * Mathf.Sin(_a) * dt; _vb += -g * Mathf.Sin(_b) * dt;
		_va *= 1f - 0.02f * dt; _vb *= 1f - 0.02f * dt;
		if (_rng.Randf() < dt * 0.15f) { _va += _rng.RandfRange(-0.05f, 0.05f); _vb += _rng.RandfRange(-0.03f, 0.03f); }
		// (never quite stopped, never wild)
		if (Mathf.Abs(_a) < 0.03f && Mathf.Abs(_va) < 0.02f) _va += 0.04f;
		_a = Mathf.Clamp(_a + _va * dt, -0.3f, 0.3f); _b = Mathf.Clamp(_b + _vb * dt, -0.2f, 0.2f);
		_pivot.Rotation = new Vector3(_b, 0, _a);
	}
}
