using Godot;

namespace ProjectDS.World.LakeParts;

/// <summary>
/// The lake's geometry, in <see cref="Lake"/>-local metres (the Lake node is only ever translated,
/// never rotated, so local axes are world axes). One source of truth for the shoreline, the ground
/// around it and the waves on it: the terrain mesh, the dressing, the water mesh's per-vertex wave
/// fade, and the rowboat all ask here, and <c>lake_water.gdshader</c> mirrors <see cref="WaveHeight"/>
/// term for term, so the boat rides exactly the swell the player sees.
///
/// Layout: an irregular oval of open water, its long axis along -Z. The near beach (the wake-up,
/// the dock and the boat) sits at +Z; the far beach, with the rescue station on a low rise behind it,
/// at the other end. Both beaches are kept low, flat and free of noise so they stay walkable; the
/// rest of the shore rises into wooded banks and then hills that close the bowl off from the sky.
/// </summary>
public static class LakeShape
{
	// Wide enough that the side shores sit far out in the morning mist (it must read as open water,
	// not a pond you could walk round), long enough for a real crossing.
	public const float SemiX = 95f;
	public const float SemiZ = 48f;
	public const float CenterZ = 1f - SemiZ;
	/// <summary>Where the waterline crosses the long axis (x = 0).</summary>
	public const float NearShoreZ = CenterZ + SemiZ;   // +1
	public const float FarShoreZ = CenterZ - SemiZ;    // -83

	/// <summary>The dock: runs from the near beach out over the water along x = 0.</summary>
	public const float DockStartZ = 5.5f, DockEndZ = -7.2f, DockHalfWidth = 0.95f, DockDeck = 0.45f;
	/// <summary>Where the moored boat sits (alongside the dock's end, bow toward the far shore).</summary>
	public static readonly Vector2 BoatMooring = new(1.85f, -5.4f);
	/// <summary>Where the boat's bow grounds on the far beach.</summary>
	public static readonly Vector2 BoatLanding = new(0.4f, FarShoreZ + 1.9f);
	public static readonly Vector2 NearClearing = new(0f, 9f);
	public static readonly Vector2 StationSite = new(0f, FarShoreZ - 20f);
	public const float StationFloor = 1.05f;

	private static FastNoiseLite _bank, _hill, _bump;

	private static void Init()
	{
		if (_bank != null) return;
		_bank = new FastNoiseLite { Seed = 1207, Frequency = 0.045f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
		_hill = new FastNoiseLite { Seed = 1208, Frequency = 0.018f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FractalOctaves = 3 };
		_bump = new FastNoiseLite { Seed = 1209, Frequency = 0.16f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
	}

	/// <summary>0..1: how much a direction from the lake's centre points at one of the two beaches.</summary>
	private static float BeachMask(float theta, float width)
	{
		float near = Mathf.Exp(-theta * theta / width);
		float far = Mathf.Abs(theta) - Mathf.Pi;
		return Mathf.Max(near, Mathf.Exp(-far * far / width));
	}

	private static float Theta(float x, float z) => Mathf.Atan2(x / SemiX, (z - CenterZ) / SemiZ);

	/// <summary>Metres from the shoreline: positive on land, negative out over the water. An oval
	/// with a wobbly edge everywhere except the two beaches, which stay put.</summary>
	public static float ShoreDist(float x, float z)
	{
		float nx = x / SemiX, nz = (z - CenterZ) / SemiZ;
		float rho = Mathf.Sqrt(nx * nx + nz * nz);
		float th = Mathf.Atan2(nx, nz);
		float beach = BeachMask(th, 0.09f);
		float wob = 0.075f * Mathf.Sin(3f * th + 0.6f) + 0.05f * Mathf.Sin(5f * th + 2.1f) + 0.022f * Mathf.Sin(11f * th + 0.3f);
		float rmod = 1f + (1f - beach) * wob;
		float radius = Mathf.Sqrt(Mathf.Pow(SemiX * Mathf.Sin(th), 2) + Mathf.Pow(SemiZ * Mathf.Cos(th), 2));
		return (rho / rmod - 1f) * radius * rmod;
	}

	/// <summary>The ground (or lake bed) height at a point.</summary>
	public static float Ground(float x, float z)
	{
		Init();
		float d = ShoreDist(x, z);
		if (d < 0f) return -0.05f - Mathf.Min(2.8f, -d * 0.16f);

		float th = Theta(x, z);
		float beach = BeachMask(th, 0.16f);
		float h = d * 0.05f - 0.05f;
		float bankH = Mathf.Lerp(2.4f + 2.6f * _bank.GetNoise2D(x, z), 0.35f, beach);
		h += Mathf.SmoothStep(4f, 17f, d) * bankH;
		float hillH = 7f + 8f * _hill.GetNoise2D(x, z);
		h += Mathf.SmoothStep(20f, 75f, d) * hillH * (1f - 0.4f * beach);

		// The two flat places the player actually walks: the wake-up clearing and the station's rise.
		float nearFlat = 1f - Mathf.SmoothStep(11f, 19f, new Vector2(x, z).DistanceTo(NearClearing));
		float station = 1f - Mathf.SmoothStep(14f, 24f, new Vector2(x, z).DistanceTo(StationSite));
		float rough = (1f - Mathf.Max(nearFlat, station)) * Mathf.SmoothStep(2f, 9f, d);
		h += _bump.GetNoise2D(x, z) * 0.4f * rough;
		// the station stands on a low, levelled rise (the floor sits on short piers above it)
		float rise = StationFloor - 0.45f + 0.012f * (z - StationSite.Y);
		h = Mathf.Lerp(h, Mathf.Max(rise, d * 0.05f - 0.05f), station * Mathf.SmoothStep(0f, 6f, d));
		return h;
	}

	/// <summary>Surface normal of <see cref="Ground"/> (central differences).</summary>
	public static Vector3 GroundNormal(float x, float z, float e = 0.5f)
	{
		float hx = Ground(x + e, z) - Ground(x - e, z);
		float hz = Ground(x, z + e) - Ground(x, z - e);
		return new Vector3(-hx, 2f * e, -hz).Normalized();
	}

	/// <summary>How much of the waves a point on the water carries: 0 at the shoreline, easing to 1
	/// by ten metres out, so the swell never climbs up the banks or through the dock.</summary>
	public static float WaveFade(float x, float z) => Mathf.SmoothStep(0.5f, 10f, -ShoreDist(x, z));

	// ------------------------------------------------------------------ waves

	/// <summary>The live wave state the lake pushes to its shader every frame.</summary>
	public struct Waves
	{
		/// <summary>The waves' own clock (it runs a little faster in a storm, so it is not TIME).</summary>
		public float Time;
		/// <summary>0 = a mirror-still sunrise lake, 1 = the white-capped chop after the breach.</summary>
		public float Intensity;
		public Vector2 BulgeAt;
		/// <summary>Metres the water domes up at <see cref="BulgeAt"/> (the creature rising under it).</summary>
		public float Bulge;
		public Vector2 RingAt;
		/// <summary>Radius of the one big ring wave the breach sends out, and its height.</summary>
		public float RingRadius, RingHeight;
		/// <summary>The thing under the lake, seen through the water (drawn by the shader only; it never
		/// moves the surface): a vast shadow at <see cref="ShadowAt"/> (radius, 0..1 darkness), and a
		/// single eye the size of a house at <see cref="EyeAt"/> — <see cref="EyeOpen"/> 0 shut .. 1
		/// open, <see cref="EyePupil"/> 0 a hair-thin slit .. 1 blown wide, <see cref="EyeLook"/> where
		/// in its iris the pupil sits (-1..1).</summary>
		public Vector2 ShadowAt, EyeAt, EyeLook;
		/// <summary>The eye's long axis (unit, Lake-local xz).</summary>
		public Vector2 EyeAxis;
		public float ShadowRadius, Shadow, EyeRadius, EyeOpen, EyePupil;
	}

	// Four long-crested swells. The storm set is dominated by waves running toward +Z, back toward
	// the near shore: the chop the creature leaves behind is literally pushing the boat home.
	private static readonly Vector2[] Dir =
	{
		new Vector2(0.18f, 1f).Normalized(), new Vector2(-0.55f, 1f).Normalized(),
		new Vector2(0.9f, 0.45f).Normalized(), new Vector2(-0.3f, -1f).Normalized(),
	};
	private static readonly float[] Length = { 13f, 8.5f, 5.5f, 3.6f };
	private static readonly float[] CalmAmp = { 0.035f, 0.022f, 0.012f, 0.008f };
	// Kept low on purpose: the chop after the breach must read as rough without heaving the view about
	// (it gave the owner a headache at nearly three times this).
	private static readonly float[] StormAmp = { 0.15f, 0.085f, 0.045f, 0.022f };
	private static readonly float[] Phase = { 0f, 1.7f, 4.1f, 2.6f };
	public const float BulgeRadius = 5.5f;
	public const float RingWidth = 3.2f;

	/// <summary>Surface height and slope (dh/dx, dh/dz) of the water at a Lake-local point.</summary>
	public static float WaveHeight(in Waves w, float x, float z, out Vector2 slope)
	{
		float fade = WaveFade(x, z);
		float h = 0f;
		slope = Vector2.Zero;
		for (int i = 0; i < 4; i++)
		{
			float k = Mathf.Tau / Length[i];
			float omega = Mathf.Sqrt(9.8f * k);
			float a = Mathf.Lerp(CalmAmp[i], StormAmp[i], w.Intensity) * fade;
			float arg = k * (Dir[i].X * x + Dir[i].Y * z) - omega * w.Time + Phase[i];
			h += a * Mathf.Sin(arg);
			slope += Dir[i] * (a * k * Mathf.Cos(arg));
		}
		if (w.Bulge > 0.001f)
		{
			Vector2 d = new Vector2(x, z) - w.BulgeAt;
			float g = w.Bulge * Mathf.Exp(-d.LengthSquared() / (BulgeRadius * BulgeRadius));
			h += g;
			slope += d * (-2f / (BulgeRadius * BulgeRadius)) * g;
		}
		if (w.RingHeight > 0.001f)
		{
			Vector2 d = new Vector2(x, z) - w.RingAt;
			float r = d.Length();
			float u = (r - w.RingRadius) / RingWidth;
			float g = w.RingHeight * Mathf.Exp(-u * u) * fade;
			h += g;
			if (r > 0.01f) slope += d / r * (-2f * u / RingWidth) * g;
		}
		return h;
	}

	public static float WaveHeight(in Waves w, float x, float z) => WaveHeight(w, x, z, out _);

	/// <summary>The rotation that tips something floating flat (up = +Y) onto a surface with this slope.</summary>
	public static Basis SurfaceTilt(Vector2 slope)
	{
		float s = slope.Length();
		if (s < 0.0005f) return Basis.Identity;
		// normal = (-sx, 1, -sz); axis = up x normal
		return new Basis(new Vector3(-slope.Y, 0f, slope.X) / s, Mathf.Atan(s));
	}

	/// <summary>Pushes the wave state to the water shader (uniform names match lake_water.gdshader).</summary>
	public static void Apply(in Waves w, ShaderMaterial m)
	{
		if (m == null) return;
		m.SetShaderParameter("wave_time", w.Time);
		m.SetShaderParameter("wave_intensity", w.Intensity);
		m.SetShaderParameter("bulge", new Vector3(w.BulgeAt.X, w.BulgeAt.Y, w.Bulge));
		m.SetShaderParameter("ring", new Vector4(w.RingAt.X, w.RingAt.Y, w.RingRadius, w.RingHeight));
		m.SetShaderParameter("shadow", new Vector4(w.ShadowAt.X, w.ShadowAt.Y, w.ShadowRadius, w.Shadow));
		m.SetShaderParameter("deep_eye", new Vector4(w.EyeAt.X, w.EyeAt.Y, w.EyeRadius, w.EyeOpen));
		m.SetShaderParameter("deep_eye_pupil", new Vector3(w.EyePupil, w.EyeLook.X, w.EyeLook.Y));
		m.SetShaderParameter("deep_eye_axis", w.EyeAxis.LengthSquared() > 0.01f ? w.EyeAxis.Normalized() : Vector2.Right);
	}

	/// <summary>The shader's copy of the wave tables, as uniform arrays, so the two can never drift.</summary>
	public static void ApplyTables(ShaderMaterial m)
	{
		var dirLen = new Vector4[4];
		var amps = new Vector4[4];
		for (int i = 0; i < 4; i++)
		{
			dirLen[i] = new Vector4(Dir[i].X, Dir[i].Y, Length[i], Phase[i]);
			amps[i] = new Vector4(CalmAmp[i], StormAmp[i], 0, 0);
		}
		m.SetShaderParameter("wave_dir_len", dirLen);
		m.SetShaderParameter("wave_amps", amps);
		m.SetShaderParameter("bulge_radius", BulgeRadius);
		m.SetShaderParameter("ring_width", RingWidth);
	}
}
