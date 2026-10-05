using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.World.HallwayParts;

namespace ProjectDS.World;

/// <summary>
/// The red lights in the hall's second half, when he is in front of them (the owner, 2026-10-04: "I love how you have the
/// shadowman inch towards the player before the light turns green again. Can we vary this so it is more interesting ...
/// make 6 - 12 unique variations ... the goal is that a typical playthrough of the game won't trigger every variation
/// unless the player dies a lot"). Each red picks one, weighted, never the same twice running; the plain inch is by far the
/// commonest (after playing them the owner found the strangest goofy: from the floor, from above, the face-down slither,
/// side to side and the zigzag are no longer picked). Every one keeps him in front of them and short of the door, and ends with him
/// standing in the hall, frozen, to be walked round in the green.
/// <list type="number">
/// <item><b>Inch</b>: a jump at a time closer, as it was.</item>
/// <item><b>Far lunges</b>: far down the hall, and then three lunges, each covering metres in a blink.</item>
/// <item><b>From the wall</b>: out of the wall beside the way, in jerks, turning to them as he comes clear.</item>
/// <item><b>From the floor</b>: rising out of the floor in front of them, a lurch at a time.</item>
/// <item><b>Melting eyes</b>: right in front of them from the start, unmoving; his eyes melt slowly from colour to colour,
/// running down his face (slow: never a flash, never a cycle).</item>
/// <item><b>From above</b>: hanging upside down out of the dark overhead, lowered a jerk at a time to look them in the face;
/// with the green he drops to his feet.</item>
/// <item><b>Many</b>: three of him down the hall, all coming at once; with the green only one is left.</item>
/// <item><b>Stillness</b>: far off, not moving at all, the whole red; then, at its very end, right in front of them.</item>
/// <item><b>Looming</b>: coming closer and growing taller each time, until he stoops over them.</item>
/// <item><b>Slither</b>: face down on the floor, head first, dragged toward them in jerks; with the green he is standing.</item>
/// <item><b>Side to side</b>: in front, then behind, then in front, then at their back, a jump at a time.</item>
/// <item><b>Zigzag</b>: from one side of the hall to the other as he comes, never where they are looking.</item>
/// </list>
/// </summary>
public partial class Act15Hallway
{
	public enum Turn { Inch, FarLunges, FromWall, FromFloor, MeltingEyes, FromAbove, Many, Stillness, Looming, Slither, SideToSide, Zigzag }

	/// <summary>How often each turns up (out of their sum): the plain inch the commonest, the strangest the rarest.</summary>
	private static readonly (Turn turn, int weight)[] TurnWeights =
	{
		// (the owner, 2026-10-04, after playing them: "having him face down on the ground and coming in from the ceiling looked
		// goofy, we should go closer to what we had before the change and make it as scary as possible": the floor, the
		// ceiling, the face-down crawl and the hopping about are out; the inch, as it was, is most of them again, and the
		// rest stay close to it, all of him standing, all of him coming)
		(Turn.Inch, 44), (Turn.FarLunges, 12), (Turn.Stillness, 11), (Turn.Looming, 11), (Turn.MeltingEyes, 9), (Turn.FromWall, 7), (Turn.Many, 6),
	};

	/// <summary>The turn this red (for tests); and every turn seen this session.</summary>
	public Turn CurrentTurn { get; private set; }
	public readonly HashSet<Turn> TurnsSeen = new();
	// (its own dice, not the hall's seeded ones: a different run of turns every playthrough)
	private readonly RandomNumberGenerator _turnRng = NewTurnRng();
	private static RandomNumberGenerator NewTurnRng() { var r = new RandomNumberGenerator(); r.Randomize(); return r; }
	private Turn _lastTurn = (Turn)(-1);
	/// <summary>The autotest takes them in order, from `--act15-turn=N` (so a run sees as many as it can).</summary>
	private static int _testTurn = -1;
	private int _turnStep;
	private bool _turnActive;
	private float _turnSide = 1f, _lungeT = -1f;
	private Vector3 _lungeFrom, _lungeTo;
	private readonly List<ShadowMan> _copies = new();

	private Turn PickTurn()
	{
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--autotest") >= 0)
		{
			if (_testTurn < 0)
			{
				_testTurn = 0;
				foreach (var a in OS.GetCmdlineUserArgs())
					if (a.StartsWith("--act15-turn=") && int.TryParse(a["--act15-turn=".Length..], out int n)) _testTurn = n;
			}
			return TurnWeights[_testTurn++ % TurnWeights.Length].turn;
		}
		int sum = 0;
		foreach (var (_, w) in TurnWeights) sum += w;
		for (int tries = 0; tries < 4; tries++)
		{
			int roll = _turnRng.RandiRange(0, sum - 1);
			foreach (var (t, w) in TurnWeights)
			{
				if (roll < w) { if (t != _lastTurn || tries == 3) return t; break; }
				roll -= w;
			}
		}
		return Turn.Inch;
	}

	/// <summary>A red with him in front: the turn picked, and its first placing.</summary>
	private void BeginTurn(PlayerController player)
	{
		ResetPose();
		CurrentTurn = PickTurn();
		_lastTurn = CurrentTurn;
		TurnsSeen.Add(CurrentTurn);
		_turnStep = 0;
		_turnActive = true;
		_lungeT = -1f;
		_turnSide = _turnRng.Randf() < 0.5f ? -1f : 1f;
		GD.Print($"[story] Act 15: the red's turn - {CurrentTurn}");
		TurnStep(player, 0);
	}

	/// <summary>When in the red (as a fraction of it) each step comes.</summary>
	private static readonly float[] TurnAt = { 0f, 0.28f, 0.5f, 0.7f };

	private void UpdateTurn(PlayerController player, float dt)
	{
		float u = _redT / Mathf.Max(_timer, 0.01f);
		if (CurrentTurn == Turn.Stillness)
		{
			if (_turnStep == 1 && u >= 0.86f) { TurnStep(player, 1); _turnStep = 2; }
			else if (_turnStep == 0) _turnStep = 1;
		}
		else if (_turnStep + 1 < TurnAt.Length && u >= TurnAt[_turnStep + 1]) { _turnStep++; TurnStep(player, _turnStep); }
		// a lunge: metres in a blink, easing in hard (jerked, not walked)
		if (_lungeT >= 0f)
		{
			_lungeT += dt / 0.13f;
			float e = Mathf.Clamp(_lungeT, 0f, 1f);
			Place(_lungeFrom.Lerp(_lungeTo, e * e), player);
			if (_lungeT >= 1f) _lungeT = -1f;
		}
		// the melting eyes: through four colours over the red, each eased into the next
		if (CurrentTurn == Turn.MeltingEyes)
		{
			Color[] cs = { new(0.95f, 0.95f, 1f), new(1f, 0.62f, 0.2f), new(0.85f, 0.06f, 0.05f), new(0.55f, 0.15f, 0.8f), new(0.35f, 0.8f, 0.3f) };
			float f = Mathf.Clamp(u, 0f, 0.999f) * (cs.Length - 1);
			int i = (int)f;
			var c = cs[i].Lerp(cs[i + 1], Mathf.SmoothStep(0f, 1f, f - i));
			_shadow.EyeColor(c);
			_shadow.EyeDrips(true, c);
			_shadow.Glare = Mathf.Lerp(0.35f, 0.7f, u);
		}
	}

	/// <summary>The local spot <paramref name="dz"/> metres ahead of them (and across at <paramref name="x"/>), short of the door.</summary>
	private Vector3 Ahead(float dz, float x = 0f)
	{
		float z = Mathf.Min(PlayerZ + dz, End - DoorKeepClear);
		z = Mathf.Max(z, PlayerZ + 0.9f);
		return new Vector3(Mathf.Clamp(x, -W2 * 0.5f + 0.2f, W2 * 0.5f - 0.2f), 0f, z);
	}

	private void Place(Vector3 local, PlayerController player)
	{
		_shadow.GlobalPosition = ToGlobal(local);
	}

	/// <summary>Stands him at a local spot, facing them (upright).</summary>
	private void Stand(Vector3 local, PlayerController player, float yLift = 0f)
	{
		_shadow.Rotation = Vector3.Zero;
		_shadow.StandAt(ToGlobal(local + Vector3.Up * yLift), player.GlobalPosition with { Y = ToGlobal(local).Y });
	}

	private void Breath(float db) => Sfx("shadow_breath", 2, _shadow.GlobalPosition + Vector3.Up * 1.8f, db, 2f);

	private void TurnStep(PlayerController player, int i)
	{
		switch (CurrentTurn)
		{
			case Turn.Inch:
			case Turn.Looming:
				Stand(Ahead(FrontDist[i]), player);
				if (CurrentTurn == Turn.Looming) _shadow.Scale = Vector3.One * (1f + 0.2f * i);
				// (each jump closer, his eyes burn a little brighter and his breath is louder: the last one right on them)
				_shadow.Glare = Mathf.Lerp(0.3f, 0.85f, i / 3f);
				Breath(i == 3 ? -2f : -14f + 3f * i);
				break;
			case Turn.FarLunges:
				if (i == 0) { Stand(Ahead(30f), player); Breath(-18f); break; }
				// three lunges: 30 m, 17, 8, then 2.2 in front of them
				float[] d = { 30f, 17f, 8f, 2.2f };
				var from = ToLocal(_shadow.GlobalPosition);
				Stand(from, player);
				_lungeFrom = from;
				_lungeTo = Ahead(d[i]);
				_lungeT = 0f;
				Sfx("shadow_strike", 1, ToGlobal(_lungeTo) + Vector3.Up * 1.8f, i == 3 ? -6f : -14f, 3f);
				break;
			case Turn.FromWall:
			{
				// out of the wall, sideways, in jerks; turning from facing across the hall to facing them
				float[] xs = { W2 * 0.5f + 0.15f, W2 * 0.5f - 0.15f, W2 * 0.5f - 0.6f, 0.6f };
				var at = Ahead(3.4f - i * 0.4f) with { X = _turnSide * xs[i] };
				_shadow.GlobalPosition = ToGlobal(at);
				var across = ToGlobal(at + new Vector3(-_turnSide, 0, 0));
				var toward = player.GlobalPosition with { Y = _shadow.GlobalPosition.Y };
				var look = across.Lerp(toward, i / 3f) - _shadow.GlobalPosition;
				look.Y = 0;
				if (look.LengthSquared() > 0.001f) _shadow.GlobalBasis = Basis.LookingAt(look.Normalized(), Vector3.Up);
				Breath(i == 3 ? -5f : -13f);
				break;
			}
			case Turn.FromFloor:
			{
				float[] ys = { -1.95f, -1.25f, -0.55f, 0f };
				Stand(Ahead(3.0f), player, ys[i]);
				Breath(i == 3 ? -5f : -14f);
				break;
			}
			case Turn.MeltingEyes:
				// (face to face: sunk so his eyes are at theirs; standing his full height that close, his face was off the top of the
				// screen and the eyes never seen)
				if (i == 0) { Stand(Ahead(1.35f), player, -0.72f); Breath(-3f); }
				break;
			case Turn.FromAbove:
			{
				// upside down out of the dark overhead (his feet up there, his head hanging), lowered a jerk at a time
				float[] tops = { 14f, 9f, 6f, 4.35f };
				var at = Ahead(2.4f);
				_shadow.GlobalPosition = ToGlobal(at + Vector3.Up * tops[i]);
				var look = player.GlobalPosition - _shadow.GlobalPosition;
				look.Y = 0;
				_shadow.GlobalBasis = Basis.LookingAt(look.Normalized(), Vector3.Up) * new Basis(Vector3.Forward, Mathf.Pi);
				Breath(i == 3 ? -4f : -15f);
				break;
			}
			case Turn.Many:
			{
				// three of him, all a step nearer each time; he is the nearest
				float[] near = { 6f, 4.6f, 3.2f, 2.0f };
				Stand(Ahead(near[i], -0.3f * _turnSide), player);
				while (_copies.Count < 2)
				{
					var c = new ShadowMan { Name = $"ShadowCopy{_copies.Count}" };
					AddChild(c);
					_copies.Add(c);
				}
				for (int k = 0; k < 2; k++)
				{
					_copies[k].Visible = true;
					var at = Ahead(near[i] + 4.5f * (k + 1), (k == 0 ? 1f : -1f) * _turnSide * 1.1f);
					_copies[k].Rotation = Vector3.Zero;
					_copies[k].StandAt(ToGlobal(at), player.GlobalPosition with { Y = ToGlobal(at).Y });
				}
				Breath(i == 3 ? -4f : -11f);
				break;
			}
			case Turn.Stillness:
				if (i == 0) { Stand(Ahead(13f), player); }
				else { Stand(Ahead(1.3f), player); Breath(-2f); }
				break;
			case Turn.Slither:
			{
				// face down, head first toward them (his feet the far end), dragged a jerk at a time
				float[] heads = { 7.5f, 5.2f, 3.4f, 1.9f };
				var head = Ahead(heads[i]);
				var feet = head + new Vector3(0, 0.18f, ShadowMan.Height);
				_shadow.GlobalPosition = ToGlobal(feet);
				_shadow.GlobalBasis = GlobalBasis * new Basis(Vector3.Right, -Mathf.Pi * 0.5f);   // (his head toward them, his face to the floor)
				Breath(i == 3 ? -4f : -13f);
				break;
			}
			case Turn.SideToSide:
			{
				// in front, behind, in front, at their back
				bool front = i % 2 == 0;
				_inFront = front;
				float[] ds = { 4.2f, 3.0f, 2.4f, 1.4f };
				var cam = player.CameraRig;
				float yaw = cam?.Yaw ?? 0f;
				Vector3 local = front ? Ahead(ds[i]) : ToLocal(player.GlobalPosition + new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * ds[i]) with { Y = 0f };
				if (!front) local.X = Mathf.Clamp(local.X, -W2 * 0.5f + 0.45f, W2 * 0.5f - 0.45f);
				Stand(local, player);
				Breath(i == 3 ? -4f : -12f);
				break;
			}
			case Turn.Zigzag:
			{
				float[] ds = { 5.2f, 4.0f, 2.9f, 1.9f };
				float[] xs = { -1.4f, 1.4f, -0.9f, 0.3f };
				Stand(Ahead(ds[i], xs[i] * _turnSide), player);
				Breath(i == 3 ? -4f : -12f);
				break;
			}
		}
	}

	/// <summary>The green: whatever turn it was, he ends standing in the hall, frozen (dropped from above, up off the floor,
	/// out of the floor); the others are gone; his eyes are his own again.</summary>
	private void EndTurn(PlayerController player)
	{
		var l = ToLocal(_shadow.GlobalPosition);
		bool odd = CurrentTurn is Turn.FromAbove or Turn.Slither or Turn.FromFloor || _lungeT >= 0f;
		if (CurrentTurn == Turn.Slither) l.Z -= ShadowMan.Height * 0.6f;
		if (odd)
		{
			if (CurrentTurn == Turn.FromAbove) Sfx("body_thump", 2, _shadow.GlobalPosition, -6f, 3f);
			Stand(new Vector3(Mathf.Clamp(l.X, -W2 * 0.5f + 0.45f, W2 * 0.5f - 0.45f), 0f, Mathf.Clamp(l.Z, PlayerZ + 0.9f, End - DoorKeepClear)), player);
		}
		_lungeT = -1f;
		foreach (var c in _copies) c.Visible = false;
		_shadow.EyeColor(new Color(0.95f, 0.95f, 1f));
		_shadow.EyeDrips(false, Colors.White);
	}

	/// <summary>Back to himself (each red starts from his own shape).</summary>
	private void ResetPose()
	{
		_shadow.Scale = Vector3.One;
		_shadow.Rotation = new Vector3(0, _shadow.Rotation.Y, 0);
		foreach (var c in _copies) c.Visible = false;
		_shadow.EyeColor(new Color(0.95f, 0.95f, 1f));
		_shadow.EyeDrips(false, Colors.White);
		_shadow.Glare = 0.3f;
	}
}
