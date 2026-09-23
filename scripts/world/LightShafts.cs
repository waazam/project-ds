using System;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Cheap PS2-flavoured light shafts: a handful of long, thin additive quads hung from the sun's
/// direction and scattered around the player, so the bright sky and the dark trees stop meeting
/// at a hard edge (Dan, 2026-09-22: "light shafts or something so it doesn't seem like such a
/// strong contrast"). One MultiMesh, one procedural gradient texture, no lights, no volumetrics.
///
/// Each shaft: a ground anchor within <see cref="Radius"/> of the player, a quad whose long axis
/// runs up toward the sun, turned to face the camera about that axis, alpha faded by distance,
/// by how much the camera looks along the shaft (end-on it would read as a blob), by a slow
/// drift, and by the atmosphere's mood: warm and strong on the open Act 1 trail, fainter in the
/// deep woods, a cold faint moon at night, off indoors and in rain. Anchors that fall behind are
/// re-rolled ahead of the player. <see cref="ForestAtmosphere"/> owns one and sets its strength.
/// </summary>
public partial class LightShafts : Node3D
{
	[Export] public int Count = 36;
	/// <summary>Anchors are rolled within this distance of the player (and dropped beyond 1.3x it).</summary>
	[Export] public float Radius = 60f;
	[Export] public Vector2 Length = new(22f, 44f);
	[Export] public Vector2 Width = new(0.7f, 2.4f);
	/// <summary>0..1 overall strength; the atmosphere drives it (mood, indoors, storm).</summary>
	public float Strength { get; set; } = 1f;
	/// <summary>The shafts' colour and peak additive intensity (the atmosphere sets these per mood).</summary>
	public Color Tint { get; set; } = new(1f, 0.9f, 0.7f);
	public float Intensity { get; set; } = 0.16f;
	/// <summary>The light's travel direction (the sun's -Z); shafts run up against it.</summary>
	public Vector3 LightDir { get; set; } = new(0.3f, -0.8f, 0.5f);
	/// <summary>0..1 how much the shafts are pulled toward vertical: a low sun gives near-horizontal
	/// streaks that read as smears, so they come down steeper than the light really does.</summary>
	[Export] public float Steepen = 0.55f;
	/// <summary>For tests: how many shafts drew with a visible alpha last frame.</summary>
	public int VisibleCount { get; private set; }

	private MultiMeshInstance3D _mmi;
	private MultiMesh _mm;
	private readonly RandomNumberGenerator _rng = new();
	private Vector3[] _anchor;
	private float[] _len, _wid, _phase, _gain;
	private ForestTerrain _terrain;
	private bool _terrainSearched;
	private float _t;

	public override void _Ready()
	{
		_rng.Seed = 4471;
		_anchor = new Vector3[Count]; _len = new float[Count]; _wid = new float[Count]; _phase = new float[Count]; _gain = new float[Count];
		var mat = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			VertexColorUseAsAlbedo = true,
			AlbedoTexture = ShaftTexture(),
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
			NoDepthTest = false,
			DisableReceiveShadows = true,
			DisableFog = false,
		};
		_mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = new QuadMesh { Size = Vector2.One, Material = mat },
			InstanceCount = Count,
		};
		_mmi = new MultiMeshInstance3D { Name = "Shafts", Multimesh = _mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
		// A generous bound: the quads are moved every frame, the box must cover them wherever they go.
		_mmi.CustomAabb = new Aabb(new Vector3(-4000, -500, -4000), new Vector3(8000, 1000, 8000));
		AddChild(_mmi);
		for (int i = 0; i < Count; i++) { _anchor[i] = new Vector3(float.NaN, 0, 0); Roll(i); }
	}

	/// <summary>A soft vertical bar: bell across, fading at both ends, a little streaky noise along it.</summary>
	private static Texture2D ShaftTexture()
	{
		const int w = 32, h = 128;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var rng = new RandomNumberGenerator { Seed = 91 };
		float[] streak = new float[w];
		for (int x = 0; x < w; x++) streak[x] = 0.75f + 0.25f * rng.Randf();
		for (int y = 0; y < h; y++)
		{
			float v = (y + 0.5f) / h;
			float ends = Mathf.SmoothStep(0f, 0.18f, v) * Mathf.SmoothStep(1f, 0.7f, v);
			for (int x = 0; x < w; x++)
			{
				float u = (x + 0.5f) / w;
				float bell = Mathf.Pow(Mathf.Sin(u * Mathf.Pi), 1.6f);
				float a = bell * ends * streak[x];
				img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
			}
		}
		return ImageTexture.CreateFromImage(img);
	}

	private void Roll(int i)
	{
		_len[i] = _rng.RandfRange(Length.X, Length.Y);
		_wid[i] = _rng.RandfRange(Width.X, Width.Y);
		_phase[i] = _rng.RandfRange(0f, Mathf.Tau);
		_gain[i] = _rng.RandfRange(0.45f, 1f);
	}

	private Vector3 Ground(Vector3 p)
	{
		if (!_terrainSearched) { _terrainSearched = true; _terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain; }
		if (_terrain != null && IsInstanceValid(_terrain)) p.Y = _terrain.HeightAt(p.X, p.Z);
		return p;
	}

	public override void _Process(double delta)
	{
		var cam = GetViewport().GetCamera3D();
		if (cam == null || _mm == null) return;
		_t += (float)delta;
		Vector3 eye = cam.GlobalPosition;
		Vector3 fwd = -cam.GlobalBasis.Z;
		Vector3 up = -LightDir.Normalized().Lerp(Vector3.Down, Steepen).Normalized();   // from the ground up toward the (steepened) sun
		float strength = Mathf.Clamp(Strength, 0f, 1f);
		int visible = 0;
		for (int i = 0; i < Count; i++)
		{
			// Anchors: re-roll any that are missing or have fallen too far behind, ahead of the player.
			Vector3 a = _anchor[i];
			bool dead = float.IsNaN(a.X) || new Vector2(a.X - eye.X, a.Z - eye.Z).Length() > Radius * 1.3f;
			if (dead)
			{
				float ang = Mathf.Atan2(fwd.X, fwd.Z) + Mathf.Pi + _rng.RandfRange(-2.2f, 2.2f);   // mostly in front
				float r = _rng.RandfRange(6f, Radius);
				a = _anchor[i] = Ground(new Vector3(eye.X + Mathf.Sin(ang) * r, 0, eye.Z + Mathf.Cos(ang) * r));
				Roll(i);
			}
			Vector3 centre = a + up * (_len[i] * 0.5f);
			// Face the camera about the shaft's axis.
			Vector3 toCam = eye - centre;
			Vector3 side = up.Cross(toCam);
			if (side.LengthSquared() < 1e-4f) side = Vector3.Right;
			side = side.Normalized();
			Vector3 normal = side.Cross(up).Normalized();
			var basis = new Basis(side * _wid[i], up * _len[i], normal);
			_mm.SetInstanceTransform(i, new Transform3D(basis, centre));

			float dist = toCam.Length();
			float far = 1f - Mathf.SmoothStep(Radius * 0.55f, Radius, dist);
			float near = Mathf.SmoothStep(3f, 9f, dist);
			float along = Mathf.Abs(fwd.Dot(up));
			float endOn = 1f - Mathf.SmoothStep(0.55f, 0.9f, along);
			float drift = 0.72f + 0.28f * Mathf.Sin(_t * 0.23f + _phase[i]) * Mathf.Sin(_t * 0.071f + _phase[i] * 1.7f);
			float alpha = Intensity * strength * _gain[i] * far * near * endOn * drift;
			if (alpha > 0.004f) visible++;
			_mm.SetInstanceColor(i, new Color(Tint.R, Tint.G, Tint.B, alpha));
		}
		VisibleCount = visible;
		_mmi.Visible = strength > 0.001f;
	}
}
