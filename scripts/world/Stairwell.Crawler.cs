using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Audio;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// The owner's additions to Act 14: their own music for the descent (`stairwell_descent`, from the moment
/// the player starts down the shaft), and the crawler (<see cref="Crawler"/>), which comes down the stairs
/// after them once the lantern's flame has died, a quarter of the way down.
///
/// It keeps its distance on its own terms: it hangs back a turn or so above (in sight across the well, if
/// they look up), then surges closer, then falls back; however fast they go it's never far behind, and if
/// they stop it comes on. The point is not to look back, and to keep going. Running is the escape now:
/// the shaft's old trick (a sprint quietly taken back up to the top) is off while it hunts.
///
/// If it reaches them it takes them: a lunge, black, and they come to three turns further up, with it
/// somewhere above again. (Not a death: the descent is long, and the stairwell's punishment has always
/// been more stairs.) At the fallen flight it stops, crouched on the flights above, watching; it is gone
/// when they jump.
/// </summary>
public partial class Stairwell
{
	/// <summary>The crawler's gap (in flights, along the stairs) it hangs back at, the least, and the most.</summary>
	public const float CrawlGap = 3.5f, CrawlGapMin = 1.1f, CrawlGapMax = 6f;

	public Crawler Crawler { get; private set; }
	public bool CrawlerHunting { get; private set; }
	/// <summary>For tests: its gap now (flights), the least and most seen while hunting, and times caught.</summary>
	public float CrawlerGap { get; private set; }
	public float CrawlerGapSeenMin { get; private set; } = 999f;
	public float CrawlerGapSeenMax { get; private set; }
	public int CrawlerCatches { get; private set; }
	public bool MusicPlaying => _music is { Playing: true };

	private AudioStreamPlayer _music;
	private bool _musicStarted;
	private float _crawlS, _crawlV, _playerSPrev = -1f, _playerV, _still, _surge, _surgeAt = 9f, _huntTime;
	private bool _taking, _musicEnding;

	/// <summary>How far down the stairs a local point is, in flights (0 at the top landing).</summary>
	public static float PathS(Vector3 local) => (Y0 - local.Y) / FlightDrop;

	/// <summary>The stairs' line at <paramref name="s"/> flights down (local), from landing to landing.</summary>
	public static Vector3 PathLocal(float s)
	{
		int k = Mathf.FloorToInt(s);
		float f = s - k;
		Vector2 a = CornerXZ(k), b = CornerXZ(k + 1);
		Vector2 xz = a.Lerp(b, f);
		return new Vector3(xz.X, Mathf.Lerp(CornerY(k), CornerY(k + 1), f), xz.Y);
	}

	private void ProcessDescentMusic(PlayerController player, Vector3 l)
	{
		var story = StoryManager.Instance;
		bool firstDescent = story == null || story.Current < Checkpoint.Act14Finished;
		// fading away at the edge of the fallen flight
		if (_music is { Playing: true } && PathS(l) >= GapCorner - 0.5f && !_musicEnding) { _musicEnding = true; EndDescentMusic(5f); }
		// on the way down: from the top landing, once, to the edge
		if (!_musicStarted && firstDescent && PlayerInShaft && PathS(l) > 0.3f)
		{
			_musicStarted = true;
			const string path = "res://assets/audio/music/stairwell_descent.mp3";
			if (ResourceLoader.Exists(path))
			{
				_music = new AudioStreamPlayer { Name = "DescentMusic", Stream = GD.Load<AudioStream>(path), Bus = "Music", VolumeDb = -30f };
				if (AudioServer.GetBusIndex("Music") < 0) _music.Bus = "Master";
				AddChild(_music);
				_music.Play();
				CreateTween().TweenProperty(_music, "volume_db", -7f, 4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
				GD.Print("[story] Act 14: the descent's music begins");
			}
		}
	}

	/// <summary>Fades the descent's music out (at the edge of the fallen flight, or the fall).</summary>
	public void EndDescentMusic(float seconds = 3f)
	{
		if (_music == null || !_music.Playing) return;
		var tw = CreateTween();
		tw.TweenProperty(_music, "volume_db", -60f, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		tw.TweenCallback(Callable.From(() => _music?.Stop()));
	}

	/// <summary>The crawler lets itself down onto the stairs two turns above them.</summary>
	private void StartCrawler(PlayerController player)
	{
		var story = StoryManager.Instance;
		if (Crawler != null || (story != null && story.Current >= Checkpoint.Act14Finished)) return;
		Crawler = new Crawler { Name = "Crawler" };
		AddChild(Crawler);
		float sP = PathS(ToLocal(player.GlobalPosition));
		_crawlS = Mathf.Max(0f, sP - 8f);
		_crawlV = 0f;
		CrawlerHunting = true;
		PlaceCrawler(0.016f);
		Sfx("stair_groan", 3, Crawler.GlobalPosition, 2f, 10f);
		AudioDirector.OneShot(this, "crawler_breath", 3, Crawler.GlobalPosition, 0f, "Events", 4f);
		GD.Print("[story] Act 14: something comes down the stairs after them");
	}

	private void PlaceCrawler(float dt)
	{
		Vector3 at = PathLocal(_crawlS), ahead = PathLocal(_crawlS + 0.3f);
		Crawler.Place(ToGlobal(at + Vector3.Down * 0.1f), ToGlobal(ahead) - ToGlobal(at));
	}

	private void ProcessCrawler(PlayerController player, Vector3 l, float dt)
	{
		if (Crawler == null || _taking) return;
		// gone once they've jumped
		if (Jumped) { if (Crawler.Visible) { Crawler.Visible = false; CrawlerHunting = false; } return; }
		float sP = PathS(l);
		if (_playerSPrev < 0f) _playerSPrev = sP;
		_playerV = Mathf.Lerp(_playerV, (sP - _playerSPrev) / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-3f * dt));
		_playerSPrev = sP;
		if (!CrawlerHunting)
		{
			Crawler.Hurry = 0f;
			PlaceCrawler(dt);
			return;
		}
		_huntTime += dt;
		float gap = sP - _crawlS;
		CrawlerGap = gap;
		CrawlerGapSeenMin = Mathf.Min(CrawlerGapSeenMin, gap);
		CrawlerGapSeenMax = Mathf.Max(CrawlerGapSeenMax, gap);
		// standing still, it comes on
		_still = Mathf.Abs(_playerV) < 0.08f ? _still + dt : 0f;
		// now and then it surges (sooner the longer they dawdle), then falls back
		if (_huntTime >= _surgeAt) { _surge = 2.2f; _surgeAt = _huntTime + (float)GD.RandRange(7.0, 14.0); }
		_surge = Mathf.Max(0f, _surge - dt);
		bool tests = GameSettings.Instance?.AutoTest ?? false;
		float want = CrawlGap + 1.2f * Mathf.Sin(_huntTime * 0.55f);
		if (_surge > 0f) want = CrawlGapMin + 0.4f;
		if (_still > 2.5f) want = tests ? CrawlGapMin : 0f;
		float speed = Mathf.Max(0f, _playerV) + 0.9f * (gap - want);
		if (gap > CrawlGapMax) speed = Mathf.Max(speed, 5f);
		speed = Mathf.Clamp(speed, 0f, 5.5f);
		_crawlV = Mathf.Lerp(_crawlV, speed, 1f - Mathf.Exp(-(speed > _crawlV ? 5f : 2.5f) * dt));
		_crawlS += _crawlV * dt;
		Crawler.Hurry = Mathf.Clamp(_crawlV / 2.5f, 0f, 1f);
		PlaceCrawler(dt);
		// at the fallen flight it stops, well back, and watches
		if (sP >= GapCorner - 2.5f)
		{
			CrawlerHunting = false;
			_crawlS = Mathf.Min(_crawlS, sP - CrawlGapMax + 1f);
			GD.Print("[story] Act 14: at the edge - it stops on the stairs above, and watches");
			return;
		}
		// it reaches them
		if (!tests && gap < 0.45f && player.GlobalPosition.DistanceTo(Crawler.GlobalPosition) < 1.8f)
			_ = Cutscene.Run(this, ct => TakenBack(player, ct), lockInput: true, freezeBody: true);
	}

	/// <summary>It has them: a turn of the head to it, its lunge, black; they come to three turns further up.</summary>
	private async System.Threading.Tasks.Task TakenBack(PlayerController player, System.Threading.CancellationToken ct)
	{
		_taking = true;
		CrawlerCatches++;
		GD.Print("[story] Act 14: the crawler reaches them - taken back up");
		var rig = player.CameraRig;
		Vector3 eye = rig.Camera.GlobalPosition, at = Crawler.GlobalPosition + Vector3.Up * 0.1f;
		float yaw = Mathf.Atan2(-(at - eye).X, -(at - eye).Z);
		float y0 = rig.Yaw;
		AudioDirector.OneShot(this, "crawler_breath", 3, at, 6f, "Events", 5f, 0f);
		double t = 0;
		while (t < 0.45)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = Mathf.SmoothStep(0f, 1f, (float)(t / 0.45));
			rig.SnapBehind(Mathf.LerpAngle(y0, yaw, u));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, -0.3f, u));
			// it comes up the last of the way at them
			Crawler.Place(Crawler.GlobalPosition.Lerp(eye + (at - eye).Normalized() * 0.7f + Vector3.Down * 0.9f, u * 0.5f), eye - Crawler.GlobalPosition);
			rig.Shake = new Vector3(Mathf.Sin((float)t * 50f), Mathf.Cos((float)t * 41f), 0) * 0.01f * u;
		}
		rig.Shake = Vector3.Zero;
		var fader = StoryBeat.Fader(this);
		if (fader != null) await fader.Fade(1f, 0.25f, ct);
		// three turns further up, where they were on the turn; it somewhere above again
		int up = Mathf.Min(3, PlayerRev);
		Vector3 d = ToGlobal(new Vector3(0, up * RevDrop, 0)) - ToGlobal(Vector3.Zero);
		player.GlobalPosition += d;
		player.Velocity = Vector3.Zero;
		rig.ShiftBy(d);
		PlayerRev = Mathf.Max(0, PlayerRev - up);
		_crawlS = PathS(ToLocal(player.GlobalPosition)) - 8f;
		_crawlV = 0f;
		_still = 0f;
		_surgeAt = _huntTime + 10f;
		_playerSPrev = -1f;
		PlaceCrawler(0.016f);
		await Cutscene.Wait(this, 1.2, ct);
		if (fader != null) await fader.Fade(0f, 1.2f, ct);
		_taking = false;
	}
}
