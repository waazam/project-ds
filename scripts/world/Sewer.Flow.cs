using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;

namespace ProjectDS.World;

/// <summary>
/// (the gushy-water pass, 2026-10-15) The sewer's water moving: the pipe's water runs on toward the cistern (the
/// surface's scum and streaks drift with it, small ripples heave along it: <c>basement_water.gdshader</c>'s
/// <c>flow</c>), and three of the cistern's pipe mouths still run: two pour out thick and ropey, murky brown-green and
/// grey-frothed, into the water below with a churn and a little spray, and the high one with the light far down it lets
/// out a thin trickle that catches its light. Only water: the pipes and the lips are <c>Sewer.cs</c>'s. The two pours
/// have the creek's water loop under them, pitched down and quiet (a steady bed, not a repeating sound).
/// </summary>
public partial class Sewer
{
	/// <summary>For tests: the pipe mouths that are running.</summary>
	public int Outflows { get; private set; }

	private void BuildOutflows()
	{
		var root = new Node3D { Name = "Outflows" };
		AddChild(root);
		// a pour out of a mouth's lower lip (its mouth at, facing n, radius r): out a little way and down to the water
		void Pour(Vector3 at, Vector3 n, float r, float width, float speed, bool loud, string name)
		{
			Vector3 lip = at + n * 0.45f + Vector3.Down * (r - 0.06f);
			float drop = lip.Y - Water;
			var path = new List<Vector3>();
			for (int i = 0; i <= 8; i++)
			{
				float t = i / 8f;
				float fallT = Mathf.Sqrt(2f * drop * t / 9.8f);
				path.Add(lip + n * (speed * 0.35f * fallT) + Vector3.Down * (drop * t));
			}
			Vector3 across = n.Cross(Vector3.Up).Normalized();
			var sheet = FlowFx.PourMat(FlowFx.Liquid.Sewer, speed: 2.4f, strands: 4f, wobble: 0.04f, aerateLen: drop * 0.8f);
			FlowFx.Sheet(root, path, across, width, width * 1.25f, sheet, $"{name}Sheet", 6);
			var rope = FlowFx.PourMat(FlowFx.Liquid.Sewer, speed: 2.7f, strands: 2f, tube: true, wobble: 0.03f, aerateLen: drop);
			foreach (float lat in new[] { -0.32f, 0.36f })
			{
				var pts = new List<Vector3>();
				foreach (var p in path) pts.Add(p + across * lat * width + n * 0.03f);
				FlowFx.Tube(root, pts, width * 0.12f, width * 0.2f, rope, $"{name}Rope", 6);
			}
			Vector3 foot = path[^1] with { Y = Water + 0.02f };
			FlowFx.Churn(root, foot, width * 1.4f, FlowFx.Liquid.Sewer, 0.9f, $"{name}Churn");
			FlowFx.Spray(root, foot, FlowFx.Liquid.Sewer, width, 24, 1.8f, $"{name}Spray");
			const string loop = "res://assets/audio/ambient/stream_loop.wav";
			if (ResourceLoader.Exists(loop))
			{
				var player = new AudioStreamPlayer3D
				{
					Name = $"{name}Sound", Stream = GD.Load<AudioStream>(loop), Bus = "Water", UnitSize = 2.5f, MaxDistance = 26f,
					PitchScale = loud ? 0.7f : 0.86f, AttenuationFilterCutoffHz = 3200f, Position = foot + Vector3.Up * 0.4f,
				};
				player.AddChild(new AmbienceLoop { BaseVolumeDb = loud ? -14f : -19f });
				root.AddChild(player);
			}
			Outflows++;
		}
		Pour(new Vector3(-12f, 3.5f, RoomZ0), Vector3.Back, 0.9f, 0.55f, 1.6f, true, "Entry");
		Pour(new Vector3(-RoomX, 2.4f, RoomZ0 + 22f), Vector3.Right, 1.1f, 0.45f, 1.3f, false, "West");
		// the high one at the far end, its light far down it: a thin trickle catching that light
		{
			Vector3 lip = new Vector3(0f, 3.2f, RoomZ1) + Vector3.Forward * 0.45f + Vector3.Down * 0.95f;
			var pts = new List<Vector3>();
			for (int i = 0; i <= 6; i++) { float t = i / 6f; pts.Add(lip + Vector3.Forward * (0.12f * Mathf.Sqrt(t)) + Vector3.Down * ((lip.Y - Water) * t)); }
			var thin = FlowFx.PourMat(FlowFx.Liquid.Sewer, speed: 2.2f, strands: 2f, tube: true, wobble: 0.012f, aerateLen: 1.5f);
			FlowFx.Tube(root, pts, 0.035f, 0.05f, thin, "TrickleRope", 6);
			FlowFx.Churn(root, pts[^1] with { Y = Water + 0.02f }, 0.35f, FlowFx.Liquid.Sewer, 0.6f, "TrickleChurn");
			FlowFx.Drips(root, lip + Vector3.Right * 0.3f, FlowFx.Liquid.Sewer, 1f, lip.Y - Water, 0.04f, "LipDrips");
			Outflows++;
		}
	}
}
