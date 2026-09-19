using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// A one-time scripted sound that fires when the player reaches a place (a
/// gunshot while crossing the bridge). The sound comes from a fixed compass
/// direction far away. Distance attenuation is off, so it arrives at full
/// weight but still pans to where it came from. It can startle the forest into
/// a temporary hush.
/// </summary>
public partial class SoundEvent : Node
{
	/// <summary>The node whose position triggers the event (e.g. the footbridge).</summary>
	[Export] public NodePath TargetPath;
	[Export] public float Radius = 3f;
	[Export] public string SoundPath = "";
	[Export] public string Bus = "Events";
	[Export] public float VolumeDb = 0f;
	/// <summary>World direction the sound comes from (y ignored).</summary>
	[Export] public Vector3 FromDirection = new(-0.8f, 0f, -0.6f);
	/// <summary>Seconds between the trigger and the sound (x..y random).</summary>
	[Export] public Vector2 DelaySeconds = new(0.3f, 0.9f);
	[Export] public float StartleStrength = 0.5f;
	[Export] public float StartleSeconds = 12f;

	public bool Fired { get; private set; }

	private Node3D _target;
	private Node3D _player;
	private AudioStreamPlayer3D _voice;
	private double _fireAt = -1;
	private double _clock;

	public override void _Ready()
	{
		_target = GetNodeOrNull<Node3D>(TargetPath);
		if (_target == null) GD.PushWarning($"SoundEvent {Name}: no target at {TargetPath}");
		_voice = new AudioStreamPlayer3D
		{
			Bus = Bus,
			AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
			MaxDistance = 0f,
			VolumeDb = VolumeDb,
			TopLevel = true,
		};
		if (ResourceLoader.Exists(SoundPath)) _voice.Stream = GD.Load<AudioStream>(SoundPath);
		else GD.PushWarning($"SoundEvent {Name}: missing {SoundPath}");
		AddChild(_voice);
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		if (Fired || _target == null) return;
		_player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (_player == null) return;

		if (_fireAt < 0)
		{
			var d = new Vector2(_player.GlobalPosition.X - _target.GlobalPosition.X, _player.GlobalPosition.Z - _target.GlobalPosition.Z);
			if (d.Length() <= Radius) _fireAt = _clock + GD.RandRange(DelaySeconds.X, DelaySeconds.Y);
			return;
		}
		if (_clock < _fireAt) return;

		Fired = true;
		GD.Print($"[event] {Name} fired");
		var dir = new Vector3(FromDirection.X, 0f, FromDirection.Z).Normalized();
		_voice.GlobalPosition = _player.GlobalPosition + dir * 60f + Vector3.Up * 4f;
		_voice.Play();
		if (StartleStrength > 0f) ForestAmbienceManager.Instance?.Startle(StartleStrength, StartleSeconds);
	}
}
