using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// What happens on the way down (Act 14). Nothing chases, nothing attacks: the stairwell is meant to
/// wear the player down. The numbers painted on the landings stop making sense. Footsteps go on
/// below them in step with their own, and carry on for a step or two after they stop. Something drops
/// past down the well and never lands. A figure stands at the rail far below, looking up, and isn't
/// there when they get down to it. A portrait with its face burnt out hangs on a landing, and later
/// an empty chair faces the wall. Somebody knocks on a door that opens onto solid concrete. For a
/// long stretch near the bottom the shaft goes silent. Each fires once, by depth reached.
/// </summary>
public partial class Stairwell
{
	private readonly List<(int rev, System.Action<PlayerController> fire)> _events = new();
	private int _nextEvent;
	private PlayerController _echoPlayer;
	private int _echoFrom = -1, _echoTo = -1, _echoAboveFrom = -1, _echoAboveTo = -1;
	private int _echoTail;
	private double _lastStep;
	private Node3D _figure;
	private int _figureRev;
	private Node3D _faller;
	private float _fallerV;

	private static Font _paint;
	private static Font Paint => _paint ??= new SystemFont
	{
		FontNames = new[] { "Stencil", "Impact", "Arial Black", "Arial" }, FontWeight = 800,
	};

	/// <summary>What is painted on turn r's first landing. It counts properly for a while.</summary>
	public static string SignText(int r) => r switch
	{
		< 9 => $"B{r + 1}",
		9 or 10 or 11 => "B9",
		>= 16 and <= 19 => $"B{19 - r}",
		20 => "B21",
		26 or 27 => "",
		31 => "B",
		>= 32 and <= 38 => new string('|', r - 31),
		39 => "B39",
		40 => "B39",
		41 => "B39",
		46 => "TURN BACK",
		48 => "B-1",
		>= 51 and <= 54 => "",
		56 => "DOWN",
		60 => "YOU WERE TOLD",
		62 => "NEVER",
		63 => "",
		_ => $"B{r + 1}",
	};

	private void BuildEvents()
	{
		// the numbers, stencilled on the wall of each turn's first landing, facing the stairs coming down
		for (int r = 0; r < Revolutions; r++)
		{
			string text = SignText(r);
			if (text.Length == 0) continue;
			int k = r * 4;
			Vector2 p = CornerXZ(k);
			bool scrawl = r >= 46;
			var l = new Label3D
			{
				Name = $"Sign{r}", Text = text, Font = Paint, FontSize = scrawl ? 64 : 128, PixelSize = 0.0035f,
				Modulate = scrawl ? new Color(0.45f, 0.06f, 0.05f, 0.9f) : new Color(0.85f, 0.83f, 0.76f, 0.85f),
				OutlineSize = 0, Shaded = true, DoubleSided = false, AlphaCut = Label3D.AlphaCutMode.Discard,
				Position = new Vector3(-H + 0.01f, CornerY(k) + 1.45f, p.Y), Rotation = new Vector3(0, Mathf.Pi * 0.5f, scrawl ? -0.06f : 0f),
				VisibilityRangeEnd = 30f,
			};
			AddChild(l);
		}

		// a steel door on turn 10's third landing, painted shut, in the outer wall
		AddDoor(10 * 4 + 2);
		// turn 20: a portrait on the landing wall, face burnt away like the ones upstairs
		AddPortrait(20 * 4 + 1, 3);
		// turn 42: a wooden chair on the landing, set to face the wall
		AddChair(42 * 4 + 3);

		_events.Add((5, p => { _echoFrom = 5; _echoTo = 9; _echoPlayer = p; }));
		_events.Add((10, p => { }));   // the door knocks by proximity (see ProcessEvents)
		_events.Add((15, p => DropSomething(p)));
		_events.Add((22, p => Whisper(p, 3f)));
		_events.Add((28, p => Sfx("stair_groan", 3, p.GlobalPosition + Vector3.Down * 3f, -2f, 8f)));
		_events.Add((30, p => { _echoAboveFrom = 30; _echoAboveTo = 32; _echoPlayer = p; }));
		_events.Add((33, p => AddFigure(p, 35)));
		_events.Add((38, p => Sfx("far_clang", 2, p.GlobalPosition + Vector3.Down * 40f, 4f, 30f)));
		_events.Add((44, p => _ = StoryBeat.Caption(this, "How far down does this go?", 0.8f, 2.6f, 1.2f)));
		_events.Add((50, p => Silence(40f)));
		_events.Add((55, p => Sfx("stair_groan", 3, p.GlobalPosition + Vector3.Up * 2f, 0f, 8f)));
		_events.Add((58, p => { _echoAboveFrom = 58; _echoAboveTo = 60; _echoPlayer = p; }));
		_events.Add((61, p => Whisper(p, -2f)));
		_events.Sort((a, b) => a.rev.CompareTo(b.rev));
	}

	private void ProcessEvents(PlayerController player, Vector3 local, float dt)
	{
		while (_nextEvent < _events.Count && DeepestRev >= _events[_nextEvent].rev)
		{
			var (rev, fire) = _events[_nextEvent++];
			fire(player);
			GD.Print($"[story] Act 14: turn {rev}");
		}
		if (!_subscribed && player.Footsteps != null)
		{
			_subscribed = true;
			_echoPlayer = player;
			player.Footsteps.Stepped += OnPlayerStepped;
		}
		// the echo carries on for a step or two after the player stops
		if (_echoTail > 0 && Time.GetTicksMsec() * 0.001 - _lastStep > 0.55)
		{
			_lastStep = Time.GetTicksMsec() * 0.001;
			_echoTail--;
			EchoStep(player);
		}
		// the knocking door
		if (_door != null && !_doorKnocked && player.GlobalPosition.DistanceTo(_door.GlobalPosition) < 3f)
		{
			_doorKnocked = true;
			for (int i = 0; i < 3; i++)
			{
				int n = i;
				GetTree().CreateTimer(0.6 + n * 0.5).Timeout += () => Sfx("wall_knock", 3, _door.GlobalPosition, -6f, 5f);
			}
		}
		// the figure: gone once the player is a turn above it and not looking, or right on it
		if (_figure != null && _figure.Visible)
		{
			float d = _figure.GlobalPosition.DistanceTo(player.GlobalPosition);
			bool looking = Seen(_figure.GlobalPosition + Vector3.Up * 1.2f);
			if ((d < 7f && !looking) || d < 3.2f) { _figure.Visible = false; if (d < 3.2f) Whisper(player, -8f); }
		}
		// something falling past
		if (_faller != null)
		{
			_fallerV = Mathf.Min(_fallerV + 9.8f * dt, 30f);
			_faller.Position += Vector3.Down * _fallerV * dt;
			_faller.RotateObjectLocal(new Vector3(0.6f, 0.3f, 0.7f).Normalized(), dt * 2.5f);
			if (_faller.GlobalPosition.Y < player.GlobalPosition.Y - 60f) { _faller.QueueFree(); _faller = null; }
		}
	}

	private bool _subscribed;
	private Node3D _door;
	private bool _doorKnocked;

	private void OnPlayerStepped()
	{
		if (_echoPlayer == null || !GodotObject.IsInstanceValid(_echoPlayer)) return;
		int rev = PlayerRev;
		bool below = rev >= _echoFrom && rev < _echoTo, above = rev >= _echoAboveFrom && rev < _echoAboveTo;
		if (!below && !above) return;
		_lastStep = Time.GetTicksMsec() * 0.001;
		_echoTail = 2;
		var p = _echoPlayer;
		GetTree().CreateTimer(above ? 0.18 : 0.38).Timeout += () => EchoStep(p, above);
	}

	/// <summary>A step on the stairs across the well, two turns below (or one above), in time with theirs.</summary>
	private void EchoStep(PlayerController p, bool above = false)
	{
		if (p == null || !GodotObject.IsInstanceValid(p)) return;
		Vector3 l = ToLocal(p.GlobalPosition);
		Vector3 at = ToGlobal(new Vector3(-l.X, l.Y + (above ? RevDrop : -2f * RevDrop), -l.Z));
		Sfx("step_metal", 6, at, above ? -8f : -10f, 6f);
	}

	private void DropSomething(PlayerController p)
	{
		// a chair, tumbling down the middle of the well from somewhere above
		var chair = new Node3D { Name = "Falling" };
		AddChild(chair);
		BuildChairMesh(chair);
		chair.GlobalPosition = ToGlobal(new Vector3(0, ToLocal(p.GlobalPosition).Y + 9f, 0));
		_faller = chair;
		_fallerV = 6f;
		var s = new AudioStreamPlayer3D { Stream = Load("res://assets/audio/sfx/fall_past_01.wav"), Bus = "Events", VolumeDb = 2f, UnitSize = 8f, MaxDistance = 60f };
		chair.AddChild(s);
		if (s.Stream != null) s.Play();
	}

	private void AddFigure(PlayerController p, int rev)
	{
		// at the rail of turn `rev`'s second corner, looking up the well
		int k = rev * 4 + 1;
		Vector2 c = CornerXZ(k);
		var fig = new Node3D { Name = "Figure", Position = new Vector3(Mathf.Sign(c.X) * (Inner + 0.25f), CornerY(k), Mathf.Sign(c.Y) * (Inner + 0.25f)) };
		AddChild(fig);
		var k1 = new MeshKit();
		k1.Mat(StationParts.StationTextures.Flat("sw_figure", new Color(0.02f, 0.02f, 0.02f), 1f, 0f));
		k1.Color = Colors.White;
		k1.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 1.55f, 0), 0.16f, 0.2f, 8, true);           // a long coat
		k1.Cylinder(new Vector3(0, 1.55f, 0), new Vector3(0, 1.62f, 0), 0.07f, 0.07f, 6, false);     // neck
		k1.Cylinder(new Vector3(0, 1.62f, 0), new Vector3(0, 1.9f, 0), 0.1f, 0.08f, 8, true);         // head, tipped back
		foreach (float s in new[] { -1f, 1f })
			k1.Cylinder(new Vector3(s * 0.2f, 1.45f, 0), new Vector3(s * 0.23f, 0.55f, 0), 0.04f, 0.03f, 5, true);   // arms, hanging long
		k1.CommitTo(fig, "Body", false);
		fig.LookAt(fig.GlobalPosition + new Vector3(-c.X, 0, -c.Y), Vector3.Up);
		fig.RotateObjectLocal(Vector3.Right, 0.25f);
		// a cold light behind it, just enough to stand it out against the dark
		fig.AddChild(new OmniLight3D { Position = new Vector3(0, 1.3f, 0.6f), LightColor = new Color(0.7f, 0.8f, 0.9f), LightEnergy = 0.9f, OmniRange = 3f, ShadowEnabled = false });
		_figure = fig;
		_figureRev = rev;
	}

	private void AddDoor(int k)
	{
		Vector2 c = CornerXZ(k);
		// on the wall at the corner's outside, facing into the landing
		bool onX = true;
		Vector3 n = onX ? new Vector3(-Mathf.Sign(c.X), 0, 0) : new Vector3(0, 0, -Mathf.Sign(c.Y));
		Vector3 at = new(Mathf.Sign(c.X) * (H - 0.03f), CornerY(k), c.Y);
		var door = new Node3D { Name = "Door", Position = at };
		AddChild(door);
		door.LookAt(door.GlobalPosition + ToGlobal(n) - ToGlobal(Vector3.Zero), Vector3.Up);
		var k1 = new MeshKit();
		k1.Mat(StairwellTextures.SteelMat);
		k1.Color = new Color(0.55f, 0.5f, 0.4f);
		BuildKit.Box(k1, new Vector3(0, 1.0f, -0.02f), new Vector3(0.84f, 2.0f, 0.05f));
		k1.Color = new Color(0.3f, 0.28f, 0.24f);
		BuildKit.Box(k1, new Vector3(0, 1.02f, -0.01f), new Vector3(0.96f, 2.1f, 0.03f));
		k1.Mat(StairwellTextures.RailMat);
		k1.Color = Colors.White;
		BuildKit.Box(k1, new Vector3(0.3f, 1.0f, -0.07f), new Vector3(0.12f, 0.03f, 0.04f));
		k1.CommitTo(door, "Door", true);
		var plate = new Label3D
		{
			Text = "3", Font = Paint, FontSize = 96, PixelSize = 0.004f, Modulate = new Color(0.8f, 0.78f, 0.7f, 0.9f),
			Shaded = true, Position = new Vector3(0, 1.62f, -0.05f), Rotation = new Vector3(0, Mathf.Pi, 0),
		};
		door.AddChild(plate);
		_door = door;
	}

	private void AddPortrait(int k, int which)
	{
		Vector2 c = CornerXZ(k);
		var frame = new Node3D { Name = "Portrait", Position = new Vector3(c.X, CornerY(k) + 1.5f, Mathf.Sign(c.Y) * (H - 0.04f)) };
		AddChild(frame);
		frame.Rotation = new Vector3(0, c.Y > 0 ? Mathf.Pi : 0f, 0.04f);
		PortraitKit.Build(frame, which, 0.55f, 0.7f);
	}

	private void AddChair(int k)
	{
		Vector2 c = CornerXZ(k);
		var chair = new Node3D { Name = "Chair", Position = new Vector3(c.X + Mathf.Sign(c.X) * 0.1f, CornerY(k), c.Y + Mathf.Sign(c.Y) * 0.1f) };
		AddChild(chair);
		BuildChairMesh(chair);
		// facing into the corner, away from the stairs
		chair.LookAt(chair.GlobalPosition + ToGlobal(new Vector3(Mathf.Sign(c.X), 0, Mathf.Sign(c.Y))) - ToGlobal(Vector3.Zero), Vector3.Up);
	}

	private static void BuildChairMesh(Node3D parent)
	{
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		k.Color = new Color(0.35f, 0.25f, 0.18f);
		BuildKit.Box(k, new Vector3(0, 0.45f, 0), new Vector3(0.42f, 0.04f, 0.42f));
		foreach (var (x, z) in new[] { (-0.18f, -0.18f), (0.18f, -0.18f), (-0.18f, 0.18f), (0.18f, 0.18f) })
			BuildKit.Box(k, new Vector3(x, 0.22f, z), new Vector3(0.04f, 0.44f, 0.04f));
		BuildKit.Box(k, new Vector3(0, 0.72f, 0.19f), new Vector3(0.42f, 0.5f, 0.03f));
		k.CommitTo(parent, "Chair", true);
	}

	private void Whisper(PlayerController p, float db)
	{
		Sfx("whisper_voice", 3, p.GlobalPosition + Vector3.Up * 3.5f + new Vector3(0.8f, 0, 0.6f), db, 4f);
	}

	private void Silence(float seconds)
	{
		_silenceUntil = Time.GetTicksMsec() * 0.001f + seconds;
		foreach (var (light, rev) in _bulbs) if (rev >= 50) light.Visible = false;
	}

	/// <summary>In the middle of the view and not behind anything.</summary>
	private bool Seen(Vector3 at)
	{
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return false;
		Vector3 to = at - cam.GlobalPosition;
		float dist = to.Length();
		if ((-cam.GlobalBasis.Z).Dot(to / Mathf.Max(dist, 0.001f)) < Mathf.Cos(Mathf.DegToRad(cam.Fov * 0.5f))) return false;
		var q = PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, at, 1u);
		if (StoryBeat.Player(this) is { } p) q.Exclude = new Godot.Collections.Array<Rid> { p.GetRid() };
		return GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
	}

	private static AudioStream Load(string path) => ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;

	private void Sfx(string name, int variants, Vector3 at, float db, float unit = 4f)
	{
		string path = $"res://assets/audio/sfx/{name}_{_rng.RandiRange(1, variants):00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, UnitSize = unit, MaxDistance = 80f, PitchScale = _rng.RandfRange(0.94f, 1.05f) };
		Cutscene.SceneRoot(this).AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
	}
}
