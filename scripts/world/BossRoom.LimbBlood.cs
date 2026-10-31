using System.Collections.Generic;
using Godot;
using ProjectDS.Entities;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-31, the owner: "when the lovecraftian monster in act 18 boss fight has the blood it is floating in start as high
/// as we can get it so its like right underneath the railing ... implement the blood splashing when the tentacles smack
/// down so that blood sprays on the walls and the platform") The blood now starts a hand under the deck
/// (<see cref="BossRoom.BloodStart"/>), so every limb comes up out of it: a column of it thrown up where one tears out of
/// the surface, and blood streaming off it, running from its tip in ropes and beads, all the while it's raised over them.
/// (The blow's own splash on the deck, the wall, the pipes and the valves is BossRoom.Blood.cs.)
/// </summary>
public partial class BossRoom
{
	private readonly Dictionary<Leviathan.Tentacle, Leviathan.Tentacle.S> _limbWas = new();
	private readonly Dictionary<Leviathan.Tentacle, GpuParticles3D> _limbDrips = new();

	private void UpdateLimbBlood()
	{
		if (Beast == null || !IsInstanceValid(Beast)) return;
		foreach (var t in Beast.Tentacles)
		{
			_limbWas.TryGetValue(t, out var was);
			_limbWas[t] = t.State;
			bool up = t.State is Leviathan.Tentacle.S.Raise or Leviathan.Tentacle.S.Hold or Leviathan.Tentacle.S.Strike;
			// torn up out of the pool: where it breaks the surface, a column of blood thrown up and falling back
			if (t.State == Leviathan.Tentacle.S.Raise && was != Leviathan.Tentacle.S.Raise && BloodY > PitFloor + 1f)
			{
				var surface = Beast.ToGlobal(t.RootLocal with { Y = BloodY - PitFloor }) + Beast.GlobalBasis * (t.RootLocal with { Y = 0 }).Normalized() * 1.5f;
				surface.Y = ToGlobal(new Vector3(0, BloodY, 0)).Y;
				Blood.Burst(this, surface, GlobalBasis.Y, 70, 7.5f, 28f);
				Blood.Burst(this, surface, GlobalBasis.Y, 30, 3.5f, 75f);
				Sfx("boss_limb_rise", 3, surface, 2f, 8f);
			}
			// raised: running with it, dripping off the tip
			if (!_limbDrips.TryGetValue(t, out var drip) || !IsInstanceValid(drip))
			{
				drip = Dripper();
				AddChild(drip);
				_limbDrips[t] = drip;
			}
			drip.Emitting = up && BloodY > PitFloor + 1f;
			if (up) drip.GlobalPosition = Beast.ToGlobal(t.Tip);
		}
	}

	/// <summary>Beads and ropes of blood running off a limb, falling (the blood system's own drops).</summary>
	private static GpuParticles3D Dripper() => new()
	{
		Name = "LimbDrip", Amount = 26, Lifetime = 1.6, Explosiveness = 0f, Randomness = 0.8f, LocalCoords = false, Emitting = false,
		ProcessMaterial = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.35f,
			Direction = Vector3.Down, Spread = 25f, InitialVelocityMin = 0.2f, InitialVelocityMax = 1.2f, Gravity = new Vector3(0, -11f, 0),
			ScaleMin = 0.6f, ScaleMax = 1.6f, ParticleFlagAlignY = true,
		},
		DrawPass1 = new CapsuleMesh { Radius = 0.016f, Height = 0.16f, RadialSegments = 5, Rings = 2, Material = Blood.BeadMat },
		CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		VisibilityAabb = new Aabb(new Vector3(-6, -30, -6), new Vector3(12, 34, 12)),
	};
}
