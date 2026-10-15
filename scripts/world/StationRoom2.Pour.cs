using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (the gushy-water pass, 2026-10-15) Room 2's torrent as <see cref="FlowFx"/> builds a pour: the lake coming in over the
/// sill in a thick sheet combed into ropes, three strands of it standing out, surging and breaking up at the sides; and
/// where it lands (on the floor, then on the rising flood) a churning boil and spray, which ride up with the water. When
/// the water turns to blood they go thick, dark and slow with it (<see cref="TorrentViscous"/>); when it's drawn back out
/// the boil and the spray stop.
/// </summary>
public partial class StationRoom2
{
	private MeshInstance3D _torrentChurn;
	private GpuParticles3D _torrentSpray;
	private ShaderMaterial _churnMat, _torrentRope;
	private float _torrentX;

	private void BuildTorrent(float cx)
	{
		_torrentX = cx;
		var holder = new Node3D { Name = "Torrent", Visible = false };
		AddChild(holder);
		holder.AddToGroup("warm_up");   // (hidden until the window breaks: drawn in the load's warm-up all the same)
		// out over the sill and curving down onto the floor
		var path = new List<Vector3>();
		for (int i = 0; i <= 10; i++)
		{
			float t = i / 10f;
			path.Add(new Vector3(cx, Mathf.Lerp(1.02f, 0f, t * t), -Half + 0.05f + t * 1.4f));
		}
		FlowFx.Sheet(holder, path, Vector3.Right, 1.5f, 1.9f, _torrentMat, "Sheet", 8);
		_torrentRope = FlowFx.PourMat(FlowFx.Liquid.Lake, speed: 3.5f, strands: 2f, tube: true, wobble: 0.05f, aerateLen: 1.0f);
		// ropes standing out of it: not all the way, wandering a little, each its own thickness
		foreach (var (lat, r, from) in new[] { (-0.62f, 0.05f, 0.15f), (-0.05f, 0.075f, 0.3f), (0.55f, 0.06f, 0.05f) })
		{
			var pts = new List<Vector3>();
			for (int i = 0; i < path.Count; i++)
			{
				float t = i / (float)(path.Count - 1);
				if (t < from) continue;
				pts.Add(path[i] + new Vector3(lat * (1f + 0.25f * t) + 0.06f * Mathf.Sin(t * 5f + lat * 7f), 0.03f, 0.02f));
			}
			FlowFx.Tube(holder, pts, r, r * 1.5f, _torrentRope, $"Rope{lat:0.0}", 6);
		}
		// where it lands
		_torrentChurn = FlowFx.Churn(holder, new Vector3(cx, 0.02f, -Half + 1.5f), 1.2f, FlowFx.Liquid.Lake, 1f, "Churn");
		_churnMat = (ShaderMaterial)_torrentChurn.MaterialOverride;
		_torrentSpray = FlowFx.Spray(holder, new Vector3(cx, 0.03f, -Half + 1.4f), FlowFx.Liquid.Lake, 0.9f, 24, 2.2f, "Spray");
		_torrent = holder;
	}

	/// <summary>0 water .. 1 blood: the pour and its boil go thick, slow and dark (with the colour's own tween).</summary>
	private void TorrentViscous(float u, Color c)
	{
		_torrentMat.SetShaderParameter("viscous", u);
		_torrentMat.SetShaderParameter("foam", 0.8f * (1f - u));
		_churnMat?.SetShaderParameter("viscous", u);
		_churnMat?.SetShaderParameter("water_color", c);
		_waterMat?.SetShaderParameter("viscous", u);
		FlowFx.SetLiquid(_torrentSpray, u > 0.5f ? FlowFx.Liquid.Blood : FlowFx.Liquid.Lake);
	}

	/// <summary>Each frame: the boil and the spray ride on the flood's surface; they stop once it's being drawn back out.</summary>
	private void TorrentTick()
	{
		if (_torrentChurn == null || _torrent == null || !_torrent.Visible) return;
		// the ropes follow the sheet's state (its colour, its direction, its fade, water or blood)
		foreach (var p in new[] { "tint", "flow", "opacity", "viscous", "foam" })
			_torrentRope.SetShaderParameter(p, _torrentMat.GetShaderParameter(p));
		float y = Mathf.Max(0.02f, Level + 0.02f);
		_torrentChurn.Position = _torrentChurn.Position with { Y = y };
		_torrentSpray.Position = _torrentSpray.Position with { Y = y };
		bool pouring = !_winning;
		_torrentSpray.Emitting = pouring;
		_churnMat.SetShaderParameter("strength", pouring ? 1f : 0f);
		// the flood's surface heaves in rings out from where it lands
		_waterMat?.SetShaderParameter("inflow", new Vector2(_torrentChurn.Position.X, _torrentChurn.Position.Z));
		_waterMat?.SetShaderParameter("inflow_strength", pouring ? 1f : 0f);
	}
}
