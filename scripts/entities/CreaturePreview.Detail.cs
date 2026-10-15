using System.Threading.Tasks;
using Godot;
using ProjectDS.World.HallwayParts;

namespace ProjectDS.Entities;

/// <summary>
/// `-- --detail[=tag] [--detail-only=crawler,wendigo,arm,head,stalker,giant,lake,leviathan,shadow]` on
/// creature_preview.tscn (2026-10-13i, the owner: "the monster models look too smooth, add more detail"): every creature
/// close up and whole, by four lights: a plain grey key (`lit`), the lantern's flame in the dark (`lantern`), the
/// blacklight (`uv`), and the lantern in fog (`fog`); to test-output/creatures/detail_[tag]_[creature]_[view]_[light].png.
/// </summary>
public partial class CreaturePreview
{
	private string _detailTag = "now";
	private string[] _detailOnly;
	private OmniLight3D _dGlow;
	private SpotLight3D _dBeam;

	private bool DetailWants(string who) => _detailOnly == null || System.Array.IndexOf(_detailOnly, who) >= 0;

	/// <summary>The light for a shot: the camera's lantern (flame or violet), the fog, or the plain grey key.</summary>
	private void DetailLight(string mode, Vector3 eye, Vector3 at)
	{
		bool lit = mode == "lit";
		foreach (var n in GetChildren()) if (n is DirectionalLight3D d) d.Visible = lit;
		env.BackgroundColor = lit ? new Color(0.16f, 0.17f, 0.18f) : mode == "fog" ? new Color(0.03f, 0.03f, 0.035f) : Colors.Black;
		env.AmbientLightColor = lit ? new Color(0.35f, 0.33f, 0.36f) : new Color(0.4f, 0.42f, 0.5f);
		env.AmbientLightEnergy = lit ? 0.45f : 0.03f;
		env.FogEnabled = mode == "fog";
		env.FogLightColor = new Color(0.05f, 0.05f, 0.06f);
		env.FogDensity = 0.08f;
		env.VolumetricFogEnabled = mode == "fog";
		env.VolumetricFogDensity = 0.03f;
		env.VolumetricFogLength = 40f;
		env.VolumetricFogAmbientInject = 0f;
		_dGlow ??= new OmniLight3D { Name = "DetailGlow", ShadowEnabled = true, OmniAttenuation = 1.3f, LightSize = 0.08f };
		_dBeam ??= new SpotLight3D { Name = "DetailBeam", ShadowEnabled = true, SpotAngleAttenuation = 0.6f };
		if (_dGlow.GetParent() == null) { AddChild(_dGlow); AddChild(_dBeam); }
		_dGlow.Visible = _dBeam.Visible = !lit;
		bool uv = mode == "uv";
		Vector3 dir = (at - eye).Normalized(), side = dir.Cross(Vector3.Up).Normalized();
		// held as the game holds it: low and to the right of the eye
		_dGlow.GlobalPosition = eye + side * 0.18f + Vector3.Down * 0.25f + dir * 0.25f;
		_dGlow.LightColor = uv ? new Color(0.62f, 0.5f, 1.0f) : new Color(1f, 0.72f, 0.42f);
		_dGlow.LightEnergy = uv ? 1.1f : 1.35f;
		_dGlow.OmniRange = uv ? 10f : 14f;
		_dGlow.LightVolumetricFogEnergy = 0.05f;
		_dBeam.GlobalTransform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), eye + Vector3.Down * 0.05f);
		_dBeam.LightColor = uv ? new Color(0.38f, 0.12f, 1.0f) : new Color(1f, 0.72f, 0.42f);
		_dBeam.LightEnergy = uv ? 4.2f : 2.5f;
		_dBeam.SpotRange = uv ? 13f : 24f;
		_dBeam.SpotAngle = uv ? 24f : 35f;
		_dBeam.LightVolumetricFogEnergy = 0.45f;
		RenderingServer.GlobalShaderParameterSet("uv_light_on", uv ? 1f : 0f);
		RenderingServer.GlobalShaderParameterSet("uv_light_pos", eye);
		RenderingServer.GlobalShaderParameterSet("uv_light_dir", dir);
		RenderingServer.GlobalShaderParameterSet("uv_light_range", 13f);
	}

	private async Task DetailShot(string who, string view, Vector3 eye, Vector3 at, params string[] modes)
	{
		if (modes.Length == 0) modes = new[] { "lit", "lantern", "uv", "fog" };
		foreach (var m in modes)
		{
			DetailLight(m, eye, at);
			await Shot($"detail_{_detailTag}_{who}_{view}_{m}", eye, at);
		}
	}

	private async Task DetailShots()
	{
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			if (a.StartsWith("--detail=")) _detailTag = a["--detail=".Length..];
			if (a.StartsWith("--detail-only=")) _detailOnly = a["--detail-only=".Length..].Split(',');
		}
		var floorBody = new StaticBody3D { Name = "Floor" };
		floorBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(400, 1, 400) }, Position = new Vector3(0, -0.5f, 0) });
		floorBody.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400, 400) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.21f, 0.2f), Roughness = 0.95f } });
		AddChild(floorBody);

		if (DetailWants("crawler"))
		{
			var cr = new Crawler { Name = "Crawler" };
			AddChild(cr);
			for (int i = 0; i < 70; i++) { cr.Hurry = 0.25f; cr.Place(new Vector3(0, 0, 1.5f - i * 0.02f), Vector3.Forward); await Frames(1); }
			var head = cr.FindChild("Head", true, false) as Node3D;
			Vector3 c = cr.GlobalPosition, h = head?.GlobalPosition ?? c;
			await DetailShot("crawler", "face", h + new Vector3(0.2f, 0.05f, -0.6f), h);
			await DetailShot("crawler", "back", c + new Vector3(0.7f, 0.9f, 0.4f), c + new Vector3(0, 0, 0.1f));
			await DetailShot("crawler", "side", c + new Vector3(1.6f, 0.5f, 0.1f), c + new Vector3(0, -0.1f, 0.1f));
			var (sk, hb) = BoneOf(cr, "arm_hand_R");
			if (sk != null) { Vector3 hp = BoneWorld(sk, hb); await DetailShot("crawler", "hand", hp + new Vector3(0.35f, 0.3f, -0.25f), hp, "lit", "lantern"); }
			await DetailShot("crawler", "far", c + new Vector3(1.2f, 1.2f, -4.5f), c, "lantern", "fog");
			cr.QueueFree();
			await Frames(3);
		}

		if (DetailWants("wendigo"))
		{
			var w = new Wendigo { Name = "Wendigo" };
			AddChild(w);
			w.StandAt(Vector3.Zero, new Vector3(0, 0, -10));
			await Seconds(1.5);
			await DetailShot("wendigo", "head", new Vector3(0.7f, 3.75f, -2.0f), w.MouthWorld + new Vector3(0, 0.15f, 0));
			await DetailShot("wendigo", "chest", new Vector3(-0.5f, 3.2f, -1.7f), w.ChestWorld);
			await DetailShot("wendigo", "hand", new Vector3(1.4f, 1.3f, -1.4f), w.ToGlobal(new Vector3(0.68f, 1.0f, -0.6f)));
			await DetailShot("wendigo", "back", new Vector3(-1.8f, 3.4f, 2.6f), new Vector3(0, 3.0f, 0), "lit", "lantern");
			await DetailShot("wendigo", "whole", new Vector3(1.5f, 2.6f, -7.5f), new Vector3(0, 2.5f, 0), "lit", "lantern", "fog");
			w.QueueFree();
			await Frames(3);
		}

		if (DetailWants("head"))
		{
			var w = new Wendigo { Name = "Platter", Position = new Vector3(40, 1f, 0) };
			AddChild(w);
			await Frames(2);
			w.ShowOnlyHead();
			await Seconds(0.5);
			Vector3 m = w.MouthWorld;
			await DetailShot("head", "front", m + new Vector3(0.3f, 0.35f, -1.1f), m + Vector3.Up * 0.15f, "lit", "lantern", "uv");
			w.QueueFree();
			await Frames(3);
		}

		if (DetailWants("arm"))
		{
			var arm = new WendigoArm { Name = "Arm", Position = new Vector3(80, 1.2f, 0) };
			AddChild(arm);
			await Seconds(0.6);
			arm.Burst();
			await Seconds(1.6);
			Vector3 hp = arm.HandWorld;
			await DetailShot("arm", "hand", hp + new Vector3(-0.3f, 0.35f, 0.75f), hp, "lit", "lantern", "uv");
			await DetailShot("arm", "forearm", hp + new Vector3(-0.7f, 0.3f, 0.9f), hp + new Vector3(-0.5f, 0f, 0f), "lantern");
			arm.QueueFree();
			await Frames(3);
		}

		if (DetailWants("stalker"))
		{
			// as the game draws it (StalkerBody's own skin: true black, the face plane, the edge light up close)
			var st = new StalkerBody { Name = "Stalker", Seed = 2077, Size = 1.08f, Position = new Vector3(120, 0, 0) };
			AddChild(st);
			await Seconds(1.2);
			Vector3 f = st.GlobalBasis.Z, eyes = st.EyesWorld;
			var (sk, nb) = BoneOf(st, "chest");
			Vector3 chest = sk != null ? BoneWorld(sk, nb) : st.GlobalPosition + Vector3.Up * 1.6f;
			await DetailShot("stalker", "face", eyes + f * 0.6f + Vector3.Right * 0.1f, eyes + Vector3.Down * 0.05f, "lantern", "uv");
			await DetailShot("stalker", "torso", chest + f * 1.3f + Vector3.Right * 0.4f, chest, "lit", "lantern", "uv");
			var (hk, hb) = BoneOf(st, "hand_R");
			if (hk != null) { Vector3 hp = BoneWorld(hk, hb); await DetailShot("stalker", "hand", hp + f * 0.6f + Vector3.Up * 0.15f, hp, "lantern"); }
			await DetailShot("stalker", "whole", st.GlobalPosition + f * 3.6f + Vector3.Up * 1.5f, st.GlobalPosition + Vector3.Up * 1.3f, "lantern", "fog");
			st.QueueFree();
			await Frames(3);
		}

		if (DetailWants("giant"))
		{
			// Act 7's giant (giant_skin: lit, its haze), close enough to read its hide, and as the crossing shows it
			var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/giant_skin.gdshader") };
			skin.SetShaderParameter("haze", 0.35f);
			skin.SetShaderParameter("haze_color", new Color(0.02f, 0.02f, 0.03f));
			skin.SetShaderParameter("albedo", new Color(0.02f, 0.02f, 0.03f));
			skin.SetShaderParameter("face_tint", new Color(0.14f, 0.14f, 0.15f));
			skin.SetShaderParameter("visibility", 1f);
			skin.SetShaderParameter("wetness", 0.1f);
			var g = new StalkerBody { Name = "Giant", Skin = skin, Size = 6f, Position = new Vector3(160, 0, 0) };
			AddChild(g);
			await Seconds(1.2);
			Vector3 f = g.GlobalBasis.Z;
			var (sk, nb) = BoneOf(g, "chest");
			Vector3 chest = sk != null ? BoneWorld(sk, nb) : g.GlobalPosition + Vector3.Up * 9f;
			await DetailShot("giant", "torso", chest + f * 5f + Vector3.Right * 1.5f, chest, "lit", "lantern");
			await DetailShot("giant", "whole", g.GlobalPosition + f * 22f + Vector3.Up * 6f, g.GlobalPosition + Vector3.Up * 7f, "lit", "fog");
			g.QueueFree();
			await Frames(3);
		}

		if (DetailWants("lake"))
		{
			var lake = new LakeCreature { Name = "Lake", Position = new Vector3(200, 0, 0) };
			AddChild(lake);
			await Frames(2);
			lake.Breach(new Vector3(200, 0, 0), new Vector3(200, 0, 16), 30);
			await Seconds(3.5);
			lake.OpenEyes();
			await Seconds(1.0);
			await DetailShot("lake", "close", new Vector3(203, 2.5f, 10f), new Vector3(200, 3f, 2f), "lit", "lantern", "fog");
			lake.QueueFree();
			await Frames(3);
		}

		if (DetailWants("leviathan"))
		{
			var lev = new Leviathan { Name = "Leviathan", Position = new Vector3(260, 0, 0), Level = 12f };
			AddChild(lev);
			await Seconds(1.5);
			await DetailShot("leviathan", "body", new Vector3(266f, 12f, 16f), new Vector3(260, 7f, 0), "lit", "lantern");
			lev.Rot = 0.7f;
			await DetailShot("leviathan", "rot", new Vector3(266f, 12f, 16f), new Vector3(260, 7f, 0), "lit");
			lev.QueueFree();
			await Frames(3);
		}

		if (DetailWants("shadow"))
		{
			var sm = new HallStalker { Name = "HallStalker" };
			AddChild(sm);
			await Seconds(1.0);
			sm.StandAt(new Vector3(320, 0, 0), new Vector3(320, 0, 10));
			await Seconds(0.5);
			await DetailShot("shadow", "close", new Vector3(320.2f, 1.7f, 1.8f), new Vector3(320, 1.6f, 0), "lit", "lantern");
			sm.QueueFree();
			await Frames(3);
		}
	}
}
