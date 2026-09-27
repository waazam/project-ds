using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// The teaser (about 90 seconds; the owner: the first 30-second cut looked fast-forwarded, and it was too dark) (`-- --trailer`, recorded with Godot's movie writer: see docs/trailer.md). The
/// owner asked for the Evil Dead trailer's camera: "the force", a low, fast, gliding point of view
/// rushing through the dark woods at the cabin. Here those rushes are smooth (the owner gets headaches from
/// shaky or rocking cameras) with only a slight sway, and there's no strobing anywhere: cuts, and dips to
/// black.
///
/// It reveals little: the woods at night and the cabin, something standing in the trees (only a shape
/// and two eyes), the cabin, the bunker's hallway, the lake at dawn, the stairwell's drop,
/// the long hallway, the church's candlelit nave and its red circle, and the great door, then black, a
/// slam, and the title: DEAD SILENT.
///
/// The player is parked and hidden; this node flies its own camera (with a lantern's light for the dark
/// places), sets the atmosphere for each shot itself (after everything else, every frame), plays the
/// score, and quits when the title has faded.
/// </summary>
public partial class TrailerDirector : Node
{
	/// <summary>The trailer is two recordings joined (docs/trailer.md): the Hollow's shots (0-84 s), and the
	/// ending in Act 1's trail level (84 s on): the first staircase peeking out of the fog, then the title.</summary>
	public const float MainSeconds = 84f, EndingSeconds = 17.5f, EndingTitleAt = 12.5f, EndingTitleEnd = 17f, EndingPreroll = 4f;
	private bool _ending, _prerolled;
	private float Seconds => _ending ? EndingSeconds : MainSeconds;
	private float TitleAt => _ending ? EndingTitleAt : 999f;
	private float TitleEnd => _ending ? EndingTitleEnd : 999f;

	private Camera3D _cam;
	private OmniLight3D _lanternGlow;
	private SpotLight3D _lanternBeam;
	private ColorRect _black;
	private Label _title, _tag;
	private CanvasLayer _layer;
	private ForestAtmosphere _atmo;
	private float _t, _baseExposure = 1f;
	private int _shot = -1;
	private readonly List<Shot> _shots = new();
	private StalkerBody _figure;

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
		public float Roll;
		/// <summary>How far the shot opens up the exposure (the owner: the first cut was too dark).</summary>
		public float Exposure = 1f;
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
		_atmo = StoryBeat.Atmosphere(this);
		_baseExposure = _atmo?.Env?.TonemapExposure ?? 1f;
		// the player: parked where it is, hidden, frozen, deaf and blind
		if (StoryBeat.Player(this) is { } player)
		{
			Cutscene.Lock(player, input: true);
			player.Visible = false;
			player.ProcessMode = ProcessModeEnum.Disabled;
		}
		// every HUD layer off (the PS2 post effect stays: it's the look)
		foreach (var n in GetTree().Root.FindChildren("*", "CanvasLayer", true, false))
			if (n is CanvasLayer c && !c.IsInGroup("post_screen") && c.GetParent()?.IsInGroup("post_screen") != true) c.Visible = false;
		if (SaveIndicator.Instance != null) SaveIndicator.Instance.Visible = false;
		_cam = new Camera3D { Name = "TrailerCam", Fov = 62f, Near = 0.05f, Far = 400f };
		GetTree().CurrentScene.AddChild(_cam);
		_cam.MakeCurrent();
		var ear = new AudioListener3D();
		_cam.AddChild(ear);
		ear.MakeCurrent();
		_lanternGlow = new OmniLight3D { LightColor = new Color(1f, 0.72f, 0.42f), LightEnergy = 1.4f, OmniRange = 9f, Position = new Vector3(0.25f, -0.3f, -0.2f), ShadowEnabled = true };
		_lanternBeam = new SpotLight3D { LightColor = new Color(1f, 0.78f, 0.5f), LightEnergy = 2.2f, SpotRange = 22f, SpotAngle = 32f, ShadowEnabled = true };
		_cam.AddChild(_lanternGlow);
		_cam.AddChild(_lanternBeam);
		// the black and the title card
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
		_tag = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Modulate = new Color(1, 1, 1, 0) };
		_tag.AddThemeFontOverride("font", UiKit.SerifItalic);
		_tag.AddThemeFontSizeOverride("font_size", 13);
		_tag.AddThemeColorOverride("font_color", new Color(UiKit.Bone, 0.8f));
		_tag.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_tag.OffsetTop = 58f;
		_layer.AddChild(_tag);
		// the score
		string score = "res://assets/audio/music/trailer_score.wav";
		if (ResourceLoader.Exists(score))
		{
			var p = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(score), Bus = "Master", VolumeDb = -1f };
			AddChild(p);
			p.Play(_ending ? MainSeconds - EndingPreroll : 0f);   // the ending picks the score up where the first recording stops (its pre-roll is trimmed)
		}
	}

	// ------------------------------------------------------------------ the shots

	private void BuildShots()
	{
		var terrain = GroundSnap.FindTerrain(this);
		float Ground(Vector3 p) => terrain != null ? terrain.HeightAt(p.X, p.Z) : p.Y;
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		var st = StationInterior.Instance;
		var church = st?.Boss?.Library?.Round?.Stair?.Church;
		var longStair = st?.Boss?.Library?.Round?.Stair;
		var lake = GetTree().GetFirstNodeInGroup("lake_marker") as Lake;
		var bunker = BunkerInterior.Instance;
		var well = st?.Room3?.Stairs;
		var hall = well?.Hallway;

		// the force: a low glide over the ground at the cabin, weaving gently, clear of the trees. Slow enough
		// to read (the owner: the first cut looked fast-forwarded): about four metres a second.
		Vector3 door = cabin?.DoorCenter ?? Vector3.Zero;
		Vector3 away = ClearestWay(door, 70f);
		Func<float, (Vector3, Vector3)> Rush(Vector3 from, Vector3 to, float h, float weave)
			=> u =>
			{
				float e = u * u * (3f - 2f * u) * 0.3f + u * 0.7f;
				Vector3 p = from.Lerp(to, e);
				Vector3 side = (to - from).Cross(Vector3.Up).Normalized();
				p += side * Mathf.Sin(u * 4.5f) * weave;
				p.Y = Ground(p) + h + 0.05f * Mathf.Sin(u * 11f);
				Vector3 ahead = from.Lerp(to, Mathf.Min(1f, e + 0.1f));
				ahead.Y = Ground(ahead) + h * 1.15f;
				return (p, ahead);
			};
		Add("woods_rush", 3f, 10f, s => { s.Mood = ForestAtmosphere.Mood.Menacing; s.Exposure = 2.1f; }, Rush(door + away * 50f, door + away * 9f, 0.6f, 0.9f));

		// the cabin, a light in its window, and nobody home
		Add("cabin", 13f, 6f, s => { s.Mood = ForestAtmosphere.Mood.Night; s.Exposure = 2.3f; }, u =>
		{
			Vector3 cam = door + away * Mathf.Lerp(15f, 12f, u) + away.Cross(Vector3.Up) * 1.5f;
			cam.Y = Ground(cam) + 1.5f;
			return (cam, door + Vector3.Up * 0.4f);
		});

		// a glimpse: the camera drifts along the treeline; for a moment, between two trunks, something tall is
		// standing there, and the camera keeps drifting as if it didn't see
		Vector3 treeSpot = door + away.Rotated(Vector3.Up, 0.9f) * 24f;
		Vector3 toTree = (treeSpot - door).Normalized();
		Vector3 across = toTree.Cross(Vector3.Up).Normalized();
		Add("glimpse", 19f, 3f, s =>
		{
			s.Mood = ForestAtmosphere.Mood.Night; s.Exposure = 2.3f;
			_figure = new StalkerBody { Name = "TrailerFigure", Size = 1.15f };
			GetTree().CurrentScene.AddChild(_figure);
			_figure.GlobalPosition = treeSpot with { Y = Ground(treeSpot) };
			_figure.LookAt(door with { Y = _figure.GlobalPosition.Y }, Vector3.Up);
			_figure.Rotate(Vector3.Up, Mathf.Pi);
			_figure.GlowEyes(new Color(0.9f, 0.85f, 0.7f), 2f);
			var rim = new OmniLight3D { LightColor = new Color(0.55f, 0.62f, 0.8f), LightEnergy = 2.2f, OmniRange = 6f, ShadowEnabled = false };
			GetTree().CurrentScene.AddChild(rim);
			rim.GlobalPosition = _figure.GlobalPosition + toTree * 2.2f + Vector3.Up * 2.2f;
		}, u =>
		{
			Vector3 cam = treeSpot - toTree * 12f + across * Mathf.Lerp(-3.5f, 3.5f, u);
			cam.Y = Ground(cam) + 1.6f;
			Vector3 look = treeSpot + across * Mathf.Lerp(-4.5f, 5.5f, u);
			look.Y = Ground(treeSpot) + 1.8f;
			return (cam, look);
		});

		// the lake at dawn: skimming the water out into the fog, slowly
		if (lake != null)
		{
			Vector3 a = lake.NearDockWorld, b = lake.FarDockWorld;
			Vector3 dir = ((b - a) with { Y = 0 }).Normalized();
			Add("lake", 22f, 8f, s => { s.Mood = ForestAtmosphere.Mood.Dawn; s.Exposure = 1.1f; }, u =>
			{
				Vector3 p = a + dir * Mathf.Lerp(8f, 26f, u);
				p.Y = a.Y + 0.9f + 0.04f * Mathf.Sin(u * 5f);
				return (p, p + dir * 20f + Vector3.Down * 0.6f);
			});
		}

		// the bunker's hallway, walked slowly, lantern in hand
		if (bunker != null)
			Add("bunker", 30f, 7f, s => { s.Underground = 1f; s.Lantern = true; s.Exposure = 1.3f; }, u =>
				(bunker.ToGlobal(new Vector3(0.15f * Mathf.Sin(u * 2f), 1.55f, Mathf.Lerp(-10f, -19f, u))), bunker.ToGlobal(new Vector3(0, 1.4f, -60f))));

		// straight down the stairwell, sinking, turning slowly
		if (well != null)
			Add("stairwell", 37f, 7f, s => { s.Underground = 1f; s.Lantern = true; s.Exposure = 1.5f; }, u =>
			{
				Vector3 p = well.ToGlobal(new Vector3(0, Mathf.Lerp(-1f, -6f, u), 0));
				float a = u * 0.5f;
				return (p, p + Vector3.Down * 10f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.6f);
			});

		// the long hallway, crept along, the far end lost in the dark
		if (hall != null)
			Add("hallway", 44f, 7f, s => { s.Underground = 1f; s.Lantern = true; s.Exposure = 1.3f; }, u =>
				(hall.ToGlobal(new Vector3(0, 1.6f, Mathf.Lerp(40f, 48f, u))), hall.ToGlobal(new Vector3(0, 1.5f, 200f))));

		// the crypt: under the brick vaults with a lantern, and at the far end, for a breath, two eyes
		if (church != null)
		{
			Add("crypt", 51f, 7f, s =>
			{
				s.Underground = 0.6f; s.Interior = 1f; s.Lantern = true; s.Exposure = 1.6f;
				for (Node n = church; n != null; n = n.GetParent()) if (n is Node3D n3) n3.Visible = true;
				var eyes = new StalkerBody { Name = "TrailerEyes", Size = 1.05f };
				church.AddChild(eyes);
				eyes.Position = new Vector3(-6f, Church.CryptFloor, Church.CryptZ1 - 1.2f);
				eyes.LookAt(church.ToGlobal(new Vector3(-2f, Church.CryptFloor, 44f)) with { Y = eyes.GlobalPosition.Y }, Vector3.Up);
				eyes.Rotate(Vector3.Up, Mathf.Pi);
				eyes.GlowEyes(new Color(1f, 0.2f, 0.08f), 3f);
				_cryptEyes = eyes;
			}, u =>
			{
				// the eyes are only there for the last second of it
				if (_cryptEyes != null) _cryptEyes.Visible = u > 0.78f;
				return (church.ToGlobal(new Vector3(-2f, Church.CryptFloor + 1.6f, Mathf.Lerp(43f, 52f, u))), church.ToGlobal(new Vector3(-4.5f, Church.CryptFloor + 1.4f, Church.CryptZ1)));
			});
		}

		// the force again, at night, from another side
		Vector3 away2 = ClearestWay(door, 60f, exclude: away);
		Add("woods_rush2", 58f, 8f, s => { s.Mood = ForestAtmosphere.Mood.Night; s.Exposure = 2.6f; }, Rush(door + away2 * 45f, door + away2 * 12f, 0.45f, 0.7f));

		// the church: down the candlelit nave toward the red circle round the altar
		if (church != null)
			Add("nave", 66f, 8f, s => { s.Interior = 1f; s.Winter = 1f; s.Mood = ForestAtmosphere.Mood.Night; s.Exposure = 1.5f; }, u =>
				(church.ToGlobal(new Vector3(0.25f * Mathf.Sin(u * 1.5f), 1.7f, Mathf.Lerp(12f, 34f, u))), church.ToGlobal(new Vector3(0, 2.5f, Church.ChancelEnd))));

		// the long stair: looking up it into the dark
		if (longStair != null)
			Add("long_stair", 74f, 4f, s => { s.Underground = 1f; s.Lantern = true; s.Exposure = 1.5f; }, u =>
			{
				float pr = Mathf.Lerp(0.05f, 0.058f, u);
				var p = longStair.PointAt(pr) + Vector3.Up * 1.5f;
				return (p, longStair.PointAt(pr + 0.04f) + Vector3.Up * 1.4f);
			});

		// and at the great door, then the slam
		if (church != null)
			Add("the_door", 78f, 4f, s => { s.Interior = 1f; s.Winter = 1f; s.Exposure = 1.5f; }, u =>
			{
				float e = u * u * (3f - 2f * u);
				return (church.ToGlobal(new Vector3(0, 1.6f, Mathf.Lerp(16f, 1.4f, e))), church.ToGlobal(new Vector3(0, 1.8f, -1f)));
			});
	}

	private StalkerBody _cryptEyes;

	private FirstClimbEvent FindClimb()
	{
		foreach (var n in GetTree().CurrentScene?.FindChildren("*", "", true, false) ?? new Godot.Collections.Array<Node>())
			if (n is FirstClimbEvent f) return f;
		return null;
	}

	/// <summary>The last shot (the owner): a slow walk up the trail's end, and out of the fog, just showing, the
	/// first staircase. Then the title. The Act 1 fog closes round the camera the way it closes round the player.</summary>
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
		// (it starts after a pre-roll, trimmed from the recording, with the camera already standing at its start,
		// so the Act 1 fog has closed right in before anything is seen)
		Add("first_stairs", EndingPreroll + 1.0f, 7.0f, s => { s.Mood = ForestAtmosphere.Mood.Auto; s.Exposure = 0.78f; }, u =>
		{
			float e = u * 0.8f + u * u * (3f - 2f * u) * 0.2f;
			Vector3 cam = foot + dir * Mathf.Lerp(16f, 5.5f, e) + dir.Cross(Vector3.Up) * 0.6f * Mathf.Sin(u * 2.5f);
			cam.Y = Ground(cam) + 1.62f + 0.03f * Mathf.Sin(u * 14f);   // a walker's slow step, barely there
			return (cam, foot + Vector3.Up * 2.2f);
		});
	}

	private void Add(string name, float start, float length, Action<Shot> setup, Func<float, (Vector3, Vector3)> path)
		=> _shots.Add(new Shot { Name = name, Start = start, Length = length, Setup = setup, Path = path });

	/// <summary>The direction out from <paramref name="from"/> with the longest clear run at head height, so a
	/// low rush doesn't fly through a trunk.</summary>
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
		// which shot
		int idx = -1;
		for (int i = 0; i < _shots.Count; i++) if (_t >= _shots[i].Start && _t < _shots[i].Start + _shots[i].Length) idx = i;
		if (idx != _shot)
		{
			_shot = idx;
			if (idx >= 0) { _shots[idx].Setup?.Invoke(_shots[idx]); if (_atmo != null) _atmo.SetMood(_shots[idx].Mood, 0.01f); }
		}
		float black = 1f;
		// the ending's pre-roll: stand at the first shot's start in the dark, so its fog is settled
		if (idx < 0 && _ending && _shots.Count > 0 && _t < _shots[0].Start)
		{
			var (pp, pl) = _shots[0].Path(0f);
			_cam.GlobalPosition = pp;
			_cam.LookAt(pl, Vector3.Up);
			if (_atmo != null)
			{
				// once (again every frame would restart it), with the shot's own settings (its mood is what lets the fog in)
				if (!_prerolled) { _prerolled = true; _shots[0].Setup?.Invoke(_shots[0]); _atmo.SetMood(_shots[0].Mood, 0.01f); }
				if (RainVfx.Instance != null) RainVfx.Instance.Intensity = 0f;   // (the rain drives the storm, which turns the Act 1 fog off)
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
			// a short dip at each end of every shot: cuts, never flashes
			float edge = Mathf.Min(_t - s.Start, s.Start + s.Length - _t);
			black = Mathf.Clamp(1f - edge / 0.12f, 0f, 1f);
			if (_atmo?.Env != null) _atmo.Env.TonemapExposure = _baseExposure * s.Exposure;
			if (_atmo != null)
			{
				_atmo.Underground = s.Underground;
				_atmo.Interior = s.Interior;
				_atmo.Winter = s.Winter;
			}
			RenderingServer.GlobalShaderParameterSet("winter", s.Winter);
			if (RainVfx.Instance != null) RainVfx.Instance.Intensity = 0f;
			if (_atmo != null) _atmo.Storm = 0f;
			WinterGlade.ForceSnow = s.Winter > 0.5f;
			_lanternGlow.Visible = s.Lantern;
			_lanternBeam.Visible = s.Lantern;
		}
		_black.Color = new Color(0, 0, 0, black);
		// the title: after the slam and a beat of black
		float ta = Mathf.Clamp((_t - TitleAt) / 1.6f, 0f, 1f) * Mathf.Clamp((TitleEnd - _t) / 1.4f, 0f, 1f);
		_title.Modulate = new Color(1, 1, 1, ta);
		_tag.Modulate = new Color(1, 1, 1, 0);
		if (_t >= Seconds)
		{
			SetProcess(false);
			GD.Print("[trailer] done");
			GetTree().Quit();
		}
	}
}
