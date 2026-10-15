using Godot;

namespace ProjectDS.World;

public partial class ForestScatter
{
	/// <summary>The woods thicken toward the edge of the playable ground until nobody gets through (2026-10-15, the owner:
	/// "all end of world parts of the map should not be accessible because the trees will always get too thick and the
	/// bushes and forest too thick to reach the end of the map ... except for our trailhead road bend"). A band inside
	/// <see cref="ForestTerrain.PlayRect"/>'s edge, its inner side wandering (never a straight line of trees): young firs,
	/// snags, saplings crowded together, vine mounds, deadfall, fern and grass between, every woody thing solid, denser
	/// the nearer the edge. The terrain's walls stand inside it (<see cref="ForestTerrain.EdgeWallInset"/>). The road is
	/// left open: it goes on, and a wall across it (the same walls) stops the player before its bend.</summary>
	[Export] public bool EdgeThicket = false;
	/// <summary>The band's width (m) at its widest; it wanders between 0.7 and 1.3 of this.</summary>
	[Export] public float EdgeBand = 16f;
	/// <summary>How far past the playable edge (m) the thicket carries on, where there's ground.</summary>
	[Export] public float EdgeOuter = 16f;
	public int EdgeCount { get; private set; }

	private void ScatterEdgeThicket()
	{
		if (!EdgeThicket) return;
		var rect = _terrain.PlayRect;
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 53 + 9) };
		var wander = new FastNoiseLite { Seed = Seed + 911, Frequency = 0.035f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FractalOctaves = 2 };
		float maxW = EdgeBand * 1.3f;
		const float c = 1.7f;
		// it goes on past the playable edge too, out across the ground beyond the walls (as thick as at the edge): along a
		// stream or down a slope nobody sees through it to where the world stops
		var o = _terrain.GlobalPosition;
		var ground = new Rect2(_terrain.MinXZ.X + o.X, _terrain.MinXZ.Y + o.Z, _terrain.MaxXZ.X - _terrain.MinXZ.X, _terrain.MaxXZ.Y - _terrain.MinXZ.Y);
		var area = rect.Grow(EdgeOuter).Intersection(ground);
		for (float z = area.Position.Y + c * 0.5f; z < area.End.Y; z += c)
			for (float x = area.Position.X + c * 0.5f; x < area.End.X; x += c)
			{
				float e0 = Mathf.Min(Mathf.Min(x - rect.Position.X, rect.End.X - x), Mathf.Min(z - rect.Position.Y, rect.End.Y - z));
				if (e0 > maxW + c) { x = Mathf.Max(x, rect.End.X - maxW - c * 1.5f); continue; }   // (skip the inside)
				float px = x + rng.RandfRange(-0.45f, 0.45f) * c, pz = z + rng.RandfRange(-0.45f, 0.45f) * c;
				float roll = rng.Randf(), kind = rng.Randf();
				// (signed: negative out past the playable edge)
				float e = Mathf.Min(Mathf.Min(px - rect.Position.X, rect.End.X - px), Mathf.Min(pz - rect.Position.Y, rect.End.Y - pz));
				float w = EdgeBand * (0.7f + 0.6f * (wander.GetNoise2D(px, pz) * 0.5f + 0.5f));
				if (e > w || e < -EdgeOuter) continue;
				float t = Mathf.Clamp(1f - e / w, 0f, 1f);   // 0 at the band's inner side .. 1 at the edge and beyond
				var p2 = new Vector2(px, pz);
				_terrain.SampleFields(px, pz, out float dT, out _, out float dS, out float dR);
				// the road stays open (it's walled further on); the trail, the water and the lot too
				// (a stream that runs off the map: the thicket closes over it near the edge, so it's no way out either)
				if (dR < _terrain.RoadWidth * 0.5f + 1.0f || dT < 4f || (dS < 3f && e > 9f) || ParkDist(p2) < 4f) continue;
				if (ClearingDist(p2) < 2f || Cleared(p2, 1.0f, false)) continue;
				if (_terrain.RouteDistance(px, pz) < 2.5f) continue;
				if (roll > 0.3f + 0.65f * t) continue;
				float h = _terrain.HeightAt(px, pz);
				float yaw = rng.RandfRange(0, Mathf.Tau);
				var pos = new Vector3(px, h - 0.05f, pz);
				float tint = rng.RandfRange(0.82f, 1.05f);
				if (kind < 0.24f)
				{
					// young firs and the odd snag, packed close
					string mesh = kind < 0.04f ? "snag" : kind < 0.15f ? "fir_young" : "fir_mid";
					float trunkR = mesh == "snag" ? 0.31f : mesh == "fir_mid" ? 0.31f : 0.17f;
					float s = rng.RandfRange(0.75f, 1.05f);
					Add(mesh, TreeChunk, pos, Basis.FromEuler(new Vector3(rng.RandfRange(-0.03f, 0.03f), yaw, rng.RandfRange(-0.03f, 0.03f))).Scaled(Vector3.One * s),
						new Color(tint, tint * 0.98f, tint * 0.97f));
					if (TreeCollision) AddCollider(pos, Cylinder(Mathf.Max(trunkR * s, 0.3f), 6f), new Transform3D(Basis.Identity, pos + new Vector3(0, 2.6f, 0)));
				}
				else if (kind < 0.6f)
				{
					// saplings, crowded into a thicket
					string mesh = kind < 0.36f ? "sapling_a" : kind < 0.48f ? "sapling_b" : "sapling_c";
					float s = rng.RandfRange(0.8f, 1.3f);
					Add(mesh, TreeChunk, pos, Basis.FromEuler(new Vector3(rng.RandfRange(-0.06f, 0.06f), yaw, rng.RandfRange(-0.06f, 0.06f))).Scaled(Vector3.One * s),
						new Color(tint, tint, tint * 0.97f));
					if (TreeCollision) AddCollider(pos, Cylinder(0.45f * s, 3f), new Transform3D(Basis.Identity, pos + Vector3.Up * 1.4f));
				}
				else if (kind < 0.78f)
				{
					// vine mounds: brambles grown over whatever's under them
					string mesh = kind < 0.66f ? "vinemound_a" : kind < 0.72f ? "vinemound_b" : "vinemound_c";
					float s = rng.RandfRange(0.9f, 1.4f);
					var b = Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(new Vector3(s, s * rng.RandfRange(0.9f, 1.3f), s));
					var mp = new Vector3(px, Mathf.Min(h, Mathf.Min(_terrain.HeightAt(px + 1f, pz), _terrain.HeightAt(px - 1f, pz))) - 0.1f, pz);
					Add(mesh, TreeChunk, mp, b, Colors.White * rng.RandfRange(0.85f, 1.05f));
					if (TreeCollision) AddCollider(mp, Cylinder(1.0f * s, 2.4f), new Transform3D(Basis.Identity, mp + Vector3.Up * 1.0f));
				}
				else if (kind < 0.86f)
				{
					// deadfall: a trunk down among them, laid on the ground
					var yb = Basis.FromEuler(new Vector3(0, yaw, 0));
					float s = rng.RandfRange(0.8f, 1.3f), half = 2.3f * s;
					float hA = _terrain.HeightAt(px + yb.X.X * half, pz + yb.X.Z * half), hB = _terrain.HeightAt(px - yb.X.X * half, pz - yb.X.Z * half);
					var basis = LieAlong(yb.X * (2f * half) + Vector3.Up * (hA - hB), yb.Z);
					var lp = new Vector3(px, Mathf.Min((hA + hB) * 0.5f, h) + 0.1f * s, pz);
					Add(rng.Randf() < 0.45f ? "log_b" : "log", TreeChunk, lp, basis.Scaled(Vector3.One * s), new Color(tint, tint * 0.98f, tint * 0.94f));
					AddCollider(lp, BoxShape(new Vector3(2.5f * s, 0.5f * s, 0.3f * s)), new Transform3D(basis, lp + Vector3.Up * 0.2f));
				}
				else
				{
					// fern and tall grass between: no stopping, just no seeing through (2026-10-15, the playtest: fan palms read
					// wrong in a fir forest; ferns and grass instead)
					float s = rng.RandfRange(0.9f, 1.4f);
					Add(rng.Randf() < 0.75f ? "fern" : "grass", FoliageChunk, pos,
						Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(Vector3.One * s), Colors.White * rng.RandfRange(0.85f, 1.05f));
				}
				EdgeCount++;
			}
		GD.Print($"[build] edge thicket: {EdgeCount} in a {EdgeBand:0} m band inside {rect}");
	}
}
