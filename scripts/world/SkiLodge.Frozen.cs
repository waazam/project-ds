using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// Act 23, the lodge frozen over (the owner's document): while the player is in the crawlspace, the storm comes in.
/// Back out of the wall into the dining hall, it's iced over: every window shattered open on a white-out of blowing
/// snow, glass round the frames and on the floor, snow drifted in under them and more sifting in; the tables glazed and
/// dripping with icicles; frost over everything. The lobby the same: its windows broken, snow on the floor under them,
/// the fire dead in the grate, the lamps gone cold and dim. And the front door, chained before, stands ajar.
/// </summary>
public partial class SkiLodge
{
	public bool Frozen { get; private set; }
	public int WindowsBroken { get; private set; }

	private readonly List<(Vector3 c, Vector3 along, Vector3 inward, float w, float h)> _winSpots = new();
	private Vector2 _stormScroll;

	/// <summary>Is this window one the freeze breaks? (The lobby's and the dining hall's; not the rooms', not the two the
	/// dining hall's chase has boxed in.)</summary>
	private static bool FreezeBreaks((Vector3 c, Vector3 along, Vector3 inward, float w, float h) s)
	{
		var c = s.c;
		bool dining = c.X > 9.5f && c.Y < RoomTop;
		bool lobby = Mathf.Abs(c.X) < 11.5f && Mathf.Abs(c.Z) < 11.5f && c.Y > 5f;
		bool chase = c.X > 31.2f && c.X < 36.1f && c.Z < -4.4f;
		return (dining || lobby) && !chase;
	}

	/// <summary>For previews and tests: the lodge frozen over now.</summary>
	public void FreezeNow() => Freeze(instant: true);

	private void Freeze(bool instant)
	{
		if (Frozen) return;
		Frozen = true;
		StoryManager.Instance?.SetFlag(LodgeFlag.Frozen);
		GD.Print("[story] Act 23: while they're in the walls, the lodge freezes over");
		_stormTarget = Storm = 1f;
		// the glass: a white-out of blowing snow beyond every broken pane
		foreach (var g in new[] { LodgeTextures.DayGlass, LodgeTextures.IceGlass })
		{
			g.AlbedoColor = new Color(0.55f, 0.6f, 0.68f);
			g.AlbedoTexture = WinterWoods.SnowStd("whiteout", 1f, 1f).AlbedoTexture;
			g.Emission = new Color(0.52f, 0.57f, 0.65f);
			g.EmissionEnergyMultiplier = 0.55f;
			g.Uv1Scale = new Vector3(0.6f, 0.6f, 1f);
		}
		var shards = new MeshKit();
		var broken = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.8f, 0.88f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.08f, MetallicSpecular = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		shards.Mat(broken);
		shards.Color = Colors.White;
		var drifts = new MeshKit();
		drifts.Mat(WinterWoods.SoftSnow);
		drifts.Color = Colors.White;
		var ice = new List<Transform3D>();
		var rng = new RandomNumberGenerator { Seed = 2323 };
		foreach (var spot in _winSpots)
		{
			if (!FreezeBreaks(spot)) continue;
			WindowsBroken++;
			var (c, along, inward, w, h) = spot;
			// what's left of the pane: jagged teeth round the frame
			for (int e = 0; e < 4; e++)
			{
				int teeth = 3 + rng.RandiRange(0, 2);
				for (int t = 0; t < teeth; t++)
				{
					float u0 = (t + rng.RandfRange(0f, 0.2f)) / teeth, u1 = (t + 1f) / teeth, um = (u0 + u1) * 0.5f;
					float reach = rng.RandfRange(0.06f, 0.26f);
					Vector3 A, B, T;
					switch (e)
					{
						case 0: A = c + along * (u0 - 0.5f) * w - Vector3.Up * h * 0.5f; B = c + along * (u1 - 0.5f) * w - Vector3.Up * h * 0.5f; T = c + along * (um - 0.5f) * w - Vector3.Up * (h * 0.5f - reach); break;
						case 1: A = c + along * (u0 - 0.5f) * w + Vector3.Up * h * 0.5f; B = c + along * (u1 - 0.5f) * w + Vector3.Up * h * 0.5f; T = c + along * (um - 0.5f) * w + Vector3.Up * (h * 0.5f - reach); break;
						case 2: A = c - along * w * 0.5f + Vector3.Up * (u0 - 0.5f) * h; B = c - along * w * 0.5f + Vector3.Up * (u1 - 0.5f) * h; T = c - along * (w * 0.5f - reach) + Vector3.Up * (um - 0.5f) * h; break;
						default: A = c + along * w * 0.5f + Vector3.Up * (u0 - 0.5f) * h; B = c + along * w * 0.5f + Vector3.Up * (u1 - 0.5f) * h; T = c + along * (w * 0.5f - reach) + Vector3.Up * (um - 0.5f) * h; break;
					}
					var o = inward * 0.012f;
					shards.Tri(A + o, B + o, T + o, inward, Vector2.Zero, Vector2.Right, Vector2.Down);
				}
			}
			// icicles off the lintel
			for (float u = -w * 0.45f; u <= w * 0.45f; u += 0.13f)
				ice.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(0.8f, rng.RandfRange(0.12f, 0.45f), 0.8f)), c + along * u + Vector3.Up * (h * 0.5f + 0.02f) + inward * 0.06f));
			// the floor under a low window: a drift, and glass in it
			bool low = c.Y < 3f;
			Vector3 floorAt = c + inward * 0.55f;
			floorAt.Y = FloorY;
			if (low || c.Y > 5f)
			{
				float size = low ? 1f : 0.7f;
				drifts.Blob(floorAt, new Vector3(w * 0.8f * size, 0.22f * size, 0.5f * size), 2400 + WindowsBroken, 0.25f, true, 1f);
				for (int g = 0; g < 6; g++)
				{
					var p = floorAt + along * rng.RandfRange(-w, w) + inward * rng.RandfRange(0.2f, 1.1f) + Vector3.Up * 0.012f;
					shards.Tri(p, p + new Vector3(rng.RandfRange(0.03f, 0.09f), 0, rng.RandfRange(-0.04f, 0.04f)), p + new Vector3(rng.RandfRange(-0.03f, 0.03f), 0, rng.RandfRange(0.03f, 0.08f)), Vector3.Up, Vector2.Zero, Vector2.Right, Vector2.Down);
				}
			}
			// snow blowing in through it
			var snow = new GpuParticles3D
			{
				Name = "WindowSnow", Amount = low ? 20 : 14, Lifetime = 3f, Position = c + inward * 0.08f,
				ProcessMaterial = new ParticleProcessMaterial
				{
					EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(w * 0.4f, h * 0.4f, 0.05f),
					Direction = inward + Vector3.Down * 0.4f, Spread = 25f, InitialVelocityMin = 0.4f, InitialVelocityMax = 1.2f, Gravity = new Vector3(0, -0.5f, 0),
					TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.5f, ScaleMin = 0.6f, ScaleMax = 1.3f,
				},
				DrawPass1 = new QuadMesh { Size = new Vector2(0.025f, 0.025f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.86f, 0.92f, 0.8f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			// (its box turned to the window's plane)
			snow.Basis = new Basis(along, Vector3.Up, along.Cross(Vector3.Up));
			((ParticleProcessMaterial)snow.ProcessMaterial).Direction = snow.Basis.Inverse() * (inward + Vector3.Down * 0.4f);
			AddChild(snow);
		}
		// the tables: glazed, and dripping icicles off both long edges
		for (int t = 0; t < 6; t++)
		{
			if (t == Collapsed) continue;
			var tc = TableCentre(t);
			foreach (float sz in new[] { -1f, 1f })
				for (float x = -TableLen * 0.48f; x <= TableLen * 0.48f; x += 0.17f)
					ice.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(0.7f, rng.RandfRange(0.1f, 0.42f), 0.7f)), new Vector3(tc.X + x + rng.RandfRange(-0.04f, 0.04f), FloorY + TableH - 0.02f, tc.Z + sz * (TableW * 0.5f + 0.012f))));
		}
		// frost over the floors of the lobby and the dining hall: thin patches of rime
		// (a few soft drifts of blown snow across the floors, low and lumpy; the frost does the rest)
		for (int i = 0; i < 12; i++)
		{
			Vector3 p = i < 5 ? new Vector3(rng.RandfRange(-7f, 7f), FloorY, rng.RandfRange(-6f, 6f)) : new Vector3(rng.RandfRange(13f, 42f), FloorY, rng.RandfRange(-6.5f, 6.5f));
			drifts.Blob(p, new Vector3(rng.RandfRange(0.3f, 0.7f), 0.05f, rng.RandfRange(0.3f, 0.6f)), 2500 + i, 0.45f, true, 1f);
		}
		shards.CommitTo(this, "BrokenGlass", false);
		drifts.CommitTo(this, "FrozenDrifts", false);
		if (ice.Count > 0)
		{
			var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = WinterWoods.IcicleMesh, InstanceCount = ice.Count };
			for (int i = 0; i < ice.Count; i++) mm.SetInstanceTransform(i, ice[i]);
			AddChild(new MultiMeshInstance3D { Name = "FrozenIcicles", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		}
		// a glaze of ice over everything in the two rooms
		foreach (var name in new[] { "DiningFurniture", "DiningShell", "LobbyFurniture", "LobbyShell", "Balcony" })
			if (GetNodeOrNull(name) is Node n) Glaze(n);
		for (int t = 0; t < 6; t++)
		{
			if (_tables[t] != null) Glaze(_tables[t]);
			if (GetNodeOrNull($"Collapse{t}") is Node cn) Glaze(cn);
			if (GetNodeOrNull($"Sheet{t}") is Node sn) Glaze(sn);
		}
		// the fire dead, the lamps gone cold and low
		if (GetNodeOrNull<GpuParticles3D>("Flames") is { } flames) flames.Visible = false;
		LodgeTextures.Glow("lodge_embers", new Color(1f, 0.42f, 0.12f), 1.6f).EmissionEnergyMultiplier = 0f;
		if (_fireLight != null) _fireLight.Visible = false;
		foreach (var ch in GetChildren())
		{
			if (ch is not OmniLight3D l || l == _fireLight) continue;
			var p = l.Position;
			bool here = p.X > 9.5f || (Mathf.Abs(p.X) < 11f && Mathf.Abs(p.Z) < 11f && p.Y < 20f);
			if (!here) continue;
			l.LightEnergy *= 0.5f;
			l.LightColor = l.LightColor.Lerp(new Color(0.62f, 0.72f, 0.9f), 0.55f);
		}
		foreach (var p in new[] { new Vector3(20f, 3f, 0f), new Vector3(34f, 3f, 0f), new Vector3(0f, 5f, 0f) })
			AddChild(new OmniLight3D { Name = "FrozenFill", Position = p, LightColor = new Color(0.62f, 0.72f, 0.92f), LightEnergy = 0.45f, OmniRange = 12f, ShadowEnabled = false, LightSpecular = 0.2f });
		FrontDoorsAjar();
		if (!instant) GD.Print($"[story] Act 23: {WindowsBroken} windows broken, the tables iced, the fire out, the front door ajar");
	}

	/// <summary>A glaze of ice over every mesh under <paramref name="n"/> (a thin glassy shell drawn over it).</summary>
	private static void Glaze(Node n)
	{
		if (n is GeometryInstance3D g && g.MaterialOverlay == null && g is not GpuParticles3D) g.MaterialOverlay = LodgeTextures.FrostOverlay;
		foreach (var c in n.GetChildren()) Glaze(c);
	}

	/// <summary>The storm beyond the broken glass never still (from _Process).</summary>
	private void FrozenProcess(float dt)
	{
		if (!Frozen) return;
		_stormScroll += new Vector2(0.35f, -0.9f) * dt;
		var off = new Vector3(Mathf.PosMod(_stormScroll.X, 1f), Mathf.PosMod(_stormScroll.Y, 1f), 0);
		LodgeTextures.DayGlass.Uv1Offset = off;
		LodgeTextures.IceGlass.Uv1Offset = off;
	}
}
