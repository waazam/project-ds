using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Entities;

/// <summary>
/// `-- --hands[=name]` on creature_preview.tscn (2026-10-13, the owner: "hands in the game could be way better, more claw
/// like"): close-ups of every creature's hands as the game shows them (the stalker, the giant's grab uses the same
/// model, the wendigo, its arm in the lodge's crawlspace, the crawler), from the back, the palm side and the edge, to
/// test-output/creatures/hands_[name]_*.png.
/// </summary>
public partial class CreaturePreview
{
	private string _handsName = "now";

	private static (Skeleton3D skel, int bone) BoneOf(Node root, string name)
	{
		foreach (var n in root.FindChildren("*", "Skeleton3D", true, false))
			if (n is Skeleton3D sk && sk.FindBone(name) is int b and >= 0) return (sk, b);
		return (null, -1);
	}

	private static Vector3 BoneWorld(Skeleton3D sk, int b) => sk.GlobalTransform * sk.GetBoneGlobalPose(b).Origin;

	/// <summary>The hand between its wrist and its middle finger's middle joint, shot from round it.</summary>
	private async Task HandOrbit(string who, Node root, string hand, string midJoint, float dist, Vector3 facing)
	{
		var (sk, hb) = BoneOf(root, hand);
		var (sk2, mb) = BoneOf(root, midJoint);
		if (sk == null) { GD.Print($"[creature-preview] {who}: no bone {hand}"); return; }
		Vector3 c = BoneWorld(sk, hb);
		if (sk2 != null) c = c.Lerp(BoneWorld(sk2, mb), 0.6f);
		Vector3 f = facing.Normalized(), side = f.Cross(Vector3.Up).Normalized();
		// a lamp held up to it (the stalker's hide is near black: by the scene's light alone its hand doesn't read)
		var lamp = new OmniLight3D { LightEnergy = 1.6f, OmniRange = dist * 5f, ShadowEnabled = true };
		AddChild(lamp);
		lamp.GlobalPosition = c + f * dist * 0.8f + side * dist * 0.5f + Vector3.Up * dist * 0.6f;
		await Shot($"hands_{_handsName}_{who}_front", c + f * dist + Vector3.Up * dist * 0.15f, c);
		await Shot($"hands_{_handsName}_{who}_side", c + side * dist + f * dist * 0.3f, c);
		await Shot($"hands_{_handsName}_{who}_other", c - side * dist + f * dist * 0.3f, c);
		await Shot($"hands_{_handsName}_{who}_below", c + f * dist * 0.6f + Vector3.Down * dist * 0.8f, c);
		lamp.QueueFree();
	}

	private async Task HandShots()
	{
		foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--hands=")) _handsName = a["--hands=".Length..];
		env.BackgroundColor = new Color(0.16f, 0.17f, 0.18f);
		env.AmbientLightEnergy = 0.55f;
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(40, 40) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.24f, 0.22f) } });

		// the stalker (and the giant: the same model, grown)
		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
		skin.SetShaderParameter("albedo", new Color(0.026f, 0.028f, 0.034f));
		skin.SetShaderParameter("face_tint", new Color(0.21f, 0.215f, 0.22f));
		var st = new StalkerBody { Name = "Stalker", Size = 1.1f, Skin = skin };
		AddChild(st);
		await Seconds(1.2);
		// facing: the stalker faces +Z in the game's space (StalkerBody turns its model round)
		await HandOrbit("stalker", st, "hand_R", "f1_2_R", 0.55f, st.GlobalBasis.Z);
		await Shot($"hands_{_handsName}_stalker_whole", st.GlobalPosition + st.GlobalBasis.Z * 4.2f + Vector3.Up * 1.4f, st.GlobalPosition + Vector3.Up * 1.2f);
		// its shoulders (2026-10-13, the owner: no spikes on them), against the light so the outline reads
		{
			var (sk, nb) = BoneOf(st, "neck");
			Vector3 n = sk != null ? BoneWorld(sk, nb) : st.GlobalPosition + Vector3.Up * 2.2f;
			Vector3 f = st.GlobalBasis.Z, sd = f.Cross(Vector3.Up).Normalized();
			await Shot($"hands_{_handsName}_stalker_shoulders_front", n + f * 1.6f + Vector3.Up * 0.2f, n);
			await Shot($"hands_{_handsName}_stalker_shoulders_side", n + sd * 1.7f + f * 0.4f + Vector3.Up * 0.2f, n);
			await Shot($"hands_{_handsName}_stalker_shoulders_back", n - f * 1.6f + sd * 0.6f + Vector3.Up * 0.4f, n);
		}
		st.QueueFree();
		await Frames(3);

		// the giant (the same body, grown, black against the grey; its shoulders)
		{
			var gskin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
			gskin.SetShaderParameter("albedo", new Color(0.0f, 0.0f, 0.0f));
			gskin.SetShaderParameter("face_tint", new Color(0.05f, 0.05f, 0.055f));
			gskin.SetShaderParameter("visibility", 1f);
			var g = new StalkerBody { Name = "Giant", Skin = gskin, Size = GiantRise.SizeFor(41.6f, 0f, 40f), Position = new Vector3(0, 0, -40f) };
			AddChild(g);
			await Seconds(1.2);
			var (sk, nb) = BoneOf(g, "neck");
			Vector3 n = sk != null ? BoneWorld(sk, nb) : g.GlobalPosition + Vector3.Up * 40f;
			Vector3 f = g.GlobalBasis.Z, sd = f.Cross(Vector3.Up).Normalized();
			float h = n.Y - g.GlobalPosition.Y;
			await Shot($"hands_{_handsName}_giant_front", n + f * h * 0.9f + Vector3.Down * h * 0.2f, n);
			await Shot($"hands_{_handsName}_giant_side", n + sd * h * 0.9f + f * h * 0.3f, n);
			g.QueueFree();
			await Frames(3);
		}
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--shoulders-only") >= 0) return;

		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--stalker-only") >= 0) return;
		// the wendigo
		var w = new Wendigo { Name = "Wendigo" };
		AddChild(w);
		w.StandAt(Vector3.Zero, new Vector3(0, 0, -10));
		await Seconds(1.5);
		await HandOrbit("wendigo", w, "hand_R", "f1_2_R", 0.95f, new Vector3(0, 0, -1));
		w.QueueFree();
		await Frames(3);

		// its arm through the crawlspace's wall (the model alone, at rest: the hand lies palm down along +X)
		var armScene = GD.Load<PackedScene>("res://assets/models/wendigo/wendigo_arm.glb");
		var arm = armScene.Instantiate<Node3D>();
		arm.Position = new Vector3(-1.2f, 1.0f, 0);
		AddChild(arm);
		await Frames(4);
		await HandOrbit("wendigo_arm", arm, "hand", "f1_2", 0.75f, new Vector3(0, 0.6f, 1));
		arm.QueueFree();
		await Frames(3);

		// the crawler
		var cr = new Crawler { Name = "Crawler" };
		AddChild(cr);
		for (int i = 0; i < 40; i++) { cr.Hurry = 0.2f; cr.Place(new Vector3(0, 0, 1f - i * 0.02f), Vector3.Forward); await Frames(1); }
		await HandOrbit("crawler", cr, "arm_hand_R", "arm_f1_R", 0.5f, new Vector3(0.3f, 0.5f, -1));
	}
}
