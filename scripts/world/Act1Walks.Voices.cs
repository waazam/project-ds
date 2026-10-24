using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The walks' sounds (STORY.md Rev 5, 2026-10-16). Every voice deep, on the Voice bus; every take through a no-repeat
/// picker; the stairs' silence (<see cref="StoryMute"/>, on the Master bus) cuts all of it.
/// - <b>Walk 1</b>: nothing is wrong, only sound: a distant twig snap off in the trees behind, once, on the way up to the
///   bridge; and out on the overlook's paths a bird that stops mid-song, the woods hushing after it. (The far rifle is
///   the footbridge's, as before.)
/// - <b>Walk 1, from the falls</b> (walk 2's until 2026-10-24): on the way back from them, one word, close and ahead of
///   him: "come." Once (a saved flag).
/// - <b>Walk 3</b>: "come and see", many of them, from the trees ahead along the way to the stairs and now and then from
///   behind, more of them the further he goes; if he's off the way they call from the way. They stop at the stairs.
///   The cabin's light goes out behind him once he is a little way out.
/// </summary>
public partial class Act1Walks
{
	public const string ComeFlag = "act1_come_heard";

	/// <summary>For tests: what has been heard (walk 1's twig and the bird cut off; walk 2's word; walk 3's calls).</summary>
	public bool TwigHeard { get; private set; }
	public bool BirdCutOff { get; private set; }
	public bool ReachedFalls { get; private set; }
	public bool ComeHeard { get; private set; }
	public int CallsHeard { get; private set; }
	public bool CallsOver { get; private set; }
	public bool CabinDark { get; private set; }

	[ExportGroup("Walk 3's voices")]
	/// <summary>Seconds between the calls (a burst of one to four voices each), and how near the stairs (m) they stop.</summary>
	[Export] public Vector2 CallEvery = new(7f, 13f);
	[Export] public float CallsStopAt = 45f;
	/// <summary>How far out from the cabin (m along the trail) walk 3's calls begin, and the cabin's light goes.</summary>
	[Export] public float CallsFrom = 28f, CabinDarkFrom = 60f;

	// the recorded take holds five deliveries of the line (stream seconds): each is a take of its own
	private static readonly (float from, float to)[] Phrases = { (0f, 1.8f), (2.8f, 4.15f), (5f, 6.1f), (6.75f, 7.5f), (8.55f, 9.6f) };
	private static readonly string[] Files = { "distant", "light", "medium", "loud" };
	/// <summary>"come" alone: the first word of the third delivery (stream seconds), cut before "and".</summary>
	private const float ComeFrom = 5.02f, ComeLength = 0.36f;
	private static RecentPicker _callPick, _twigPick, _songPick;
	private double _nextCall = -1;
	private Waterfall _waterfall;
	private Node3D _cabin;

	private void ReadySounds()
	{
		ComeHeard = StoryManager.Instance?.HasFlag(ComeFlag) ?? false;
		_waterfall = GetTree().CurrentScene?.FindChild("Waterfall", true, false) as Waterfall;
	}

	private void UpdateSounds(PlayerController player, float dt)
	{
		if (StoryMute.On) return;
		var pp = player.GlobalPosition;
		var q = new Vector2(pp.X, pp.Z);
		var vel = new Vector2(player.Velocity.X, player.Velocity.Z);
		float s = TrailS(q, out float offT);
		switch (_builtStage)
		{
			case 1:
				// the twig: on the way up the trail toward the bridge, off in the woods behind them to one side
				if (!TwigHeard && offT < 8f && s > 105f && s < 178f && vel.Length() > 1f)
				{
					TwigHeard = true;
					var back = -vel.Normalized();
					var side = new Vector2(back.Y, -back.X) * (_rng.Randf() < 0.5f ? 1f : -1f);
					var at = q + back * _rng.RandfRange(12f, 20f) + side * _rng.RandfRange(22f, 32f);
					int take = 1 + _twigPick.Next(_rng, 4);
					PlayWorld($"res://assets/audio/sfx/twig_snap_{take:00}.wav", "Distant", at, 0.4f, -3f, 10f, 120f, _rng.RandfRange(0.82f, 0.95f));
					GD.Print("[story] Act 1 walk 1: a twig snaps somewhere off in the trees");
				}
				// the bird that stops: out on the overlook's paths
				if (!BirdCutOff && vel.Length() > 0.5f && (Near(_spur, q, 7f, 30f) || Near(_ridge, q, 7f, 30f)))
				{
					BirdCutOff = true;
					CutOffSong(q, vel);
				}
				goto case 2;
			case 2:
				if (!ReachedFalls && (_waterfall != null && IsInstanceValid(_waterfall) ? pp.DistanceTo(_waterfall.LipCenter) < 14f : Near(_falls, q, 6f, FallsSplitAt)))
				{
					ReachedFalls = true;
					GD.Print("[story] Act 1 walk 1: at the falls");
				}
				// on the way back: down the falls path's far side (or anywhere well away from the falls again), walking on
				if (ReachedFalls && !ComeHeard && vel.Length() > 1f && (Near(_falls, q, 6f, FallsSplitAt + 45f) || AwayFromFalls(pp, 55f)))
				{
					ComeHeard = true;
					StoryManager.Instance?.SetFlag(ComeFlag);
					SayCome(player, vel);
				}
				break;
			case 3:
				UpdateCalls(player, q, s, dt);
				if (!CabinDark && s > CabinDarkFrom) PutOutCabinLight();
				break;
		}
	}

	/// <summary>Near a branch (within <paramref name="within"/> m of it), at least <paramref name="fromS"/> m along it.</summary>
	private static bool Near(Polyline2 line, Vector2 q, float within, float fromS)
	{
		if (line == null) return false;
		return line.Closest(q, out float sp) < within && sp >= fromS;
	}

	private bool AwayFromFalls(Vector3 p, float metres) =>
		_waterfall != null && IsInstanceValid(_waterfall) && p.DistanceTo(_waterfall.LipCenter) > metres;

	private AudioStreamPlayer3D PlayWorld(string path, string bus, Vector2 at, float height, float db, float unit, float maxDist, float pitch)
	{
		if (!ResourceLoader.Exists(path)) return null;
		var p = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = bus, VolumeDb = db, UnitSize = unit, MaxDistance = maxDist, PitchScale = pitch,
		};
		AddChild(p);
		p.GlobalPosition = new Vector3(at.X, _terrain.HeightAt(at.X, at.Y) + height, at.Y);
		p.Finished += p.QueueFree;
		p.Play();
		return p;
	}

	// ------------------------------------------------------------------ walk 1: the bird that stops

	private void CutOffSong(Vector2 q, Vector2 vel)
	{
		var ahead = vel.Normalized();
		var side = new Vector2(ahead.Y, -ahead.X) * (_rng.Randf() < 0.5f ? 1f : -1f);
		var at = q + ahead * _rng.RandfRange(4f, 10f) + side * _rng.RandfRange(10f, 16f);
		// a songbird call (1-8, never 7: the owner, 2026-10-15)
		int[] calls = { 1, 2, 3, 4, 5, 6, 8 };
		int n = calls[_songPick.Next(_rng, calls.Length)];
		var song = PlayWorld($"res://assets/audio/sfx/bird_{n:00}.wav", "Birds", at, _rng.RandfRange(5f, 8f), -4f, 5f, 60f, _rng.RandfRange(0.95f, 1.05f));
		if (song == null) return;
		float len = (float)(song.Stream.GetLength() / song.PitchScale);
		float cut = Mathf.Clamp(len * _rng.RandfRange(0.42f, 0.58f), 0.35f, 2.5f);
		// stopped mid-phrase (a breath of a fade, so it doesn't click), and the woods go quiet a moment after it
		var tw = song.CreateTween();
		tw.TweenInterval(cut);
		tw.TweenProperty(song, "volume_db", -60f, 0.05f);
		tw.TweenCallback(Callable.From(() =>
		{
			if (IsInstanceValid(song)) song.QueueFree();
			ForestAmbienceManager.Instance?.Startle(0.45f, 5f);
		}));
		GD.Print($"[story] Act 1 walk 1: a bird stops mid-song ({cut:0.0} s in)");
	}

	// ------------------------------------------------------------------ walk 2: "come."

	private void SayCome(PlayerController player, Vector2 vel)
	{
		var q = new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z);
		var ahead = vel.Normalized();
		var side = new Vector2(ahead.Y, -ahead.X) * _rng.RandfRange(-1.8f, 1.8f);
		var at = q + ahead * _rng.RandfRange(6.5f, 9f) + side;
		string file = Files[_callPick.Next(_rng, Files.Length)];
		string path = $"res://assets/audio/voice/come_and_see_{file}.mp3";
		if (!ResourceLoader.Exists(path)) return;
		// close, deep, felt more than heard: low and slow, a big near source
		var v = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Voice", VolumeDb = -3f, UnitSize = 6f, MaxDistance = 40f, PitchScale = 0.8f };
		AddChild(v);
		v.GlobalPosition = new Vector3(at.X, _terrain.HeightAt(at.X, at.Y) + 1.4f, at.Y);
		v.Play(ComeFrom);
		var tw = v.CreateTween();
		tw.TweenInterval(ComeLength / v.PitchScale);
		tw.TweenProperty(v, "volume_db", -60f, 0.12f);
		tw.TweenCallback(Callable.From(v.QueueFree));
		GD.Print("[story] Act 1 walk 2: \"come.\" ahead of them");
	}

	// ------------------------------------------------------------------ walk 3: "come and see"

	private void UpdateCalls(PlayerController player, Vector2 q, float s, float dt)
	{
		if (CallsOver) return;
		float u = RouteProgress(q, out float off);
		var end = RouteAt(RouteLength, out _);
		if (q.DistanceTo(end) < CallsStopAt || FirstClimbEvent.Approach > 0.02f)
		{
			CallsOver = true;
			GD.Print($"[story] Act 1 walk 3: the voices stop at the stairs ({CallsHeard} calls)");
			return;
		}
		if (u < CallsFrom && off < 12f) return;
		if (_nextCall < 0) _nextCall = _clock + 1.5;
		if (_clock < _nextCall) return;
		_nextCall = _clock + _rng.RandfRange(CallEvery.X, CallEvery.Y);
		float along = Mathf.Clamp(u / Mathf.Max(RouteLength, 1f), 0f, 1f);
		// many of them, more the further they go; mostly from the trees ahead along the way, now and then from behind
		int voices = 1 + (along > 0.3f ? 1 : 0) + (along > 0.6f ? 1 : 0) + (_rng.Randf() < 0.35f ? 1 : 0);
		float delay = 0f;
		for (int i = 0; i < voices; i++)
		{
			bool behind = i > 0 && _rng.Randf() < 0.3f;
			float at = behind ? u - _rng.RandfRange(12f, 24f) : u + _rng.RandfRange(16f, 32f);
			var p = RouteAt(Mathf.Clamp(at, 0f, RouteLength), out Vector2 dir);
			var lat = new Vector2(dir.Y, -dir.X) * (_rng.Randf() < 0.5f ? 1f : -1f) * _rng.RandfRange(5f, 13f);
			Call(p + lat, delay, behind);
			delay += _rng.RandfRange(0.15f, 0.7f);
		}
	}

	private void Call(Vector2 at, float delay, bool behind)
	{
		int take = _callPick.Next(_rng, Files.Length * Phrases.Length);
		string file = Files[take / Phrases.Length];
		var (from, to) = Phrases[take % Phrases.Length];
		string path = $"res://assets/audio/voice/come_and_see_{file}.mp3";
		if (!ResourceLoader.Exists(path)) return;
		float db = file switch { "distant" => -12f, "light" => -9f, "medium" => -6f, _ => -4f } + _rng.RandfRange(-2f, 1.5f) - (behind ? 2f : 0f);
		var v = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = "Voice", VolumeDb = -40f, UnitSize = 5f, MaxDistance = 70f,
			PitchScale = _rng.RandfRange(0.78f, 0.92f),   // deep, every one
		};
		AddChild(v);
		v.GlobalPosition = new Vector3(at.X, _terrain.HeightAt(at.X, at.Y) + _rng.RandfRange(0.8f, 2.4f), at.Y);
		var tw = v.CreateTween();
		tw.TweenInterval(delay);
		tw.TweenCallback(Callable.From(() => { if (!StoryMute.On) v.Play(from); }));
		tw.TweenProperty(v, "volume_db", db, 0.35f);
		tw.TweenInterval(Mathf.Max(0.2f, (to - from) / v.PitchScale - 0.5f));
		tw.TweenProperty(v, "volume_db", -60f, 0.2f);
		tw.TweenCallback(Callable.From(v.QueueFree));
		CallsHeard++;
	}

	/// <summary>The cabin's window goes dark behind them (walk 3): the cabin's own way of it if it has one, else its lights
	/// fade out.</summary>
	private void PutOutCabinLight()
	{
		CabinDark = true;
		_cabin ??= GetTree().GetFirstNodeInGroup("trailhead_cabin") as Node3D;
		if (_cabin == null || !IsInstanceValid(_cabin)) return;
		if (_cabin.HasMethod("PutOutLight")) { _cabin.Call("PutOutLight"); return; }
		foreach (var n in _cabin.FindChildren("*", "Light3D", true, false))
			if (n is Light3D l && l is not DirectionalLight3D)
				l.CreateTween().TweenProperty(l, "light_energy", 0f, 3f);
		GD.Print("[story] Act 1 walk 3: the cabin's light is gone behind them");
	}
}
