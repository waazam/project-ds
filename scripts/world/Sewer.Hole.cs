using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// The hole in the cistern (Act 17's end). Going up to it asks "Drop down into the hole?", and a yes
/// asks again, "Are you sure?". Two yeses and they drop (Act 18, <see cref="Checkpoint.Act17Finished"/>).
/// Saying no changes the room:
/// <list type="bullet">
/// <item>the first no: "come and see", whispered from one pipe mouth, then another, then another, round the room;</item>
/// <item>the second: the light turns to a hazy teal that makes the room hard to see across, and the
/// whispers multiply and don't stop;</item>
/// <item>after that, each no only brings them closer.</item>
/// </list>
/// </summary>
public partial class Sewer
{
	public int Noes { get; private set; }
	public bool Teal => _teal;
	public bool Asking { get; private set; }
	public bool Dropped { get; private set; }
	public int Whispers { get; private set; }
	public Interactable HoleUse => _holeUse;

	private static readonly Color TealFog = new(0.05f, 0.2f, 0.19f);
	private static readonly string[] Takes = { "distant", "light", "medium", "loud" };
	private Interactable _holeUse;
	private bool _teal;
	private float _whisperT = -1f;

	private void BuildHole()
	{
		_holeUse = new Interactable { Name = "HoleUse", Prompt = "Drop down into the hole?", PickRadius = 1.3f, MaxDistance = 2.8f, Position = HoleLocal + Vector3.Up * 0.7f };
		_holeUse.Interacted += OnHole;
		AddChild(_holeUse);
	}

	private void OnHole(PlayerController player)
	{
		if (Asking || Dropped) return;
		_ = Ask(player);
	}

	private async Task Ask(PlayerController player)
	{
		Asking = true;
		if (ChoicePrompt.Instance == null) { Cutscene.SceneRoot(this).AddChild(new ChoicePrompt { Name = "ChoicePrompt" }); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
		const string hint = "A / D  choose      E  answer";
		int first = await ChoicePrompt.Instance.Ask(player, "Yes", "No", "Drop down into the hole?", hint);
		int second = -1;
		if (first == 0 && GodotObject.IsInstanceValid(player))
		{
			await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
			second = await ChoicePrompt.Instance.Ask(player, "Yes", "No", "Are you sure?", hint);
		}
		Asking = false;
		if (!GodotObject.IsInstanceValid(player)) return;
		if (first == 0 && second == 0)
		{
			Dropped = true;
			_holeUse.Enabled = false;
			GD.Print("[story] Act 17: yes, and yes - into the hole");
			_ = Cutscene.Run(this, ct => Drop(player, ct), lockInput: true, freezeBody: true);
			return;
		}
		if (first < 0 && second < 0) return;   // stepped away from it: no answer
		SaidNo(player);
	}

	private void SaidNo(PlayerController player)
	{
		Noes++;
		GD.Print($"[story] Act 17: no ({Noes})");
		if (Noes == 1) _ = RoundTheRoom(player);
		else if (Noes == 2)
		{
			_teal = true;
			foreach (var l in _lamps) { l.LightColor = new Color(0.35f, 0.9f, 0.85f); l.LightEnergy *= 0.6f; }
			_holeLight.LightColor = new Color(0.45f, 0.95f, 0.9f);
			_holeLight.LightEnergy = 2.5f;
			_whisperT = 1.5f;
		}
		else _whisperT = 0.3f;
	}

	/// <summary>The first no: one voice, going round the pipe mouths.</summary>
	private async Task RoundTheRoom(PlayerController player)
	{
		Vector3[] spots =
		{
			new(-RoomX + 0.5f, 1.8f, RoomZ0 + 10f), new(-11f, 2f, RoomZ1 - 0.5f), new(RoomX - 0.5f, 2f, RoomZ0 + 25f), new(12f, 1.5f, RoomZ0 + 0.5f),
		};
		for (int i = 0; i < spots.Length; i++)
		{
			Voice(Takes[i % Takes.Length], ToGlobal(spots[i]), -4f);
			await ToSignal(GetTree().CreateTimer(1.1), SceneTreeTimer.SignalName.Timeout);
		}
	}

	/// <summary>After the second no, the whispers don't stop: from anywhere, overlapping, more of them the more they refuse.</summary>
	private void ProcessWhispers(PlayerController player, float dt)
	{
		if (!_teal || Dropped) return;
		_whisperT -= dt;
		if (_whisperT > 0f) return;
		_whisperT = _rng.RandfRange(1.2f, 3.2f) / Mathf.Max(1, Noes - 1);
		float a = _rng.RandfRange(0, Mathf.Tau), d = _rng.RandfRange(4f, 16f);
		Vector3 at = player.GlobalPosition + new Vector3(Mathf.Cos(a) * d, _rng.RandfRange(0.5f, 5f), Mathf.Sin(a) * d);
		Voice(Takes[_rng.RandiRange(0, Takes.Length - 1)], at, _rng.RandfRange(-12f, -5f), _rng.RandfRange(0.85f, 1.1f));
	}

	private void Voice(string take, Vector3 at, float db, float pitch = 1f)
	{
		string path = $"res://assets/audio/voice/come_and_see_{take}.mp3";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Unnatural", VolumeDb = db, UnitSize = 6f, MaxDistance = 60f, PitchScale = pitch };
		Cutscene.SceneRoot(this).AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
		Whispers++;
	}

	/// <summary>Two yeses: to the edge, a look down into it, a step, and the black takes the screen.</summary>
	private async Task Drop(PlayerController player, CancellationToken ct)
	{
		var rig = player.CameraRig;
		Vector3 hole = HoleWorld;
		Vector3 from = player.GlobalPosition;
		Vector3 away = from - hole;
		away.Y = 0f;
		Vector3 edge = hole + away.Normalized() * (HoleHalf + 0.2f);
		edge.Y = from.Y;
		double t = 0;
		while (t < 1.0)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			player.GlobalPosition = from.Lerp(edge, Mathf.SmoothStep(0f, 1f, (float)t));
			Vector3 to = hole + Vector3.Down * 2f - rig.Camera.GlobalPosition;
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-to.X, -to.Z)) * Mathf.Min(1f, dt * 4f), 0));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), Mathf.Min(1f, dt * 3f)));
		}
		await Cutscene.Wait(this, 0.8, ct);
		var fader = StoryBeat.Fader(this);
		Vector3 top = player.GlobalPosition;
		float v = 0f, fallen = 0f;
		t = 0;
		while (t < 1.2)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			v += 9.8f * dt;
			fallen += v * dt;
			float u = Mathf.Min(1f, (float)t / 0.35f);
			player.GlobalPosition = top.Lerp(hole with { Y = top.Y }, u) + Vector3.Down * fallen;
			if (t > 0.45 && fader != null && !fader.IsBlack) fader.SetBlack(true);
		}
		fader?.SetBlack(true);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act17Finished);
		GD.Print("[story] Act 17 done: down the hole - Act 18 starts here");
		// Act 18 isn't built yet: the credits, from the black
		await Cutscene.Wait(this, 2.0, ct);
		if (GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending) await ending.Credits(fader, ct);
	}
}
