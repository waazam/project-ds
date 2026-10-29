using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;

namespace ProjectDS.World;

/// <summary>
/// Act 1 shot list: a small waterfall on the creek above the footbridge. Places itself where
/// the stream bed drops fastest between <see cref="SearchFrom"/> and <see cref="SearchTo"/>
/// metres upstream of the trail crossing, and builds, all in code:
/// - a rock ledge across the channel (the water slides over its flat top) with a wet rock
///   face under it and stacked boulders framing it on both banks (solid);
/// - the water from the lip to the pool (the gushy-water pass, 2026-10-15: <see cref="FlowFx"/>'s pour): dark glassy
///   water over the ledge, then a curtain combed into ropes, whitening as it falls, surging, its sides ragged, with
///   three strands of it standing out over the curtain;
/// - where it lands: a churning boil of white water, spray thrown up off it, a thin mist (the flat foam patch
///   that drifted below it is gone: the creek's own foam streaks carry the white water on down);
/// - (2026-10-29, the owner's friend's playtest: it "looks like molten metal") its own shader, waterfall.gdshader: a
///   lighter glass body you see the rock through, white aerated foam from just over the lip, fine streaks sliding
///   faster than before, a soft sheen and no metal glints; and more spray and a mist that rises and drifts at its foot;
/// - a quiet looping water sound on the Water bus (the creek's own loop, pitched down: the
///   stream audio already along the creek carries most of it);
/// - clear zones along the creek down to the bridge so the fall can be seen from the deck.
/// Photographable from the bridge and from the bank (<see cref="PhotoSubject"/> "waterfall").
/// </summary>
[GlobalClass]
public partial class Waterfall : Node3D
{
	[Export] public float SearchFrom = 12f;
	[Export] public float SearchTo = 45f;
	/// <summary>Stream length over which the drop is measured and the fall is built.</summary>
	[Export] public float Window = 5f;
	[Export] public float CurtainWidth = 1.5f;
	[Export] public float SoundVolumeDb = -9f;
	[Export] public int Seed = 23;

	private ForestTerrain _terrain;
	private Polyline2 _stream;
	private Vector3 _o;
	private readonly RandomNumberGenerator _rng = new();

	/// <summary>World position of the curtain's middle, for tests and the photo preview.</summary>
	public Vector3 CurtainCenter { get; private set; }
	/// <summary>World position of the lip's middle.</summary>
	public Vector3 LipCenter { get; private set; }

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_terrain = GroundSnap.FindTerrain(this);
		if (_terrain == null || _terrain.Stream == null || _terrain.Stream.Points.Count < 2) return;
		if (!_terrain.TryGetStreamCrossing(out Vector3 cross, out _, out _)) return;
		_stream = _terrain.Stream;
		_o = _terrain.GlobalPosition;
		_rng.Seed = (ulong)Seed;
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;

		_stream.Closest(new Vector2(cross.X - _o.X, cross.Z - _o.Z), out float sCross);
		float best = -1f, sc = sCross - SearchFrom;
		for (float s = sCross - SearchTo; s <= sCross - SearchFrom; s += 0.5f)
		{
			float drop = Level(s - Window * 0.5f) - Level(s + Window * 0.5f);
			if (drop > best) { best = drop; sc = s; }
		}
		Build(sc, sCross);
		GD.Print($"[waterfall] at stream s {sc:0.0} ({sCross - sc:0.0} m above the crossing), drop {best:0.00} m, curtain {CurtainCenter}");
	}

	// ------------------------------------------------------------------ stream frame

	private float Level(float s) => _terrain.WaterLevel(s);
	private Vector2 Flat(float s, out Vector2 dir) { var p = _stream.At(s, out dir); dir = dir.Normalized(); return p + new Vector2(_o.X, _o.Z); }
	/// <summary>World point at stream s, <paramref name="lat"/> metres to the right of the flow, at height y.</summary>
	private Vector3 P(float s, float lat, float y)
	{
		var p = Flat(s, out Vector2 d);
		var n = new Vector2(-d.Y, d.X);
		var q = p + n * lat;
		return new Vector3(q.X, y, q.Y);
	}
	private Vector3 Ground(Vector3 w) => new(w.X, _terrain.HeightAt(w.X, w.Z), w.Z);
	/// <summary>Lowest ground within r of w (centre and a ring of six).</summary>
	private float LowUnder(Vector3 w, float r)
	{
		float lo = _terrain.HeightAt(w.X, w.Z);
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Tau * i / 6f;
			lo = Mathf.Min(lo, _terrain.HeightAt(w.X + Mathf.Cos(a) * r, w.Z + Mathf.Sin(a) * r));
		}
		return lo;
	}

	// ------------------------------------------------------------------ build

	private void Build(float sc, float sCross)
	{
		float sLip = sc - Window * 0.5f, sPool = sc + Window * 0.5f;
		float lipY = Level(sLip), poolY = Level(sPool);
		float sCurt = sPool - 0.9f;                 // the curtain's foot
		float sTop = sCurt - 0.45f;                 // where the water leaves the lip
		float curtBottom = Level(sCurt) + 0.02f;
		LipCenter = P(sTop, 0, lipY);
		CurtainCenter = P((sTop + sCurt) * 0.5f, 0, (lipY + curtBottom) * 0.5f);

		var body = new StaticBody3D { Name = "Rocks", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "rock");
		AddChild(body);
		var k = new MeshKit();
		var rock = ProcTextures.RockTriMat;
		int id = 0;
		void Rock(Vector3 c, Vector3 r, float wet, bool collide, bool onGround = true)
		{
			float g = _rng.RandfRange(0.85f, 1.15f) * (1f - 0.35f * wet);
			k.Color = new Color(g, g * 1.01f, g * 0.97f);
			// Every stone stands on the ground: same top, but reaching down to the lowest ground under it
			// (the channel and the pool are deeper than the water line these are placed by). A stone set on
			// another one (onGround false) rests on it as it is. Collision stays the stone's upper, visible part.
			Vector3 cv = c, rv = r;
			float bed = LowUnder(c, Mathf.Min(r.X, r.Z) * 0.9f) - 0.05f;
			if (onGround && c.Y - r.Y > bed)
			{
				float top = c.Y + r.Y;
				cv.Y = (top + bed) * 0.5f;
				rv.Y = (top - bed) * 0.5f;
			}
			k.Mat(rock).Blob(cv, rv, Seed * 101 + id++, 0.2f, true, 1f, 0.35f);
			if (collide && r.Y > 0.25f)
				body.AddChild(new CollisionShape3D { Position = c, Shape = new SphereShape3D { Radius = Mathf.Min(r.X, Mathf.Min(r.Y, r.Z)) * 0.95f } });
		}

		// the ledge: rows of flat-topped rock filling the channel under the lip water (centre tops just
		// under the water, the sides standing higher), so the sloped stream bed under it is hidden
		for (float s = sLip + 0.3f; s <= sTop + 0.05f; s += 0.8f)
			for (float lat = -2.6f; lat <= 2.61f; lat += 0.75f)
			{
				float side = Mathf.Abs(lat);
				float top = lipY - 0.04f + Mathf.Max(0f, side - 0.9f) * 0.45f + _rng.RandfRange(-0.03f, 0.05f);
				float bed = Level(s) - 0.6f;
				float ry = Mathf.Max(0.3f, (top - bed) * 0.5f);
				Rock(P(s, lat + _rng.RandfRange(-0.1f, 0.1f), top - ry), new Vector3(0.55f, ry, 0.55f), side < 0.9f ? 1f : 0.4f, side > 0.9f);
			}
		// the face behind the curtain: tall wet stones from the pool up to the lip
		for (float lat = -2.2f; lat <= 2.21f; lat += 0.62f)
		{
			float side = Mathf.Abs(lat);
			float top = lipY - 0.02f + Mathf.Max(0f, side - 0.9f) * 0.45f;
			float bot = curtBottom - 0.3f;
			float ry = (top - bot) * 0.5f;
			Rock(P(sTop - 0.25f, lat, bot + ry), new Vector3(0.42f, ry, 0.34f), 1f, side > 0.9f);
		}
		// stacked boulders on both banks framing the fall, big at the foot, smaller on top
		foreach (float side in new[] { -1f, 1f })
			for (int i = 0; i < 7; i++)
			{
				float s = Mathf.Lerp(sLip - 0.3f, sPool + 0.6f, i / 6f) + _rng.RandfRange(-0.3f, 0.3f);
				float lat = side * (2.4f + _rng.RandfRange(0f, 0.8f));
				var g = Ground(P(s, lat, 0));
				float r = _rng.RandfRange(0.55f, 0.9f);
				float top = Mathf.Max(g.Y + r * 0.9f, Level(s) + 0.4f);
				Rock(new Vector3(g.X, top - r * 0.7f, g.Z), new Vector3(r, r * 0.8f, r * 0.9f), 0.2f, true);
				if (i % 2 == 0)
				{
					float r2 = r * _rng.RandfRange(0.45f, 0.65f);
					Rock(new Vector3(g.X, top + r2 * 0.3f, g.Z) + new Vector3(_rng.RandfRange(-0.2f, 0.2f), 0, _rng.RandfRange(-0.2f, 0.2f)),
						new Vector3(r2, r2 * 0.75f, r2), 0.1f, true, onGround: false);
				}
			}
		// the pool's rim: a few low stones breaking the water below the fall
		for (int i = 0; i < 6; i++)
		{
			float s = sCurt + _rng.RandfRange(0.9f, 2.6f);
			float lat = _rng.RandfRange(-1.7f, 1.7f);
			if (Mathf.Abs(lat) < 0.6f) lat = Mathf.Sign(lat + 0.001f) * 0.9f;
			float r = _rng.RandfRange(0.18f, 0.35f);
			Rock(P(s, lat, Level(s) - r * 0.25f), new Vector3(r * 1.2f, r * 0.6f, r), 1f, false);
		}
		k.Color = Colors.White;
		k.CommitTo(this, "RockMesh");

		BuildWater(sLip, sTop, sCurt, lipY, curtBottom);

		Flat(sCurt, out Vector2 flow);
		BuildMist(P(sCurt, 0, curtBottom + 0.15f), flow);
		// (the gushy-water pass) where it lands: a boil of white water, and spray thrown up off it
		// (over the creek's own surface and its waves: it writes depth, and would hide a churn sat on it)
		FlowFx.Churn(this, P(sCurt + 0.25f, 0, Level(sCurt + 0.25f) + 0.1f), 1.35f, FlowFx.Liquid.Creek, 1f, "FootChurn");
		FlowFx.Spray(this, P(sCurt, 0, curtBottom + 0.05f), FlowFx.Liquid.Creek, CurtainWidth * 0.9f, 56, 2.4f, "FootSpray");
		// (2026-10-29) more spray off it, finer and higher, and the white haze of it hanging round the foot
		FlowFx.Spray(this, P(sCurt + 0.15f, 0, curtBottom + 0.1f), FlowFx.Liquid.Creek, CurtainWidth * 1.1f, 40, 3.2f, "FootSprayFine");
		FlowFx.Mist(this, P(sCurt + 0.6f, 0, curtBottom + 0.4f), new Vector3(flow.X, 0.25f, flow.Y) * 0.25f, 2.2f, 14, 0.14f, "FootHaze");
		BuildSound(P(sCurt, 0, curtBottom + 0.5f));
		BuildClearZones(sLip, sPool, sCross);

		AddChild(new PhotoSubject
		{
			Name = "PhotoSubject",
			Id = "waterfall",
			// the curtain's middle and its lip, a little in front of the water
			LookPoints = new[] { CurtainCenter + Dir3(sCurt) * 0.3f, LipCenter + Dir3(sCurt) * 0.3f + Vector3.Up * 0.1f, P(sCurt + 0.6f, 0, curtBottom + 0.2f) },
			MinDistance = 3f,
			MaxDistance = 45f,
			ConeDegrees = 16f,
			OwnerPath = "..",
		});
	}

	private Vector3 Dir3(float s) { Flat(s, out Vector2 d); return new Vector3(d.X, 0, d.Y); }

	// ------------------------------------------------------------------ water

	/// <summary>The lip (flat water over the ledge) and the curtain (the drop), one strip, UV.x across, UV.y along the flow.</summary>
	private void BuildWater(float sLip, float sTop, float sCurt, float lipY, float bottomY)
	{
		// the path of the water's centre: along the ledge, over the edge, a slight outward arc, down to the pool
		var path = new List<Vector3>();
		const int lipSteps = 4, fallSteps = 10;
		for (int i = 0; i <= lipSteps; i++)
		{
			float s = Mathf.Lerp(sLip, sTop, i / (float)lipSteps);
			path.Add(P(s, 0, Mathf.Max(lipY, Level(s)) + 0.015f));
		}
		for (int i = 1; i <= fallSteps; i++)
		{
			float v = i / (float)fallSteps;
			float s = Mathf.Lerp(sTop, sCurt, Mathf.Sqrt(v) * 0.85f + v * 0.15f);   // shoots out, then falls near-vertical
			path.Add(P(s, 0, Mathf.Lerp(lipY + 0.015f, bottomY, v * v * 0.6f + v * 0.4f)));
		}
		// (the gushy-water pass, 2026-10-15: was a flat strip with a scrolling streak texture) the pour shader: dark glassy
		// water over the lip, then ropes of it, whitening as they fall, surging and breaking up at the sides
		float lipLen = 0f;
		for (int i = 0; i < lipSteps; i++) lipLen += path[i].DistanceTo(path[i + 1]);
		var mat = FallMat(speed: 4.2f, strands: 8f, wobble: 0.1f, aerateLen: 1.3f);
		mat.SetShaderParameter("aerate_start", lipLen - 0.35f);
		mat.SetShaderParameter("rope", 1.0f);
		var k = new MeshKit();
		k.Mat(mat);
		const int across = 8;
		float vAcc = 0f;
		for (int i = 0; i < path.Count - 1; i++)
		{
			bool falling = i >= lipSteps;
			float t0 = i / (float)(path.Count - 1), t1 = (i + 1) / (float)(path.Count - 1);
			float w0 = CurtainWidth * (falling ? 1f + 0.2f * (i - lipSteps) / fallSteps : 1.1f);
			float w1 = CurtainWidth * (falling ? 1f + 0.2f * (i + 1 - lipSteps) / fallSteps : 1.1f);
			Vector3 a = path[i], b = path[i + 1];
			float seg = a.DistanceTo(b);
			float s0 = Mathf.Lerp(sLip, sCurt, t0), s1 = Mathf.Lerp(sLip, sCurt, t1);
			Vector3 n0 = Right(s0), n1 = Right(s1);
			for (int j = 0; j < across; j++)
			{
				float u0 = j / (float)across, u1 = (j + 1) / (float)across;
				Vector3 p00 = a + n0 * (u0 - 0.5f) * w0, p10 = a + n0 * (u1 - 0.5f) * w0;
				Vector3 p01 = b + n1 * (u0 - 0.5f) * w1, p11 = b + n1 * (u1 - 0.5f) * w1;
				// (the gushy-water pass: the colour is one per triangle, so the old fade in at the lip and out at the foot cut a
				// sawtooth across the strip; the pour shader keeps the lip dark and rags the foot itself, and the churn hides it)
				const float c0 = 1f, c1 = 1f, a0 = 1f, a1 = 1f;
				var nrm = (p10 - p00).Cross(p01 - p00).Normalized();
				k.Color = new Color(c0, c0, c0, a0);
				k.Tri(p00, p10, p11, nrm, new Vector2(u0, vAcc), new Vector2(u1, vAcc), new Vector2(u1, vAcc + seg));
				k.Color = new Color(c1, c1, c1, a1);
				k.Tri(p00, p11, p01, nrm, new Vector2(u0, vAcc), new Vector2(u1, vAcc + seg), new Vector2(u0, vAcc + seg));
			}
			vAcc += seg;
		}
		var mi = new MeshInstance3D { Name = "Water", Mesh = k.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(mi);
		// ropes over the curtain: thick strands standing out of it, two at its ragged sides, so it's never a flat sheet
		var fall = path.GetRange(lipSteps, path.Count - lipSteps);
		var rope = FallMat(speed: 4.6f, strands: 3f, tube: true, wobble: 0.05f, aerateLen: 0.7f);
		float[] lats = { -0.47f, 0.12f, 0.49f };
		for (int r = 0; r < lats.Length; r++)
		{
			var pts = new List<Vector3>();
			for (int i = 1; i < fall.Count; i++)
			{
				float v = i / (float)(fall.Count - 1);
				float s = Mathf.Lerp(sTop - 0.1f, sCurt, v);
				pts.Add(fall[i] + Right(s) * lats[r] * CurtainWidth * (1f + 0.2f * v) + Dir3(s) * (0.03f + 0.04f * v));
			}
			float r0 = Mathf.Abs(lats[r]) > 0.4f ? 0.05f : 0.07f;
			FlowFx.Tube(this, pts, r0, r0 * 1.7f, rope, $"Rope{r}", 7);
		}
	}

	private Vector3 Right(float s) { Flat(s, out Vector2 d); return new Vector3(-d.Y, 0, d.X); }

	private static Shader _fallShader;

	/// <summary>(2026-10-29) The curtain's and the ropes' material: waterfall.gdshader (water, not FlowFx's pour, which
	/// read as molten metal here).</summary>
	private static ShaderMaterial FallMat(float speed, float strands, bool tube = false, float wobble = 0.05f, float aerateLen = 1.2f)
	{
		var m = new ShaderMaterial { Shader = _fallShader ??= GD.Load<Shader>("res://assets/shaders/waterfall.gdshader") };
		m.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		m.SetShaderParameter("speed", speed);
		m.SetShaderParameter("strands", strands);
		m.SetShaderParameter("tube", tube ? 1f : 0f);
		m.SetShaderParameter("wobble", wobble);
		m.SetShaderParameter("aerate_len", aerateLen);
		// (the ropes stand out of the curtain: less of the rock seen through them)
		m.SetShaderParameter("glass_alpha", tube ? 0.55f : 0.42f);
		return m;
	}

	/// <summary>A thin, slow mist off the foot of the fall.</summary>
	private void BuildMist(Vector3 at, Vector2 flow)
	{
		var ramp = new Gradient();
		ramp.SetColor(0, new Color(1, 1, 1, 0));
		ramp.SetColor(1, new Color(1, 1, 1, 0));
		ramp.AddPoint(0.3f, new Color(1, 1, 1, 1));
		ramp.AddPoint(0.7f, new Color(1, 1, 1, 0.6f));
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(0.7f, 0.1f, 0.4f),
			Direction = new Vector3(flow.X, 1.2f, flow.Y).Normalized(),
			Spread = 35f,
			InitialVelocityMin = 0.15f,
			InitialVelocityMax = 0.45f,
			Gravity = new Vector3(0, 0.04f, 0),
			ScaleMin = 0.8f,
			ScaleMax = 1.6f,
			ColorRamp = new GradientTexture1D { Gradient = ramp },
		};
		var draw = new QuadMesh { Size = new Vector2(1.1f, 1.1f) };
		draw.Material = new StandardMaterial3D
		{
			AlbedoTexture = PropTextures.Puff(),
			AlbedoColor = new Color(0.92f, 0.94f, 0.95f, 0.11f),   // (fainter: unshaded, at night the puffs glowed as balls; 2026-10-29 a touch more: spray at the base)
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		var mist = new GpuParticles3D
		{
			Name = "Mist",
			Amount = 16,
			Lifetime = 3.2,
			Preprocess = 3.0,
			ProcessMaterial = pm,
			DrawPass1 = draw,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			VisibilityAabb = new Aabb(new Vector3(-3, -1, -3), new Vector3(6, 5, 6)),
		};
		AddChild(mist);
		mist.GlobalPosition = at;
	}

	private void BuildSound(Vector3 at)
	{
		const string path = "res://assets/audio/ambient/stream_loop.wav";
		if (!ResourceLoader.Exists(path)) return;
		var player = new AudioStreamPlayer3D
		{
			Name = "FallSound",
			Stream = GD.Load<AudioStream>(path),
			Bus = "Water",
			UnitSize = 3.5f,
			MaxDistance = 45f,
			PitchScale = 0.82f,
			AttenuationFilterCutoffHz = 5000f,
		};
		player.AddChild(new AmbienceLoop { BaseVolumeDb = SoundVolumeDb });
		AddChild(player);
		player.GlobalPosition = at;
	}

	/// <summary>Keeps trees off the fall and out of the line from the bridge up the creek.</summary>
	private void BuildClearZones(float sLip, float sPool, float sCross)
	{
		int n = 0;
		void Zone(Vector3 p, float r, bool foliage)
		{
			var cz = new ClearZone { Name = $"Clear{n++}", Radius = r, ClearFoliage = foliage };
			AddChild(cz);
			cz.GlobalPosition = p;
		}
		Zone(P((sLip + sPool) * 0.5f, 0, 0), 5.5f, true);
		for (float s = sPool + 3f; s < sCross - 2f; s += 4f) Zone(P(s, 0, 0), 4.2f, false);
	}
}
