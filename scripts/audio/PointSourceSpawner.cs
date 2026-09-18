using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// Puts a looping 3D source on every node of a group (for example the
/// stream's "stream_audio_points"), so level designers only place markers.
/// </summary>
public partial class PointSourceSpawner : Node
{
	[Export] public string Group = "stream_audio_points";
	[Export] public string StreamPath = "res://assets/audio/ambient/stream_loop.wav";
	[Export] public string Bus = "Water";
	[Export] public float VolumeDb = -6f;
	[Export] public float UnitSize = 6f;
	[Export] public float MaxDistance = 60f;

	public int Spawned { get; private set; }

	public override void _Ready() => CallDeferred(MethodName.Spawn);

	private void Spawn()
	{
		if (!ResourceLoader.Exists(StreamPath)) { GD.PushWarning($"PointSourceSpawner: missing {StreamPath}"); return; }
		var stream = GD.Load<AudioStream>(StreamPath);
		foreach (var node in GetTree().GetNodesInGroup(Group))
		{
			if (node is not Node3D point) continue;
			var player = new AudioStreamPlayer3D
			{
				Stream = stream, Bus = Bus, UnitSize = UnitSize, MaxDistance = MaxDistance,
				AttenuationFilterCutoffHz = 6000f,
			};
			player.AddChild(new AmbienceLoop { BaseVolumeDb = VolumeDb });
			point.AddChild(player);
			Spawned++;
		}
	}
}
