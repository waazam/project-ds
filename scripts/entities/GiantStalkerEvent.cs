using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// Act 4's one-time set-piece: a few minutes into the storm search, a giant
/// version of the stalker crosses the far horizon in the fog for about ten
/// seconds, never to be seen again, and the compass repoints to the cabin.
/// Never interactive, never close: this is scale and dread, not a fight.
/// </summary>
public partial class GiantStalkerEvent : Node
{
	[Export] public Vector2 DelaySeconds = new(180f, 300f);
	[Export] public Vector2 AutoTestDelaySeconds = new(4f, 7f);
	[Export] public float BodyScale = 26f;
	[Export] public float Distance = 190f;
	[Export] public float SweepWidth = 240f;
	[Export] public float DurationSeconds = 10f;
	[Export] public Vector2 ThudInterval = new(1.1f, 1.7f);

	private readonly RandomNumberGenerator _rng = new();
	private double _clock;
	private double _fireAt = -1;
	private bool _done;
	private Node3D _player;

	public override void _Process(double delta)
	{
		if (_done) return;
		if (StormController.Instance is not { Active: true }) return;
		_clock += delta;
		if (_fireAt < 0)
		{
			var delay = GameSettings.Instance.AutoTest ? AutoTestDelaySeconds : DelaySeconds;
			_fireAt = _clock + _rng.RandfRange(delay.X, delay.Y);
			return;
		}
		if (_clock < _fireAt) return;
		_done = true;
		_ = Run();
	}

	private async Task Run()
	{
		_player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (_player == null) { StoryManager.Instance?.MarkGiantEventDone(); return; }
		var terrain = GroundSnap.FindTerrain(this);

		Vector3 fwd = -(GetViewport().GetCamera3D()?.GlobalBasis.Z ?? Vector3.Forward); fwd.Y = 0; fwd = fwd.Normalized();
		if (fwd.LengthSquared() < 0.01f) fwd = Vector3.Forward;
		Vector3 right = fwd.Cross(Vector3.Up).Normalized();
		Vector3 centre = _player.GlobalPosition + fwd * Distance;
		Vector3 start = centre - right * SweepWidth * 0.5f;
		Vector3 end = centre + right * SweepWidth * 0.5f;
		float Ground(Vector3 p) => terrain?.HeightAt(p.X, p.Z) ?? p.Y;
		start.Y = Ground(start); end.Y = Ground(end);

		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
		skin.SetShaderParameter("albedo", new Color(0.02f, 0.02f, 0.03f));
		skin.SetShaderParameter("face_tint", new Color(0.14f, 0.14f, 0.15f));
		skin.SetShaderParameter("visibility", 1f);
		skin.SetShaderParameter("wetness", 0.4f);
		var body = new StalkerBody { Skin = skin, Size = BodyScale, SwaySeconds = 22f, SwayDegrees = 0.8f, HeadDriftDegrees = 1.5f };
		// Must be in the tree before GlobalPosition/LookAt: Godot can't resolve a global transform
		// for an orphan node, and silently no-ops (with a console warning) instead.
		GetTree().Root.AddChild(body);
		body.GlobalPosition = start;
		body.LookAt(body.GlobalPosition + right, Vector3.Up);

		var steps = new List<AudioStream>();
		foreach (var name in new[] { "branch_drop_01", "branch_drop_02" })
		{
			string p = $"res://assets/audio/sfx/{name}.wav";
			if (ResourceLoader.Exists(p)) steps.Add(GD.Load<AudioStream>(p));
		}
		var voice = new AudioStreamPlayer3D { Bus = "Unnatural", UnitSize = 40f, MaxDistance = 400f, TopLevel = true };
		body.AddChild(voice);

		GD.Print("[story] Act 4: the giant crosses the horizon");
		double t = 0, nextThud = 0;
		while (t < DurationSeconds)
		{
			double dt = GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / DurationSeconds);
			Vector3 p = start.Lerp(end, u);
			p.Y = Ground(p);
			body.GlobalPosition = p;
			if (t >= nextThud && steps.Count > 0)
			{
				nextThud = t + _rng.RandfRange(ThudInterval.X, ThudInterval.Y);
				voice.Stream = steps[_rng.RandiRange(0, steps.Count - 1)];
				voice.VolumeDb = _rng.RandfRange(6f, 10f);
				voice.PitchScale = _rng.RandfRange(0.45f, 0.55f);
				voice.Play();
			}
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		body.QueueFree();
		StoryManager.Instance?.MarkGiantEventDone();
	}
}
