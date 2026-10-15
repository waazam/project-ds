using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// The air the Act 7 giant stands in (2026-10-15, the owner: "the giant stalker should blend more with the background as
/// well maybe add fog?"). It was drawn fog-free with a hand-set near-black haze: a sharp black cut-out over the trees.
/// Now it sits in the scene's own air:
/// <list type="bullet">
/// <item>its haze is the scene's fog at its distance (the Environment's own fog colour and density, read every frame, as
/// the storm and the fire change them), held a step darker than the fog so it still reads as a shape;</item>
/// <item>giant_skin softens its silhouette (the edges going over to the haze, a dither at the rim), lifts it a little
/// toward the air with height, and keeps its eyes the one crisp thing;</item>
/// <item>banks of mist (fog_bank.gdshader) drift along the far tree line in front of its legs and torso, so only parts
/// of it come up out of the murk.</item>
/// </list>
/// <see cref="Look"/> picks how much of it (the three looks tried: the shadow, veiled, and aerial).
/// </summary>
public partial class GiantStalkerEvent
{
	public enum AirLook { Shadow, Veiled, Aerial }
	/// <summary>Shadow: in the scene's air, soft-edged, no mist. Veiled: that and mist along the tree line. Aerial: veiled,
	/// lighter and further into the air.</summary>
	[Export] public AirLook Look = AirLook.Veiled;
	[Export] public int FogBanks = 6;

	private sealed partial class Air : Node3D
	{
		public readonly List<(MeshInstance3D mesh, ShaderMaterial mat, Vector3 home, float drift)> Banks = new();
		public Vector3 Along;
		public double T;
	}

	private Air AttachAir(StalkerBody body, ShaderMaterial skin, Vector3 fwd)
	{
		// (dev: `--giant-look=shadow|veiled|aerial` to compare them)
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--giant-look=") && System.Enum.TryParse<AirLook>(a["--giant-look=".Length..], true, out var l)) Look = l;
		var air = new Air { Name = "GiantAir", Along = fwd.Cross(Vector3.Up).Normalized() };
		Cutscene.SceneRoot(this).AddChild(air);
		skin.SetShaderParameter("edge_soft", Look == AirLook.Shadow ? 0.55f : 0.7f);
		skin.SetShaderParameter("height_lift", Look == AirLook.Aerial ? 0.22f : 0.1f);
		skin.SetShaderParameter("eye_keep", 0.75f);
		if (Look == AirLook.Shadow) return air;
		var shader = GD.Load<Shader>("res://assets/shaders/fog_bank.gdshader");
		var terrain = GroundSnap.FindTerrain(this);
		Vector3 c = body.GlobalPosition + fwd * TurnRadius;   // the middle of its loop
		for (int i = 0; i < FogBanks; i++)
		{
			// across the lane and a little either side of it, in front of it (toward the lookout) and among its lanes,
			// low over the crowns: its legs and torso go in and out of them
			float u = FogBanks > 1 ? (float)i / (FogBanks - 1) : 0.5f;
			Vector3 at = c + air.Along * Mathf.Lerp(-SweepWidth * 0.75f, SweepWidth * 0.75f, u) + fwd * _rng.RandfRange(-TurnRadius * 1.6f, TurnRadius * 0.4f);
			float ground = terrain?.HeightAt(at.X, at.Z) ?? at.Y;
			at.Y = ground + BodyScale * _rng.RandfRange(0.95f, 1.35f);
			var mat = new ShaderMaterial { Shader = shader };
			mat.SetShaderParameter("seed", _rng.RandfRange(0f, 50f));
			mat.SetShaderParameter("density", Look == AirLook.Aerial ? 0.85f : 0.7f);
			mat.SetShaderParameter("amount", 0f);
			var mesh = new MeshInstance3D
			{
				Mesh = new QuadMesh { Size = new Vector2(BodyScale * _rng.RandfRange(2.2f, 3.4f), BodyScale * _rng.RandfRange(0.9f, 1.3f)) },
				MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true,
			};
			air.AddChild(mesh);
			mesh.GlobalPosition = at;
			air.Banks.Add((mesh, mat, at, _rng.RandfRange(0.4f, 1.1f) * (_rng.Randf() < 0.5f ? -1f : 1f)));
		}
		return air;
	}

	/// <summary>Every frame: its haze from the scene's fog at its distance; the mist drifting, turned to the eye, fading with it.</summary>
	private void TickAir(Air air, StalkerBody body, float vis, float dt)
	{
		if (air == null || !IsInstanceValid(air)) return;
		air.T += dt;
		var cam = GetViewport()?.GetCamera3D();
		var env = GetViewport()?.World3D?.Environment ?? cam?.Environment;
		Color fog = env != null && env.FogEnabled ? env.FogLightColor : new Color(0.12f, 0.13f, 0.15f);
		float density = env != null && env.FogEnabled ? env.FogDensity : 0.02f;
		float d = cam != null ? cam.GlobalPosition.DistanceTo(body.GlobalPosition) : 150f;
		// how much of the fog lies between the eye and it (Godot's exponential fog), kept short of swallowing it
		float fogAmt = 1f - Mathf.Exp(-density * d);
		var (lo, hi, dark) = Look switch
		{
			AirLook.Shadow => (0.7f, 0.82f, 0.45f),
			AirLook.Aerial => (0.78f, 0.9f, 0.7f),
			_ => (0.72f, 0.86f, 0.55f),
		};
		float haze = Mathf.Lerp(lo, hi, Mathf.Clamp(fogAmt, 0f, 1f));
		var skin = body.Skin as ShaderMaterial;
		skin?.SetShaderParameter("haze", haze);
		skin?.SetShaderParameter("haze_color", fog * dark);
		skin?.SetShaderParameter("base_y", body.GlobalPosition.Y);
		skin?.SetShaderParameter("top_y", body.GlobalPosition.Y + BodyScale * 2.4f);
		foreach (var (mesh, mat, home, drift) in air.Banks)
		{
			if (!IsInstanceValid(mesh)) continue;
			// drifting slowly along the tree line and back, never one sheet moving together
			Vector3 at = home + air.Along * (Mathf.Sin((float)air.T * 0.03f * drift + home.X) * BodyScale * 0.6f);
			mesh.GlobalPosition = at;
			if (cam != null)
			{
				Vector3 to = cam.GlobalPosition - at; to.Y = 0;
				if (to.LengthSquared() > 1e-4f) mesh.GlobalBasis = Basis.LookingAt(-to.Normalized(), Vector3.Up);
			}
			// mist catches the sky's light: a step paler than the fog the dark trees sink into, or it can't be seen at night
			mat.SetShaderParameter("color", fog.Lerp(new Color(0.42f, 0.44f, 0.48f), 0.35f) * 1.25f);
			mat.SetShaderParameter("drift", (float)air.T * 0.012f * drift);
			mat.SetShaderParameter("amount", Mathf.Clamp(vis * 1.2f, 0f, 1f));
		}
	}
}
