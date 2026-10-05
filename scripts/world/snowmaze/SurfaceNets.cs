using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// A surface from a distance field (Act 24's snow caves: organic tunnels, alcoves and mounds from one function), by
/// naive surface nets: a grid of samples; in every cell the surface passes through, one vertex at the average of where it
/// crosses the cell's edges; a quad across every grid edge the surface crosses, joining the four cells round it. Smooth,
/// watertight, no tables. Normals from the field's gradient. The field is negative in the open (the air of the tunnels)
/// and positive in the solid (the snow and ice), so faces point into the open.
/// </summary>
public static class SurfaceNets
{
	/// <summary>Meshes <paramref name="field"/> over the box from <paramref name="min"/>, <paramref name="n"/> cells each way of
	/// <paramref name="step"/> metres. Returns the arrays (vertices, normals, indices); empty when nothing crosses.</summary>
	public static (Vector3[] verts, Vector3[] normals, int[] index) Mesh(Func<Vector3, float> field, Vector3 min, Vector3I n, float step)
	{
		int nx = n.X + 1, ny = n.Y + 1, nz = n.Z + 1;
		var d = new float[nx * ny * nz];
		int Id(int x, int y, int z) => (z * ny + y) * nx + x;
		for (int z = 0; z < nz; z++)
			for (int y = 0; y < ny; y++)
				for (int x = 0; x < nx; x++)
					d[Id(x, y, z)] = field(min + new Vector3(x, y, z) * step);
		// one vertex per crossed cell
		var cellVert = new int[n.X * n.Y * n.Z];
		Array.Fill(cellVert, -1);
		int Cell(int x, int y, int z) => (z * n.Y + y) * n.X + x;
		var verts = new List<Vector3>();
		Span<float> c = stackalloc float[8];
		for (int z = 0; z < n.Z; z++)
			for (int y = 0; y < n.Y; y++)
				for (int x = 0; x < n.X; x++)
				{
					int mask = 0;
					for (int k = 0; k < 8; k++)
					{
						c[k] = d[Id(x + (k & 1), y + ((k >> 1) & 1), z + ((k >> 2) & 1))];
						if (c[k] > 0f) mask |= 1 << k;
					}
					if (mask == 0 || mask == 255) continue;
					Vector3 sum = Vector3.Zero;
					int cnt = 0;
					// the twelve edges: pairs of corners differing in one bit
					for (int a = 0; a < 8; a++)
						for (int bit = 1; bit <= 4; bit <<= 1)
						{
							if ((a & bit) != 0) continue;
							int b = a | bit;
							bool sa = c[a] > 0f, sb = c[b] > 0f;
							if (sa == sb) continue;
							float t = c[a] / (c[a] - c[b]);
							var pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1);
							var pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
							sum += pa.Lerp(pb, t);
							cnt++;
						}
					cellVert[Cell(x, y, z)] = verts.Count;
					verts.Add(min + (new Vector3(x, y, z) + sum / cnt) * step);
				}
		if (verts.Count == 0) return (Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<int>());
		// a quad across each crossed grid edge (interior edges only: the four cells round it must exist)
		var index = new List<int>();
		void Quad(int v0, int v1, int v2, int v3, bool flip)
		{
			if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) return;
			if (flip) { index.Add(v0); index.Add(v2); index.Add(v1); index.Add(v0); index.Add(v3); index.Add(v2); }
			else { index.Add(v0); index.Add(v1); index.Add(v2); index.Add(v0); index.Add(v2); index.Add(v3); }
		}
		for (int z = 1; z < n.Z; z++)
			for (int y = 1; y < n.Y; y++)
				for (int x = 0; x < n.X; x++)
				{
					// the edge along x at (y, z)
					float a = d[Id(x, y, z)], b = d[Id(x + 1, y, z)];
					if ((a > 0f) == (b > 0f)) continue;
					Quad(cellVert[Cell(x, y - 1, z - 1)], cellVert[Cell(x, y, z - 1)], cellVert[Cell(x, y, z)], cellVert[Cell(x, y - 1, z)], a > 0f);
				}
		for (int z = 1; z < n.Z; z++)
			for (int y = 0; y < n.Y; y++)
				for (int x = 1; x < n.X; x++)
				{
					float a = d[Id(x, y, z)], b = d[Id(x, y + 1, z)];
					if ((a > 0f) == (b > 0f)) continue;
					Quad(cellVert[Cell(x - 1, y, z - 1)], cellVert[Cell(x - 1, y, z)], cellVert[Cell(x, y, z)], cellVert[Cell(x, y, z - 1)], a > 0f);
				}
		for (int z = 0; z < n.Z; z++)
			for (int y = 1; y < n.Y; y++)
				for (int x = 1; x < n.X; x++)
				{
					float a = d[Id(x, y, z)], b = d[Id(x, y, z + 1)];
					if ((a > 0f) == (b > 0f)) continue;
					Quad(cellVert[Cell(x - 1, y - 1, z)], cellVert[Cell(x, y - 1, z)], cellVert[Cell(x, y, z)], cellVert[Cell(x - 1, y, z)], a > 0f);
				}
		// normals: the field's gradient (pointing into the open, where it falls)
		var normals = new Vector3[verts.Count];
		float e = step * 0.5f;
		for (int i = 0; i < verts.Count; i++)
		{
			var p = verts[i];
			var g = new Vector3(field(p + new Vector3(e, 0, 0)) - field(p - new Vector3(e, 0, 0)),
				field(p + new Vector3(0, e, 0)) - field(p - new Vector3(0, e, 0)),
				field(p + new Vector3(0, 0, e)) - field(p - new Vector3(0, 0, e)));
			normals[i] = g.LengthSquared() > 1e-12f ? (-g).Normalized() : Vector3.Up;
		}
		return (verts.ToArray(), normals, index.ToArray());
	}
}
