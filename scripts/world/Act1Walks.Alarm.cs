using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-29, the owner's friend's playtest) Act 1's opening sequence with the cabin up the trail in the woods, before
/// the footbridge (it was by the lot):
/// <list type="bullet">
/// <item><b>Check in.</b> The camera out of the trunk and the chapter's title gone: "I should check into the cabin." The
/// door is locked; the key is under the mat (Cabin.Story.cs).</item>
/// <item><b>The alarm.</b> Pictures taken, at the trunk across the trail past the overlook's fork (or, the overlook and
/// the falls both done, well up the trail past the cabin): the van's alarm going off far off, back at the lot. The cabin's
/// door shuts behind him, out of sight. Walking back it gets louder, its volume set by how far he is from the van.</item>
/// <item><b>The silence.</b> Level with the cabin on the way back (or near it from any side) it stops dead; the cabin's
/// door slams open; "Post Pictures." The first pinning waits for this (<see cref="PhotoBoard.PostFlag"/>). The van is gone
/// from the lot from then on (saved), and walking back to its bay: "The van's gone."</item>
/// </list>
/// Each step is saved (a flag): a Continue between the alarm and the silence has it going again, the door shut.
/// </summary>
public partial class Act1Walks
{
	public const string CheckInFlag = "act1_checkin_thought", AlarmFlag = "act1_car_alarm", VanGoneFlag = "act1_van_gone";
	private const string AlarmPath = "res://assets/audio/sfx/car_alarm_loop.wav";

	/// <summary>Trail metres of the cabin (where the trail passes it); 140 until the level's things are in.</summary>
	public float CabinS { get; private set; } = 140f;
	/// <summary>For tests: the thought, the alarm going (its loudness now), it stopped, the van gone and its thought.</summary>
	public bool CheckInThought { get; private set; }
	public bool AlarmOn => _alarm != null && IsInstanceValid(_alarm) && _alarm.Playing;
	public float AlarmDb => AlarmOn ? _alarm.VolumeDb : -80f;
	public bool AlarmStopped => Flag(PhotoBoard.PostFlag);
	public bool VanGone { get; private set; }
	public bool VanGoneThought { get; private set; }
	/// <summary>For tests: where the van stood (its bay), world.</summary>
	public Vector3 VanSpot { get; private set; }

	private AudioStreamPlayer3D _alarm;
	private Node3D _van;
	private double _postAt = -1;

	private void SetupAlarm()
	{
		if (_hub != null)
		{
			var c = _hub.GlobalPosition;
			CabinS = TrailS(new Vector2(c.X, c.Z), out _);
		}
		CheckInThought = Flag(CheckInFlag);
		var trailhead = GetTree().CurrentScene?.FindChild("Trailhead", true, false);
		_van = trailhead?.GetNodeOrNull<Node3D>("Car");
		if (_van != null) VanSpot = _van.GlobalPosition;
		if (Flag(VanGoneFlag)) TakeVan();
		if (Flag(AlarmFlag) && !Flag(PhotoBoard.PostFlag)) StartAlarm(true);
		GD.Print($"[story] Act 1: the cabin at {CabinS:0} m up the trail");
	}

	private void UpdateAlarm(PlayerController player, float s, float offT)
	{
		var pp = player.GlobalPosition;
		// the camera in hand, the title gone: the cabin first
		if (!CheckInThought && Flag(StoryManager.Flag.PickupTakenCamera) && !Flag(Cabin.HubDoorFlag)
			&& (UI.ChapterCard.Shown.Contains(1) || _clock > 30.0) && UI.ChapterCard.Current == null
			&& UI.Subtitle.Instance is { CurrentlyShowing: false } sub)
		{
			CheckInThought = true;
			SetStoryFlag(CheckInFlag);
			_ = sub.Show("I should check into the cabin.", 0.6f, 3.4f, 1.2f);
			GD.Print("[story] Act 1: \"I should check into the cabin.\"");
		}
		if (_builtStage == 1 && !Flag(AlarmFlag) && (PhotoLog.Instance?.RecordedCount ?? 0) > 0)
		{
			var gate = _trail.At(NorthGateAt, out _);
			bool atTrunk = new Vector2(pp.X, pp.Z).DistanceTo(gate) < 16f;
			bool doneAndOut = OverlookReached && ReachedFalls && s > CabinS + 50f && offT < 10f;
			if (atTrunk || doneAndOut)
			{
				SetStoryFlag(AlarmFlag);
				_hub?.ShutHubDoor();
				StartAlarm(false);
				GD.Print($"[story] Act 1: the van's alarm, far off ({(atTrunk ? "at the trunk" : "the walk done")})");
			}
		}
		if (AlarmOn)
		{
			// louder the nearer the van: faint from the trunk (about 235 m), loud by the cabin (about 140 m)
			float d = pp.DistanceTo(VanSpot);
			_alarm.VolumeDb = Mathf.Lerp(-30f, -8f, Mathf.Clamp((250f - d) / 120f, 0f, 1f));
			bool level = (s <= CabinS + 4f && offT < 30f) || _hub != null && pp.DistanceTo(_hub.GlobalPosition) < 22f;
			if (level) Silence();
		}
		if (_postAt >= 0 && _clock >= _postAt && UI.Subtitle.Instance is { } psub)
		{
			_postAt = -1;
			_ = psub.Show("Post Pictures.", 0.3f, 3.2f, 1.2f);
		}
		if (VanGone && !VanGoneThought && new Vector2(pp.X - VanSpot.X, pp.Z - VanSpot.Z).Length() < 7f && UI.Subtitle.Instance is { CurrentlyShowing: false } vsub)
		{
			VanGoneThought = true;
			_ = vsub.Show("The van's gone.", 0.5f, 3.0f, 1.2f);
			GD.Print("[story] Act 1: back at the bay - the van's gone");
		}
	}

	private void StartAlarm(bool resumed)
	{
		if (AlarmOn || !ResourceLoader.Exists(AlarmPath)) return;
		var stream = (AudioStreamWav)GD.Load<AudioStreamWav>(AlarmPath).Duplicate();
		stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
		stream.LoopBegin = 0;
		stream.LoopEnd = (int)(stream.GetLength() * stream.MixRate);
		// from the van's bay, panned to it; its loudness is ours to set (by the distance), not the falloff's
		_alarm = new AudioStreamPlayer3D
		{
			Name = "CarAlarm", Stream = stream, Bus = "Events", VolumeDb = -30f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
			MaxDistance = 0f, PanningStrength = 0.6f,
		};
		AddChild(_alarm);
		_alarm.GlobalPosition = VanSpot + Vector3.Up * 0.8f;
		_alarm.Play(resumed ? 0f : 0.2f);
	}

	/// <summary>Level with the cabin: the alarm cut off dead, the door slams open, the van gone.</summary>
	private void Silence()
	{
		if (_alarm != null && IsInstanceValid(_alarm)) _alarm.QueueFree();
		_alarm = null;
		SetStoryFlag(PhotoBoard.PostFlag);
		SetStoryFlag(VanGoneFlag);
		TakeVan();
		_hub?.SlamHubDoorOpen();
		_postAt = _clock + 1.4;
		GD.Print("[story] Act 1: the alarm stops dead, level with the cabin; the door slams open");
	}

	/// <summary>The van gone from its bay (hidden, its body out of the way; the opening's node stays).</summary>
	private void TakeVan()
	{
		VanGone = true;
		if (_van == null || !IsInstanceValid(_van) || !_van.Visible) return;
		_van.Visible = false;
		foreach (var n in _van.FindChildren("*", "CollisionShape3D", true, false))
			if (n is CollisionShape3D c) c.Disabled = true;
		GD.Print("[story] Act 1: the van is gone from the lot");
	}
}
