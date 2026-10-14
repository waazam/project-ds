using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Act 1's way from the fallen fir that ends the main trail to the first stairs: the old path's
/// line. Its stones are gone (2026-10-03, the owner: "Let's remove the stone pathway"); the falling
/// trees show the way now (<see cref="Act1TreeFalls"/>), and <see cref="ForestAtmosphere"/> fogs
/// toward its end. Nothing of the friend's lies here any more (his things are in the Hollow).
///
/// <see cref="Waypoints"/> are world XZ (x, z), from the trail's end round the fir's crown to
/// the stairs' foot.
/// </summary>
[GlobalClass]
public partial class FriendTrail : Node3D
{
	[Export] public Vector2[] Waypoints = System.Array.Empty<Vector2>();
	/// <summary>Radius kept clear of trees round each belonging, so it can be seen (m). No lane along the line.</summary>
	[Export] public float CorridorRadius = 1.2f;
	/// <summary>Distances (m along the line) of spots kept clear of trees (the friend's belongings used to lie there; they are in the Hollow now).</summary>
	[Export] public float[] ItemAt = System.Array.Empty<float>();

	private ForestTerrain _terrain;
	private readonly List<Vector2> _pts = new();
	private readonly List<float> _cum = new();

	public float Length => _cum.Count > 0 ? _cum[^1] : 0f;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_terrain = GroundSnap.FindTerrain(this);
		if (_terrain == null || Waypoints.Length < 2) return;
		_pts.AddRange(Waypoints);
		_cum.Add(0f);
		for (int i = 1; i < _pts.Count; i++) _cum.Add(_cum[^1] + _pts[i].DistanceTo(_pts[i - 1]));

		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		BuildCorridor();
	}

	// ------------------------------------------------------------------ line helpers

	/// <summary>World XZ at distance s along the line, and the unit travel direction there.</summary>
	public Vector2 At(float s, out Vector2 dir)
	{
		s = Mathf.Clamp(s, 0f, Length);
		for (int i = 1; i < _pts.Count; i++)
			if (s <= _cum[i] || i == _pts.Count - 1)
			{
				float seg = _cum[i] - _cum[i - 1];
				float t = seg > 0 ? (s - _cum[i - 1]) / seg : 0f;
				dir = (_pts[i] - _pts[i - 1]).Normalized();
				return _pts[i - 1].Lerp(_pts[i], Mathf.Clamp(t, 0, 1));
			}
		dir = Vector2.Down;
		return _pts[^1];
	}

	private Vector3 Ground(Vector2 p) => new(p.X, _terrain.HeightAt(p.X, p.Y), p.Y);

	private void BuildCorridor()
	{
		foreach (float s in ItemAt)
		{
			var p = At(s, out _);
			var cz = new ClearZone { Radius = CorridorRadius, ClearFoliage = true, Name = $"ClearItem{(int)s}" };
			AddChild(cz);
			cz.GlobalPosition = Ground(p);
		}
	}
}
