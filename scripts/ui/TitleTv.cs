using System;
using Godot;
using ProjectDS.World;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.UI;

/// <summary>
/// The title screen: one old wood-grain TV on a concrete floor in the dark,
/// playing a worn tape of the stairs in the fog. The VCR's on-screen text
/// (PLAY, the channel, a date and a running clock, and the title) is drawn over
/// the picture in a small 2D viewport; the tube itself (title_crt.gdshader)
/// makes it an old tape: washed out, jittering, a tracking band crawling down,
/// and now and then the picture tearing into snow, when the title flickers to
/// the voice's line for a moment. The screen's wavering glow is the only light
/// in the room.
///
/// Rendered in its own world into a 640x360 viewport; <see cref="Texture"/> is
/// what the menu's backdrop finishes (grade, grain, dither). No game world is
/// loaded.
/// </summary>
public partial class TitleTv : Control
{
	private const string StairsPath = "res://assets/ui/title_stairs.png";
	private const string ShaderPath = "res://assets/ui/title_crt.gdshader";

	public Texture2D Texture => _world?.GetTexture();

	private SubViewport _world, _screen;
	private ShaderMaterial _tube;
	private OmniLight3D _glow;
	private Label _clock, _title, _play;
	private readonly RandomNumberGenerator _rng = new();
	private float _nextBurst, _burstLeft, _burstLength;
	private bool _dumped;
	private double _tapeSeconds = 3 * 3600 + 14 * 60 + 7;   // the tape's clock: 3:14:07 AM and counting

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Visible = false;   // only hosts the viewports; the backdrop shows the result
		_rng.Randomize();
		_nextBurst = _rng.RandfRange(6f, 10f);
		BuildScreen();
		BuildWorld();
	}

	// ---------------------------------------------------------------- what the tape shows

	private void BuildScreen()
	{
		// Same shape as the tube's picture area (the TV's aperture is ~1.13:1).
		_screen = new SubViewport { Size = new Vector2I(340, 300), Disable3D = true, TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
		AddChild(_screen);
		var bg = new ColorRect { Color = Colors.Black };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		_screen.AddChild(bg);
		if (ResourceLoader.Exists(StairsPath))
		{
			var pic = new TextureRect
			{
				Texture = GD.Load<Texture2D>(StairsPath),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			};
			pic.SetAnchorsPreset(LayoutPreset.FullRect);
			_screen.AddChild(pic);
		}

		_play = Osd("PLAY ▶", 20, new Vector2(16, 12));
		Osd("CH 03", 20, new Vector2(262, 12));
		Osd("SEP. 21 1998", 18, new Vector2(16, 264));
		_clock = Osd("", 18, new Vector2(212, 264));
		_title = Osd("DEAD SILENT", 34, new Vector2(0, 200));
		_title.HorizontalAlignment = HorizontalAlignment.Center;
		_title.Size = new Vector2(340, 40);
		UpdateClock();
	}

	private Label Osd(string text, int size, Vector2 pos)
	{
		var l = new Label { Text = text, Position = pos };
		l.AddThemeFontOverride("font", UiKit.Mono);
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", new Color(0.93f, 0.95f, 0.97f));
		l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
		l.AddThemeConstantOverride("shadow_offset_x", 2);
		l.AddThemeConstantOverride("shadow_offset_y", 2);
		_screen.AddChild(l);
		return l;
	}

	private void UpdateClock()
	{
		var ts = TimeSpan.FromSeconds(_tapeSeconds % 86400);
		_clock.Text = $"AM {(ts.Hours % 12 == 0 ? 12 : ts.Hours % 12)}:{ts.Minutes:00}:{ts.Seconds:00}";
	}

	// ---------------------------------------------------------------- the room

	private void BuildWorld()
	{
		_world = new SubViewport
		{
			Size = new Vector2I(640, 360), OwnWorld3D = true, Msaa3D = Viewport.Msaa.Disabled,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		AddChild(_world);
		var root = new Node3D { Name = "TitleRoom" };
		_world.AddChild(root);

		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.004f, 0.005f, 0.007f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.1f, 0.11f, 0.14f),
			AmbientLightEnergy = 0.12f,
			FogEnabled = true,
			FogLightColor = new Color(0.02f, 0.022f, 0.028f),
			FogDensity = 0.12f,
		};
		root.AddChild(new WorldEnvironment { Environment = env });

		// The set: a big tube TV with its knobs on the right, wood-grain body.
		var rng = new RandomNumberGenerator { Seed = 1998 };
		var spec = BunkerKit.RandomCrt(rng, 2);
		spec.KnobsRight = true;
		spec.Body = new Color(0.2f, 0.13f, 0.08f);   // dark wood-grain
		_tube = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
		_tube.SetShaderParameter("picture", _screen.GetTexture());
		var tv = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(-26f)), Vector3.Zero);
		Vector3 tube;
		bool modelled = BuildModelledTv(root, tv, spec, out tube);
		if (!modelled)
		{
			var body = new MeshKit();
			var screen = new MeshKit();
			screen.Mat(_tube);
			tube = BunkerKit.Crt(body, screen, tv, spec, Colors.White);
			body.CommitTo(root, "TvBody");
			screen.CommitTo(root, "TvScreen", false);
		}

		// Floor and the wall behind, bare concrete; a couple of cables snaking off into the dark.
		var room = new MeshKit();
		room.Mat(BunkerTextures.RoomFloorMat);
		room.Color = new Color(0.7f, 0.7f, 0.72f);
		room.Box(new Vector3(0, -0.05f, 0), new Vector3(12f, 0.1f, 12f));
		room.Mat(BunkerTextures.RoomWallMat);
		room.Box(new Vector3(0, 2f, -1.1f), new Vector3(12f, 4f, 0.2f));
		if (!modelled)
		{
			room.Mat(BunkerTextures.RubberMat);
			room.Color = Colors.White;
			BunkerKit.Tube(room, new[] { new Vector3(0.08f, 0.17f, -0.36f), new Vector3(0.3f, 0.01f, -0.6f), new Vector3(1.1f, 0.01f, -0.4f), new Vector3(2.2f, 0.01f, 0.3f) }, 0.012f, 0.012f, 4);
			BunkerKit.Tube(room, new[] { new Vector3(-0.1f, 0.15f, -0.36f), new Vector3(-0.5f, 0.01f, -0.7f), new Vector3(-1.6f, 0.01f, -0.5f), new Vector3(-2.6f, 0.01f, 0.4f) }, 0.01f, 0.01f, 4);
		}
		room.CommitTo(root, "Room");

		// The only light: the tube's glow, spilling onto the floor in front and up the bezel.
		var face = tv.Basis * Vector3.Back;
		_glow = new OmniLight3D
		{
			LightColor = new Color(0.72f, 0.78f, 0.9f), LightEnergy = 0.8f, OmniRange = 3.4f, OmniAttenuation = 1.6f,
			ShadowEnabled = true,
		};
		root.AddChild(_glow);
		_glow.Position = tube + face * 0.7f + Vector3.Down * 0.15f;   // out in front, so it pools on the floor rather than bleaching the bezel

		// Low and a little to the left, so the set sits right of centre and the menu has the dark on the left.
		var cam = new Camera3D { Fov = 40f, Current = true };
		root.AddChild(cam);
		cam.Position = new Vector3(-0.46f, 0.43f, 1.13f);
		cam.LookAt(new Vector3(-0.19f, 0.33f, 0f), Vector3.Up);
	}

	/// <summary>The set modelled in Blender (tools/Blender/title_tv.py → assets/models/title/title_tv.glb, 2026-10-15: the
	/// owner's rule, every model made in Blender): a wood-grain tube TV, knobs on the right, rabbit ears, its cable, and the two
	/// cables across the floor. Built at 0.72 x 0.6 x 0.6 m and scaled to <paramref name="spec"/>; the tube ("TvScreen") gets
	/// the tape's shader, the rest the game's own materials by name. False when the file is missing (the code-built set
	/// stands in).</summary>
	private bool BuildModelledTv(Node3D root, Transform3D tv, BunkerKit.CrtSpec spec, out Vector3 tube)
	{
		tube = tv * new Vector3(-spec.W * 0.11f, spec.H * 0.53f, spec.D * 0.5f + 0.002f);
		const string path = "res://assets/models/title/title_tv.glb";
		if (!ResourceLoader.Exists(path) || GD.Load<PackedScene>(path)?.Instantiate<Node3D>() is not { } model) return false;
		root.AddChild(model);
		if (model.FindChild("TV", true, false) is Node3D set)
			set.Transform = tv * new Transform3D(Basis.FromScale(new Vector3(spec.W / 0.72f, spec.H / 0.6f, spec.D / 0.6f)), Vector3.Zero);
		var cabinet = (StandardMaterial3D)BunkerTextures.PlasticMat.Duplicate();
		cabinet.AlbedoColor = spec.Body;
		Material Pick(string name) => name switch
		{
			"screen" => _tube,
			"cabinet" => cabinet,
			"rubber" => BunkerTextures.RubberMat,
			"metal" => ProcTextures.MetalMat,
			"grille" => ProcTextures.Flat("tv_grille", new Color(0.05f, 0.05f, 0.05f), 0.8f, 0.1f),
			"knob" => ProcTextures.Flat("tv_knob", new Color(0.08f, 0.08f, 0.08f), 0.45f, 0.4f),
			_ => ProcTextures.Flat("tv_trim", new Color(0.06f, 0.055f, 0.05f), 0.5f, 0.3f),
		};
		foreach (var n in model.FindChildren("*", nameof(MeshInstance3D), true, false))
			if (n is MeshInstance3D mi)
				for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
					mi.SetSurfaceOverrideMaterial(i, Pick(mi.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? ""));
		return true;
	}

	// ---------------------------------------------------------------- the tape playing

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		float t = Time.GetTicksMsec() / 1000f;
		if (!_dumped && t > 3f && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--dump-title") >= 0)
		{
			_dumped = true;
			_screen.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://test-output/title_screen_tape.png"));
			_world.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://test-output/title_screen_room.png"));
			GetTree().Quit();
		}

		double before = Math.Floor(_tapeSeconds);
		_tapeSeconds += delta;
		if (Math.Floor(_tapeSeconds) != before) UpdateClock();
		_play.Visible = (t % 2f) < 1.4f;   // the PLAY mark blinks, like a VCR's

		// Now and then the tape loses it for a moment, and the title isn't the title.
		float burst = 0f;
		if (_burstLeft > 0f)
		{
			_burstLeft -= dt;
			float k = 1f - Mathf.Abs(_burstLeft / _burstLength * 2f - 1f);   // up, then back down
			burst = Mathf.Clamp(k * 1.6f, 0f, 1f);
			if (_burstLeft <= 0f) _title.Text = "DEAD SILENT";
		}
		else if ((_nextBurst -= dt) <= 0f)
		{
			_burstLength = _burstLeft = _rng.RandfRange(0.35f, 0.9f);
			_nextBurst = _rng.RandfRange(9f, 20f);
			if (_rng.Randf() < 0.45f) _title.Text = "COME UP AND SEE";
		}
		_tube.SetShaderParameter("burst", burst);

		// The tube's glow wavers; the room light follows it (and jumps with the snow).
		float glow = 1f + 0.06f * Mathf.Sin(t * 2.3f) + 0.04f * Mathf.Sin(t * 7.1f + 1.3f) + (_rng.Randf() < 0.01f ? -0.25f : 0f);
		_tube.SetShaderParameter("glow", glow);
		_glow.LightEnergy = 0.8f * glow + burst * 0.5f;
	}
}
