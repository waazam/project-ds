using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Napalm burning on a body (2026-10-14d: the wendigo in Act 24, the pyre it falls into, ending 3's charge). It was one
/// FireVfx box stood at the body's middle: a smooth teardrop of fire beside the creature, left behind as it turned and ran.
/// Now the fire clings to the body itself: tongues of burning fuel (napalm_tongue.gdshader) riding its bones (the spine,
/// the shoulders, the arms, the thighs, the head), standing up off them whatever the pose, with oily black smoke pouring
/// off its back (FireVfx's smoke, darkened), embers, and a light in its chest that breathes with the burning.
/// <see cref="Intensity"/> 0 .. 1, live.
/// </summary>
public partial class NapalmBurn : Node3D
{
	private float _intensity = 1f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Intensity { get => _intensity; set => _intensity = Mathf.Clamp(value, 0f, 1f); }
	[Export] public float LightEnergy = 2f, LightRange = 7f;
	/// <summary>How big the body is (the wendigo's 0.66 in the maze, 1 at full size).</summary>
	[Export] public float Size = 1f;

	private readonly List<(Node3D at, MultiMesh mm, MultiMeshInstance3D mi, float scale, float seed)> _spots = new();
	private OmniLight3D _light;
	private FireVfx _smoke;
	private float _shown = -1f, _clock;
	private static readonly string[] Wanted = { "chest", "spine", "neck", "head", "shoulder", "upper_arm", "forearm", "arm", "thigh", "shin", "hips", "pelvis" };

	public override void _Ready()
	{
		var skel = FindSkel(GetParent());
		var mat = (ShaderMaterial)NapalmStream.TongueMaterial().Duplicate();
		var rng = new RandomNumberGenerator { Seed = 2414 };
		if (skel != null)
		{
			int taken = 0;
			for (int b = 0; b < skel.GetBoneCount() && taken < 16; b++)
			{
				string n = skel.GetBoneName(b).ToLowerInvariant();
				bool want = false;
				foreach (var w in Wanted) if (n.Contains(w)) { want = true; break; }
				if (!want || n.Contains("twist") || n.Contains("end") || n.Contains("finger") || n.Contains("toe")) continue;
				var att = new BoneAttachment3D { Name = $"Burn_{skel.GetBoneName(b)}", BoneName = skel.GetBoneName(b) };
				skel.AddChild(att);
				float s = n.Contains("chest") || n.Contains("spine") || n.Contains("hips") || n.Contains("pelvis") ? 1.2f : n.Contains("head") || n.Contains("neck") ? 0.8f : 0.7f;
				AddSpot(att, mat, s, rng);
				taken++;
			}
			GD.Print($"[burn] napalm on {taken} bones of {GetParent()?.Name}");
		}
		if (_spots.Count == 0)
		{
			// (no skeleton: a few tongues over the body's middle, as before, but napalm's)
			for (int k = 0; k < 3; k++)
			{
				var at = new Node3D { Name = $"BurnSpot{k}", Position = new Vector3(rng.RandfRange(-0.3f, 0.3f), 0.8f + k * 0.5f, rng.RandfRange(-0.2f, 0.2f)) * Size };
				AddChild(at);
				AddSpot(at, mat, 1.2f, rng);
			}
		}
		_light = new OmniLight3D { Name = "BurnLight", LightColor = new Color(1f, 0.5f, 0.18f), OmniRange = LightRange, OmniAttenuation = 1.3f, ShadowEnabled = false, Position = new Vector3(0, 1.4f * Size, 0.3f * Size) };
		AddChild(_light);
		// the smoke pouring off it: FireVfx's smoke-only source, black
		_smoke = new FireVfx { Name = "BurnSmoke", Extent = new Vector3(0.5f, 0.6f, 0.5f) * Size, FlameScale = 0f, Smoke = true, SmokeAmount = 1.2f, EmitLight = false, Haze = false, Embers = false, Position = new Vector3(0, 2f * Size, 0), Dark = true };
		AddChild(_smoke);
	}

	private void AddSpot(Node3D at, ShaderMaterial mat, float scale, RandomNumberGenerator rng)
	{
		const int N = 3;
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 8 }, InstanceCount = N };
		float seed = rng.Randf();
		for (int i = 0; i < N; i++) mm.SetInstanceCustomData(i, new Color(Mathf.PosMod(seed + i * 0.31f, 1f), 0.4f + 0.3f * i, 0.04f, 1f));
		var mi = new MultiMeshInstance3D { Name = "Tongues", Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true, CustomAabb = new Aabb(-Vector3.One * 2f, Vector3.One * 4f) };
		at.AddChild(mi);
		_spots.Add((at, mm, mi, scale, seed));
	}

	private static Skeleton3D FindSkel(Node n)
	{
		if (n == null) return null;
		if (n is Skeleton3D s) return s;
		foreach (var c in n.GetChildren()) if (FindSkel(c) is { } f) return f;
		return null;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_clock += dt;
		_shown = _shown < 0f ? _intensity : Mathf.MoveToward(_shown, _intensity, dt * 0.8f);
		float k = _shown;
		foreach (var (at, mm, mi, scale, seed) in _spots)
		{
			if (!IsInstanceValid(at)) continue;
			mi.Visible = k > 0.02f;
			if (!mi.Visible) continue;
			// standing up off the bone in world space (fire rises whatever the limb is doing), the tongues about the bone
			mi.GlobalTransform = new Transform3D(Basis.Identity, at.GlobalPosition);
			for (int i = 0; i < 3; i++)
			{
				float a = seed * 30f + i * 2.1f;
				float h = Size * scale * (0.6f + 0.9f * k) * (i == 0 ? 1f : 0.7f) * (0.85f + 0.2f * Mathf.Sin(_clock * 1.9f + a));
				float w = Size * scale * (0.28f + 0.16f * k) * (i == 0 ? 1f : 0.75f);
				var off = new Vector3(Mathf.Cos(a), -0.15f, Mathf.Sin(a)) * Size * scale * (i == 0 ? 0.03f : 0.12f);
				mm.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, a) * Basis.FromScale(new Vector3(w, h, w)), off));
				mm.SetInstanceCustomData(i, new Color(Mathf.PosMod(seed + i * 0.31f, 1f), 0.4f + 0.3f * i, 0.04f, k));
			}
		}
		if (_light != null)
		{
			_light.Visible = k > 0.02f;
			float flick = 0.85f + 0.1f * Mathf.Sin(_clock * 6.3f) * Mathf.Sin(_clock * 3.7f + 1f) + 0.05f * Mathf.Sin(_clock * 11f);
			_light.LightEnergy = LightEnergy * k * flick;
		}
		if (_smoke != null) _smoke.Intensity = k;
	}
}
