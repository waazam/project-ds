using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Builds an ordinary interior house staircase: painted closed stringers and
/// risers, stained treads with a worn runner held by stair rods, a handrail
/// with balusters on one side, a capped newel post, and (optionally) a top
/// landing that simply ends, baseboard and all, against a wall that isn't there.
///
/// Local frame: origin on the floor at the centre of the first riser; the
/// stairs climb toward -Z. Collision is one convex hull (ramp over the steps +
/// landing block) plus a railing wall; both are tagged surface = "wood".
///
/// If a child Area3D named "TopTrigger" and a Marker3D named "AutotestApproach"
/// exist they are moved to the landing / 1.5 m in front of the first step.
/// </summary>
[Tool]
[GlobalClass]
public partial class StaircaseBuilder : Node3D
{
	[Export] public int Steps = 14;          // risers
	[Export] public float Rise = 0.19f;
	[Export] public float Run = 0.26f;
	[Export] public float Width = 1.1f;
	[Export] public bool IncludeLanding = true;
	[Export] public float LandingDepth = 1.0f;
	[Export] public bool IncludeRailing = true;
	/// <summary>+1 = railing on +X (right side going up), -1 = left.</summary>
	[Export] public int RailSide = 1;
	[Export] public float RunnerWidth = 0.72f;
	[Export] public bool BuildCollision = true;

	private const float Nose = 0.025f, TreadT = 0.03f, Skirt = 0.07f, PanelT = 0.04f;

	public float TotalHeight => Steps * Rise;
	public float TopFrontZ => -(Steps - 1) * Run;
	public float BackZ => TopFrontZ - (IncludeLanding ? LandingDepth : Run);

	public override void _Ready() => Build();

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		var gen = new Node3D { Name = "Generated" };
		AddChild(gen);

		var paint = ProcTextures.PaintMat;
		var trim = ProcTextures.Cached("stair_trim", () => new StandardMaterial3D
		{
			AlbedoTexture = ProcTextures.Paint(), AlbedoColor = new Color(1.02f, 1.01f, 0.98f),
			Roughness = 0.55f, MetallicSpecular = 0.5f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		});
		var tread = ProcTextures.TreadMat;
		var carpet = ProcTextures.CarpetMat;
		var brass = ProcTextures.Flat("brass_dull", new Color(0.42f, 0.34f, 0.18f), 0.45f, 0.6f);
		var vent = ProcTextures.Flat("vent", new Color(0.16f, 0.15f, 0.14f), 0.6f, 0.4f);

		var k = new MeshKit();
		int N = Steps;
		float H = TotalHeight, W = Width, hw = W * 0.5f;
		float zTop = TopFrontZ, zBack = BackZ;

		// --- side panels (closed stringer + under-stair wall), both sides ---
		var profile = new List<Vector2>
		{
			new(0f, 0f), new(0f, Rise + Skirt), new(zTop, H + Skirt), new(zBack, H + Skirt), new(zBack, 0f),
		};
		k.Mat(paint);
		k.ExtrudeX(profile, hw, hw + PanelT, 1.5f);
		k.ExtrudeX(profile, -hw - PanelT, -hw, 1.5f);
		// trim cap along the top edge of each stringer
		k.Mat(trim);
		foreach (float x in new[] { hw + PanelT * 0.5f, -hw - PanelT * 0.5f })
		{
			k.Beam(new Vector3(x, Rise + Skirt + 0.012f, 0.012f), new Vector3(x, H + Skirt + 0.012f, zTop), PanelT + 0.018f, 0.024f, 3f);
			k.Beam(new Vector3(x, H + Skirt + 0.012f, zTop - 0.012f), new Vector3(x, H + Skirt + 0.012f, zBack), PanelT + 0.018f, 0.024f, 3f);
			// baseboard where the stair side meets the (grass) floor
			k.Box(new Vector3(x + Mathf.Sign(x) * (PanelT * 0.5f + 0.009f), 0.06f, zBack * 0.5f), new Vector3(0.018f, 0.12f, -zBack + 0.02f), 3f);
		}
		k.Mat(paint);
		// back wall under the landing / last step
		k.Box(new Vector3(0, (H - TreadT) * 0.5f, zBack + 0.01f), new Vector3(W, H - TreadT, 0.02f), 1.5f);

		// --- risers + treads ---
		for (int i = 0; i < N; i++)
		{
			float z = -i * Run;
			float y0 = i * Rise, y1 = (i + 1) * Rise - TreadT;
			k.Mat(paint).Box(new Vector3(0, (y0 + y1) * 0.5f, z - 0.01f), new Vector3(W, y1 - y0, 0.02f), 2f);
			bool last = i == N - 1;
			float zFront = z + Nose;
			float zRear = last ? zBack : z - Run;
			k.Mat(tread).Box(new Vector3(0, (i + 1) * Rise - TreadT * 0.5f, (zFront + zRear) * 0.5f),
				new Vector3(W, TreadT, zFront - zRear), 2.5f);
		}

		// --- runner ---
		k.Mat(carpet);
		float v = 0f, cw = RunnerWidth * 0.5f, lift = 0.006f;
		void RunnerQuad(Vector3 a, Vector3 b, Vector3 n, float len)
		{
			// a,b = centre line start/end; quad spans x in [-cw, cw]
			k.Quad(new Vector3(-cw, a.Y, a.Z), new Vector3(cw, a.Y, a.Z), new Vector3(cw, b.Y, b.Z), new Vector3(-cw, b.Y, b.Z), n,
				new Vector2(0, v), new Vector2(1, v), new Vector2(1, v + len), new Vector2(0, v + len));
			v += len;
		}
		for (int i = 0; i < N; i++)
		{
			float z = -i * Run;
			bool last = i == N - 1;
			float yLow = i * Rise + (i == 0 ? 0.004f : lift);
			float yTreadBot = (i + 1) * Rise - TreadT, yTop = (i + 1) * Rise + lift;
			float zRiser = z + lift, zNose = z + Nose + lift;
			RunnerQuad(new Vector3(0, yLow, zRiser), new Vector3(0, yTreadBot, zRiser), Vector3.Back, (yTreadBot - yLow) * 1.3f);
			// underside lip of the nosing
			k.Quad(new Vector3(-cw, yTreadBot - 0.001f, zRiser), new Vector3(cw, yTreadBot - 0.001f, zRiser),
				new Vector3(cw, yTreadBot - 0.001f, zNose), new Vector3(-cw, yTreadBot - 0.001f, zNose), Vector3.Down);
			RunnerQuad(new Vector3(0, yTreadBot, zNose), new Vector3(0, yTop, zNose), Vector3.Back, TreadT * 1.3f);
			float zEnd = last ? (IncludeLanding ? zBack + 0.16f : zBack + 0.03f) : z - Run;
			RunnerQuad(new Vector3(0, yTop, zNose), new Vector3(0, yTop, zEnd), Vector3.Up, (zNose - zEnd) * 1.3f);
			// stair rod where tread meets the next riser
			if (!last)
			{
				k.Mat(brass).Cylinder(new Vector3(-cw - 0.03f, yTop + 0.008f, z - Run + 0.014f),
					new Vector3(cw + 0.03f, yTop + 0.008f, z - Run + 0.014f), 0.0065f, 0.0065f, 5, false);
				k.Mat(carpet);
			}
		}

		// --- landing extras: baseboard on the far edge, a floor register ---
		if (IncludeLanding)
		{
			float bbH = 0.11f, bbT = 0.018f;
			k.Mat(trim).Box(new Vector3(0, H + bbH * 0.5f, zBack + bbT * 0.5f + 0.001f), new Vector3(W, bbH, bbT), 3f);
			// shoe moulding
			k.Box(new Vector3(0, H + 0.009f, zBack + bbT + 0.008f), new Vector3(W, 0.018f, 0.016f), 3f);
			// cap bead on top of the baseboard
			k.Box(new Vector3(0, H + bbH + 0.006f, zBack + bbT * 0.5f + 0.002f), new Vector3(W, 0.012f, bbT + 0.006f), 3f);
			// floor register beside the runner, near the far edge
			float vx = -RailSide * (cw + (hw - cw) * 0.5f);
			k.Mat(vent).Box(new Vector3(vx, H + 0.003f, zBack + 0.3f), new Vector3(0.12f, 0.006f, 0.26f));
			for (int s = 0; s < 6; s++)
				k.Mat(paint).Box(new Vector3(vx, H + 0.0065f, zBack + 0.19f + s * 0.044f), new Vector3(0.12f, 0.002f, 0.012f), 4f);
		}

		// --- railing ---
		float xr = RailSide * (hw + PanelT * 0.5f);
		float NoseLine(float z) => Rise - z * Rise / Run;
		const float railH = 0.86f;
		if (IncludeRailing)
		{
			float newelZ = -0.12f;
			float newelTop = NoseLine(newelZ) + railH + 0.22f;
			k.Mat(paint).Box(new Vector3(xr, newelTop * 0.5f, newelZ), new Vector3(0.09f, newelTop, 0.09f), 2f);
			// newel base block & cap
			k.Box(new Vector3(xr, 0.12f, newelZ), new Vector3(0.11f, 0.24f, 0.11f), 2f);
			k.Mat(tread).Box(new Vector3(xr, newelTop + 0.015f, newelZ), new Vector3(0.125f, 0.03f, 0.125f), 3f);
			k.Blob(new Vector3(xr, newelTop + 0.06f, newelZ), new Vector3(0.045f, 0.04f, 0.045f), 7, 0f, false, 3f);

			// sloped handrail from newel to landing edge
			Vector3 r0 = new(xr, NoseLine(newelZ) + railH, newelZ);
			Vector3 r1 = new(xr, H + railH, zTop);
			k.Mat(tread).Beam(r0, r1, 0.06f, 0.055f, 3f);
			// balusters, two per tread
			k.Mat(paint);
			for (int i = 0; i < N - 1; i++)
				for (int b = 0; b < 2; b++)
				{
					float z = -i * Run - Run * (b == 0 ? 0.28f : 0.76f);
					if (z > newelZ - 0.1f) continue;
					float yb = NoseLine(z) + Skirt - 0.01f;
					float yt = NoseLine(z) + railH - 0.03f;
					k.Box(new Vector3(xr, (yb + yt) * 0.5f, z), new Vector3(0.032f, yt - yb, 0.032f), 3f);
				}
			// landing run of the railing to a second newel
			float zEndNewel = zBack + 0.06f;
			float landTop = H + railH + 0.18f;
			k.Box(new Vector3(xr, (H + landTop) * 0.5f, zEndNewel), new Vector3(0.09f, landTop - H, 0.09f), 2f);
			k.Mat(tread).Box(new Vector3(xr, landTop + 0.015f, zEndNewel), new Vector3(0.125f, 0.03f, 0.125f), 3f);
			k.Blob(new Vector3(xr, landTop + 0.06f, zEndNewel), new Vector3(0.045f, 0.04f, 0.045f), 8, 0f, false, 3f);
			if (zTop - zEndNewel > 0.1f)
			{
				k.Beam(r1, new Vector3(xr, H + railH, zEndNewel), 0.06f, 0.055f, 3f);
				k.Mat(paint);
				for (float z = zTop - 0.12f; z > zEndNewel + 0.06f; z -= 0.13f)
				{
					float yb = H + Skirt - 0.01f, yt = H + railH - 0.03f;
					k.Box(new Vector3(xr, (yb + yt) * 0.5f, z), new Vector3(0.032f, yt - yb, 0.032f), 3f);
				}
			}
		}

		var mi = k.CommitTo(gen, "StairMesh");

		if (BuildCollision && !Engine.IsEditorHint())
		{
			var body = new StaticBody3D { Name = "StairBody", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "wood");
			gen.AddChild(body);
			float x0 = -hw - PanelT, x1 = hw + PanelT;
			var pts = new List<Vector3>();
			foreach (float x in new[] { x0, x1 })
			{
				pts.Add(new Vector3(x, 0, Run));            // ramp foot, one run in front of the first riser
				pts.Add(new Vector3(x, H, zTop));           // nosing line meets the landing
				pts.Add(new Vector3(x, H, zBack));
				pts.Add(new Vector3(x, 0, zBack));
			}
			body.AddChild(new CollisionShape3D { Name = "Ramp", Shape = new ConvexPolygonShape3D { Points = pts.ToArray() } });
			if (IncludeRailing)
			{
				// railing wall along the rail side: sloped section + landing section
				Vector3 a = new(xr, NoseLine(0) + 0.5f, 0), b = new(xr, H + 0.5f, zTop);
				Vector3 d = b - a;
				var basis = Basis.LookingAt(d.Normalized(), Vector3.Up);
				body.AddChild(new CollisionShape3D
				{
					Name = "RailWall", Transform = new Transform3D(basis, (a + b) * 0.5f),
					Shape = new BoxShape3D { Size = new Vector3(0.08f, 1.1f, d.Length()) },
				});
				body.AddChild(new CollisionShape3D
				{
					Name = "RailWallLanding", Position = new Vector3(xr, H + 0.5f, (zTop + zBack) * 0.5f),
					Shape = new BoxShape3D { Size = new Vector3(0.08f, 1.1f, zTop - zBack) },
				});
			}
		}

		// Move the scene's hook nodes to where this configuration puts them (runtime only, so the editor never dirties the scene).
		if (Engine.IsEditorHint()) return;
		if (GetNodeOrNull<Node3D>("TopTrigger") is Node3D trig)
		{
			trig.Position = new Vector3(0, H, (zTop + zBack) * 0.5f);
			if (trig.GetNodeOrNull<CollisionShape3D>("Shape") is CollisionShape3D cs)
			{
				cs.Position = new Vector3(0, 0.9f, 0);
				cs.Shape = new BoxShape3D { Size = new Vector3(W, 1.8f, Mathf.Max(0.3f, (zTop - zBack) * 0.9f)) };
			}
		}
		if (GetNodeOrNull<Node3D>("AutotestApproach") is Node3D appr)
			appr.Position = new Vector3(0, 0, 1.5f);
	}
}
