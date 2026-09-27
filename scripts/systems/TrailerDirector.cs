using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// The Dead Silent teaser (docs/trailer.md), about 48 s, cut to the owner's own song, "Petty Theft": 17 s from
/// its 1:40 (eight shots, a cut on every third beat), then the song jumps to 3:28 for the first staircase
/// coming out of the fog, and the title lands as the song stops at 3:56. It is filmed in-engine at full 1080p, recorded with Godot's movie writer, and joined from two
/// recordings: the Hollow's shots (`-- --trailer`) and the ending in Act 1's trail level
/// (`-- --trailer-ending`).
///
/// The owner's notes on the earlier cuts shaped it:
/// <list type="bullet">
/// <item>short (30-45 s) and smooth (60 fps), camera moves gentle but not slow;</item>
/// <item>no monsters shown (the horror should surprise people);</item>
/// <item>the bunker and the stairwell as the heart of it, each broken in two: the bunker's hallway, then the
/// same hallway gone red; the stairwell from the top, then its decayed depths;</item>
/// <item>a bird's-eye view down on the trail as the fog rolls in;</item>
/// <item>the lake's far trees green, not white;</item>
/// <item>the church's nave as a high, slow, surreal swoop toward the altar;</item>
/// <item>no church door, no long-stair climb, no clipping through trees.</item>
/// </list>
/// Smooth, gentle cameras (the owner gets headaches from shaky ones); no strobing.
///
/// The player is parked and hidden; this node flies its own camera (with a lantern's light for the dark
/// places), sets the atmosphere for every shot after everything else each frame, and quits at the end. The
/// music is laid under the joined video afterwards (the recording carries only the game's own sound).
/// </summary>
public partial class TrailerDirector : Node
{
	/// <summary>The cut (the owner: a 30-45 second teaser), on the song's grid (85 bpm, measured from the song):
	/// 16.9 s of eight shots, three beats each, from 1:40.24; then the song jumps to the big downbeat at 3:28.94
	/// on the cut to the stairs, and the title lands on the hit at 3:56.12. The song's own ending rings out under
	/// the title (docs/trailer.md).</summary>
	public const float Beat = 60f / 85f, StairsAt = 24f * Beat, TitleAtSong = StairsAt + (236.123f - 208.94f), TitleHold = 9f;
	/// <summary>The ending recording: a pre-roll (trimmed off) so Act 1's fog is settled, the stairs, the title.</summary>
	public const float EndingPreroll = 4f, EndingStairs = TitleAtSong - StairsAt;
	private bool _ending, _prerolled;
	private float Seconds => _ending ? EndingPreroll + EndingStairs + TitleHold : StairsAt;
	private float TitleAt => _ending ? EndingPreroll + EndingStairs : 9999f;
	private float TitleEnd => TitleAt + TitleHold - 1.5f;

	private Camera3D _cam;
	private OmniLight3D _lanternGlow;
	private SpotLight3D _lanternBeam;
	private ColorRect _black;
	private Label _title;
	private CanvasLayer _layer;
	private ForestAtmosphere _atmo;
	private float _t, _baseExposure = 1f;
	private int _shot = -1;
	private readonly List<Shot> _shots = new();

	private sealed class Shot
	{
		public string Name;
		public float Start, Length;
		public Action<Shot> Setup;
		/// <summary>The camera at u (0..1 through the shot): position and a point to look at.</summary>
		public Func<float, (Vector3 pos, Vector3 look)> Path;
		public ForestAtmosphere.Mood Mood = ForestAtmosphere.Mood.Night;
		public float Underground, Interior, Winter;
		public bool Lantern;
		/// <summary>How far the shot opens up the exposure.</summary>
		public float Exposure = 1f;
		/// <summary>Seconds of fade in from black at its start (a cut otherwise).</summary>
		public float FadeIn = 0.08f, FadeOut = 0.08f;
		/// <summary>Every frame, after the atmosphere: a shot's own touch on the environment at u.</summary>
		public Action<Godot.Environment, float> Tick;
	}

	public override void _Ready()
	{
		ProcessPriority = 1000;   // after the level's own atmosphere drivers, every frame
		ProcessMode = ProcessModeEnum.Always;
		_ = Run();
	}

	private async Task Run()
	{
		_ending = GameSettings.Instance?.TrailerEnding ?? false;
		for (int i = 0; i < 240; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (_ending ? FindClimb() != null : StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church?.FontKeyPickup != null && GetTree().GetFirstNodeInGroup("cabin") != null) break;
		}
		for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Setup();
		if (_ending) BuildEnding(); else BuildShots();
		GD.Print($"[trailer] {_shots.Count} shots, {Seconds} s; starts at frame {Engine.GetFramesDrawn()}");
		SetProcess(true);
	}

	private void Setup()
	{
		// full resolution (the owner: the first cuts were rendered at the game's 640x360 and looked low): the 3D
		// draws at the window's size, the interface still laid out for 640x360
		var root = GetTree().Root;
		root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
		root.ContentScaleSize = new Vector2I(640, 360);
		root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
		_atmo = StoryBeat.Atmosphere(this);
		_baseExposure = _atmo?.Env?.TonemapExposure ?? 1f;
		// the player: parked where it is, hidden, frozen
		if (StoryBeat.Player(this) is { } player)
		{
			Cutscene.Lock(player, input: true);
			player.Visible = false;
			player.ProcessMode = ProcessModeEnum.Disabled;
		}
		// every HUD layer off (the PS2 post effect stays: it's the look)
		foreach (var n in root.FindChildren("*", "CanvasLayer", true, false))
			if (n is CanvasLayer c && !c.IsInGroup("post_screen") && c.GetParent()?.IsInGroup("post_screen") != true) c.Visible = false;
		if (SaveIndicator.Instance != null) SaveIndicator.Instance.Visible = false;
		_cam = new Camera3D { Name = "TrailerCam", Fov = 60f, Near = 0.05f, Far = 600f };
		GetTree().CurrentScene.AddChild(_cam);
		_cam.MakeCurrent();
		var ear = new AudioListener3D();
		_cam.AddChild(ear);
		ear.MakeCurrent();
		_lanternGlow = new OmniLight3D { LightColor = new Color(1f, 0.72f, 0.42f), LightEnergy = 1.4f, OmniRange = 9f, Position = new Vector3(0.25f, -0.3f, -0.2f), ShadowEnabled = true };
		_lanternBeam = new SpotLight3D { LightColor = new Color(1f, 0.78f, 0.5f), LightEnergy = 2.2f, SpotRange = 22f, SpotAngle = 32f, ShadowEnabled = true };
		_cam.AddChild(_lanternGlow);
		_cam.AddChild(_lanternBeam);
		_layer = new CanvasLayer { Layer = 120, Name = "TrailerLayer" };
		GetTree().CurrentScene.AddChild(_layer);
		_black = new ColorRect { Color = new Color(0, 0, 0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };
		_black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_layer.AddChild(_black);
		_title = new Label { Text = "DEAD SILENT", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Modulate = new Color(1, 1, 1, 0) };
		_title.AddThemeFontOverride("font", UiKit.SerifTitle);
		_title.AddThemeFontSizeOverride("font_size", 40);
		_title.AddThemeColorOverride("font_color", UiKit.Bone);
		_title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_layer.AddChild(_title);
	}

	// ------------------------------------------------------------------ the shots

	private void BuildShots()
	{
		var terrain = GroundSnap.FindTerrain(this);
		float Ground(Vector3 p) => terrain != null ? terrain.HeightAt(p.X, p.Z) : p.Y;
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		var st = StationInterior.Instance;
		var church = st?.Boss?.Library?.Round?.Stair?.Church;
		var lake = GetTree().GetFirstNodeInGroup("lake_marker") as Lake;
		var bunker = BunkerInterior.Instance;
		var well = st?.Room3?.Stairs;
		Vector3 door = cabin?.DoorCenter ?? Vector3.Zero;
		Vector3 away = ClearestWay(door, 70f);
		// eight shots of three beats each (the song runs at 84.7 bpm: a beat is 0.708 s), every cut on a beat
		float T(int shot) => shot * Beat * 3f;
		float L = Beat * 3f;

		// the cabin at dusk, a light in its window
		Add("cabin", T(0), L, s => { s.Mood = ForestAtmosphere.Mood.Night; s.Exposure = 2.2f; s.FadeIn = 0.5f; }, u =>
		{
			Vector3 cam = door + away * Mathf.Lerp(15f, 12.5f, u) + away.Cross(Vector3.Up) * 1.5f;
			cam.Y = Ground(cam) + 1.5f;
			return (cam, door + Vector3.Up * 0.4f);
		});
		// from the treetops, looking down on the trail as the fog rolls in round the trunks
		if (terrain != null && terrain.TrailLength > 60f)
		{
			float s0 = terrain.TrailLength * 0.4f;
			Vector3 c = terrain.TrailPoint(s0, out Vector3 tan);
			tan = (tan with { Y = 0 }).Normalized();
			c.Y = Ground(c);
			Add("treetops", T(1), L, s =>
			{
				// the thin dawn air, so the woods read from above; the fog is the height fog, pooling up round the trunks
				s.Mood = ForestAtmosphere.Mood.Dawn; s.Exposure = 1.0f;
				s.Tick = (env, u) =>
				{
					env.FogDensity = 0.0025f;
					env.FogHeight = c.Y + 3.5f;
					env.FogHeightDensity = Mathf.Lerp(0.02f, 0.4f, u * u * (3f - 2f * u));
				};
			}, u =>
			{
				Vector3 p = terrain.TrailPoint(s0 + Mathf.Lerp(-8f, -2f, u), out _);
				p.Y = c.Y + 52f;
				Vector3 look = terrain.TrailPoint(s0 + Mathf.Lerp(6f, 10f, u), out _);
				look.Y = Ground(look);
				return (p, look);
			});
		}
		// the lake at dawn, skimming the water
		if (lake != null)
		{
			Vector3 a = lake.NearDockWorld, b = lake.FarDockWorld;
			Vector3 dir = ((b - a) with { Y = 0 }).Normalized();
			Add("lake", T(2), L, s => { s.Mood = ForestAtmosphere.Mood.Dawn; s.Exposure = 1.05f; s.Tick = (env, _) => env.FogDensity = 0.0022f; }, u =>
			{
				Vector3 p = a + dir * Mathf.Lerp(10f, 17f, u);
				p.Y = a.Y + 0.9f;
				return (p, p + dir * 20f + Vector3.Down * 0.6f);
			});
		}
		// the bunker's hallway, lantern in hand; then the same hallway gone red
		if (bunker != null)
		{
			Add("bunker", T(3), L, s => { s.Underground = 1f; s.Lantern = true; s.Exposure = 1.3f; }, u =>
				(bunker.ToGlobal(new Vector3(0, 1.55f, Mathf.Lerp(-9f, -12.5f, u))), bunker.ToGlobal(new Vector3(0, 1.4f, -60f))));
			Add("bunker_red", T(4), L, s => { s.Underground = 1f; s.Lantern = false; s.Exposure = 1.5f; bunker.Hallway?.SetRedInstant(); }, u =>
				(bunker.ToGlobal(new Vector3(0, 1.5f, Mathf.Lerp(-40f, -43.5f, u))), bunker.ToGlobal(new Vector3(0, 1.3f, -90f))));
		}
		// straight down the stairwell from the top; then deep down, where it has gone to rot
		if (well != null)
		{
			Add("stairwell", T(5), L, s => { s.Underground = 1f; s.Lantern = true; s.Exposure = 1.5f; }, u =>
			{
				Vector3 p = well.ToGlobal(new Vector3(0, Mathf.Lerp(-1f, -3f, u), 0));
				float a = u * 0.35f;
				return (p, p + Vector3.Down * 10f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.6f);
			});
			float bottom = Stairwell.BottomYFor(Stairwell.DefaultRevolutions);
			Add("stairwell_deep", T(6), L, s => { s.Underground = 1f; s.Lantern = true; s.Exposure = 1.6f; }, u =>
			{
				// among the last flights, looking down past them into the rotten drop
				Vector3 p = well.ToGlobal(new Vector3(0, Mathf.Lerp(bottom + Stairwell.FallDepth + 9f, bottom + Stairwell.FallDepth + 7.2f, u), 0));
				float a = 1.2f + u * 0.25f;
				return (p, p + Vector3.Down * 10f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.9f);
			});
		}
		// the church: a high, slow swoop down the candlelit nave toward the red circle round the altar
		if (church != null)
		{
			for (Node n = church; n != null; n = n.GetParent()) if (n is Node3D n3) n3.Visible = true;
			Add("nave", T(7), L, s => { s.Interior = 1f; s.Winter = 1f; s.Mood = ForestAtmosphere.Mood.Night; s.Exposure = 1.55f; s.FadeOut = 0.3f; }, u =>
			{
				Vector3 p = church.ToGlobal(new Vector3(0, Mathf.Lerp(11f, 9f, u), Mathf.Lerp(14f, 22f, u)));
				return (p, church.ToGlobal(new Vector3(0, 1.4f, Church.ChancelEnd - 0.4f)));
			});
		}
	}

	private void Add(string name, float start, float length, Action<Shot> setup, Func<float, (Vector3, Vector3)> path)
		=> _shots.Add(new Shot { Name = name, Start = start, Length = length, Setup = setup, Path = path });

	private FirstClimbEvent FindClimb()
	{
		foreach (var n in GetTree().CurrentScene?.FindChildren("*", "", true, false) ?? new Godot.Collections.Array<Node>())
			if (n is FirstClimbEvent f) return f;
		return null;
	}

	/// <summary>The last shot (the owner): from the song's 3:28, a slow walk up the trail's end, and out of the
	/// fog, just showing, the first staircase; the title as the song stops. Act 1's fog is held closed right in.</summary>
	private void BuildEnding()
	{
		var terrain = GroundSnap.FindTerrain(this);
		float Ground(Vector3 p) => terrain != null ? terrain.HeightAt(p.X, p.Z) : p.Y;
		var climb = FindClimb();
		Vector3 foot = climb?.GlobalPosition ?? Vector3.Zero;
		Vector3 pref = Vector3.Back;
		if (terrain != null)
		{
			var end = terrain.TrailPoint(terrain.TrailLength, out _);
			pref = ((end - foot) with { Y = 0 }).Normalized();
		}
		Vector3 dir = ClearestWay(foot, 26f, prefer: pref);
		Add("first_stairs", EndingPreroll, EndingStairs, s => { s.Mood = ForestAtmosphere.Mood.Auto; s.Exposure = 0.78f; s.FadeIn = 1.2f; s.FadeOut = 0.05f; }, u =>
		{
			float e = u * 0.75f + u * u * (3f - 2f * u) * 0.25f;
			Vector3 cam = foot + dir * Mathf.Lerp(24f, 4.5f, e) + dir.Cross(Vector3.Up) * 0.5f * Mathf.Sin(u * 2.2f);
			cam.Y = Ground(cam) + 1.62f + 0.025f * Mathf.Sin(u * 30f);   // a walker's slow step, barely there
			return (cam, foot + Vector3.Up * 2.2f);
		});
	}

	/// <summary>The direction out from <paramref name="from"/> with the longest clear run at head height.</summary>
	private Vector3 ClearestWay(Vector3 from, float reach, Vector3? exclude = null, Vector3? prefer = null)
	{
		var space = GetTree().CurrentScene is Node3D n ? n.GetWorld3D().DirectSpaceState : null;
		Vector3 best = Vector3.Back; float bestLen = -1f;
		for (int i = 0; i < 36; i++)
		{
			float a = Mathf.Tau * i / 36f;
			var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
			if (exclude is Vector3 ex && d.Dot(ex) > 0.3f) continue;
			if (prefer is Vector3 pf && d.Dot(pf) < 0.5f) continue;
			float len = reach;
			if (space != null)
				foreach (float h in new[] { 0.6f, 1.6f })
				{
					var q = PhysicsRayQueryParameters3D.Create(from + Vector3.Up * h + d * 3f, from + Vector3.Up * h + d * reach, 1u);
					var hit = space.IntersectRay(q);
					if (hit.Count > 0) len = Mathf.Min(len, ((Vector3)hit["position"] - from).Length());
				}
			if (len > bestLen) { bestLen = len; best = d; }
		}
		return best;
	}

	// ------------------------------------------------------------------ running it

	public override void _Process(double delta)
	{
		if (_cam == null) return;
		_t += (float)delta;
		int idx = -1;
		for (int i = 0; i < _shots.Count; i++) if (_t >= _shots[i].Start && _t < _shots[i].Start + _shots[i].Length) idx = i;
		if (idx != _shot)
		{
			_shot = idx;
			if (idx >= 0) { _shots[idx].Setup?.Invoke(_shots[idx]); if (_atmo != null) _atmo.SetMood(_shots[idx].Mood, 0.01f); }
		}
		float black = 1f;
		// the ending's pre-roll: stand at the shot's start in the dark, so Act 1's fog has closed in
		if (idx < 0 && _ending && _shots.Count > 0 && _t < _shots[0].Start)
		{
			var (pp, pl) = _shots[0].Path(0f);
			_cam.GlobalPosition = pp;
			_cam.LookAt(pl, Vector3.Up);
			if (_atmo != null)
			{
				// once (every frame would restart it), with the shot's own settings (its mood is what lets the fog in)
				if (!_prerolled) { _prerolled = true; _shots[0].Setup?.Invoke(_shots[0]); _atmo.SetMood(_shots[0].Mood, 0.01f); }
				if (RainVfx.Instance != null) RainVfx.Instance.Intensity = 0f;   // (the rain drives the storm, which turns the fog off)
				_atmo.Storm = 0f;
				_atmo.Underground = 0f;
				_atmo.Act1FogOverride = 1f;
			}
		}
		if (idx >= 0)
		{
			var s = _shots[idx];
			float u = Mathf.Clamp((_t - s.Start) / s.Length, 0f, 1f);
			var (pos, look) = s.Path(u);
			_cam.GlobalPosition = pos;
			if ((look - pos).LengthSquared() > 1e-4f) _cam.LookAt(look, Mathf.Abs((look - pos).Normalized().Dot(Vector3.Up)) > 0.98f ? Vector3.Forward : Vector3.Up);
			float fin = Mathf.Clamp((_t - s.Start) / Mathf.Max(s.FadeIn, 0.01f), 0f, 1f);
			float fout = Mathf.Clamp((s.Start + s.Length - _t) / Mathf.Max(s.FadeOut, 0.01f), 0f, 1f);
			black = 1f - Mathf.Min(fin, fout);
			if (_atmo?.Env != null) _atmo.Env.TonemapExposure = _baseExposure * s.Exposure;
			if (_atmo != null)
			{
				_atmo.Underground = s.Underground;
				_atmo.Interior = s.Interior;
				_atmo.Winter = s.Winter;
				_atmo.Storm = 0f;
			}
			RenderingServer.GlobalShaderParameterSet("winter", s.Winter);
			if (RainVfx.Instance != null) RainVfx.Instance.Intensity = 0f;
			WinterGlade.ForceSnow = s.Winter > 0.5f;
			_lanternGlow.Visible = s.Lantern;
			_lanternBeam.Visible = s.Lantern;
			if (_atmo?.Env is { } env) { env.FogHeightDensity = 0f; s.Tick?.Invoke(env, u); }
		}
		_black.Color = new Color(0, 0, 0, black);
		// the title, as the song stops
		float ta = Mathf.Clamp((_t - TitleAt) / 0.4f, 0f, 1f) * Mathf.Clamp((TitleEnd - _t) / 2.5f, 0f, 1f);   // in on the hit, out slowly as the song rings out
		_title.Modulate = new Color(1, 1, 1, ta);
		if (_t >= Seconds)
		{
			SetProcess(false);
			GD.Print("[trailer] done");
			GetTree().Quit();
		}
	}
}
