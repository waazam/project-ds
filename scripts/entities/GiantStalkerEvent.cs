using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// Act 4's one-time set-piece: a minute or two into the storm walk from the camp, a
/// giant version of the stalker crosses the far horizon in the fog for about ten
/// seconds, never to be seen again. The compass keeps pointing at the cabin.
/// Never interactive, never close: this is scale and dread, not a fight.
///
/// It always happens before the player reaches the cabin (the door cannot be broken
/// until it has): if the player comes within <see cref="GuaranteeRadius"/> of the cabin
/// (group "cabin") before the fuse has burnt down, it fires there and then, and the giant
/// crosses beyond the cabin, in the direction the player is heading.
///
/// Restore: done for good once <see cref="StoryManager.Flag.GiantEventDone"/> is saved;
/// otherwise the fuse starts over whenever the (restored) storm is raging.
/// The fuse counts only unpaused storm time; the thuds are on the Unnatural bus.
/// </summary>
public partial class GiantStalkerEvent : Node
{
	[Export] public Vector2 DelaySeconds = new(60f, 120f);
	[Export] public Vector2 AutoTestDelaySeconds = new(4f, 7f);
	[Export] public float BodyScale = 26f;
	[Export] public float Distance = 190f;
	[Export] public float SweepWidth = 240f;
	[Export] public float DurationSeconds = 10f;
	[Export] public Vector2 ThudInterval = new(1.1f, 1.7f);
	/// <summary>Coming this close to the cabin (horizontal metres) fires it at once, from the camp on. 0 = off.</summary>
	[Export] public float GuaranteeRadius = 80f;
	/// <summary>How far past the cabin the giant crosses, when the guarantee fires it.</summary>
	[Export] public float BeyondCabin = 90f;

	private readonly RandomNumberGenerator _rng = new();
	private double _clock;
	private double _fireAt = -1;
	private bool _done;
	private Node3D _cabin;
	private Vector3? _centreOverride;

	/// <summary>For tests: whether the crossing has started (or the saved story is past it).</summary>
	public bool Done => _done;

	public override void _Ready() => Callable.From(Restore).CallDeferred();

	private void Restore()
	{
		if (StoryManager.Instance is { GiantEventDone: true }) { _done = true; SetProcess(false); }
	}

	public override void _Process(double delta)
	{
		if (_done) { SetProcess(false); return; }
		if (TryGuarantee()) return;
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
		SetProcess(false);
		Cutscene.Run(this, Cross);
	}

	/// <summary>The player is near the cabin and the giant has not crossed yet: it crosses now, beyond the cabin.</summary>
	private bool TryGuarantee()
	{
		if (GuaranteeRadius <= 0f || StoryManager.Instance is not { Current: >= Checkpoint.Act3DoorBoarded }) return false;
		if (_cabin == null || !IsInstanceValid(_cabin)) _cabin = GetTree().GetFirstNodeInGroup("cabin") as Node3D;
		var player = StoryBeat.Player(this);
		if (_cabin == null || player == null) return false;
		Vector3 d = _cabin.GlobalPosition - player.GlobalPosition; d.Y = 0;
		if (d.Length() > GuaranteeRadius) return false;
		Vector3 dir = d.LengthSquared() > 0.01f ? d.Normalized() : Vector3.Forward;
		_centreOverride = _cabin.GlobalPosition + dir * BeyondCabin;
		_done = true;
		SetProcess(false);
		GD.Print("[story] Act 4: the giant crosses now (near the cabin)");
		Cutscene.Run(this, Cross);
		return true;
	}

	private async Task Cross(CancellationToken ct)
	{
		var player = StoryBeat.Player(this);
		if (player == null) { StoryManager.Instance?.MarkGiantEventDone(); return; }
		var terrain = GroundSnap.FindTerrain(this);

		Vector3 fwd = -(GetViewport().GetCamera3D()?.GlobalBasis.Z ?? Vector3.Forward); fwd.Y = 0; fwd = fwd.Normalized();
		if (fwd.LengthSquared() < 0.01f) fwd = Vector3.Forward;
		Vector3 centre = player.GlobalPosition + fwd * Distance;
		if (_centreOverride is { } over)
		{
			// Beyond the cabin, across the player's way there.
			centre = over;
			fwd = centre - player.GlobalPosition; fwd.Y = 0; fwd = fwd.Normalized();
		}
		Vector3 right = fwd.Cross(Vector3.Up).Normalized();
		Vector3 start = centre - right * SweepWidth * 0.5f;
		Vector3 end = centre + right * SweepWidth * 0.5f;
		float Ground(Vector3 p) => terrain?.HeightAt(p.X, p.Z) ?? p.Y;
		start.Y = Ground(start); end.Y = Ground(end);

		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
		skin.SetShaderParameter("albedo", new Color(0.02f, 0.02f, 0.03f));
		skin.SetShaderParameter("face_tint", new Color(0.14f, 0.14f, 0.15f));
		skin.SetShaderParameter("visibility", 1f);
		skin.SetShaderParameter("wetness", 0.4f);
		var body = new StalkerBody { Name = "Act4Giant", Skin = skin, Size = BodyScale, SwaySeconds = 22f, SwayDegrees = 0.8f, HeadDriftDegrees = 1.5f };
		// Must be in the tree before GlobalPosition/LookAt: Godot can't resolve a global transform
		// for an orphan node, and silently no-ops (with a console warning) instead.
		Cutscene.SceneRoot(this).AddChild(body);
		try
		{
			body.GlobalPosition = start;
			body.LookAt(body.GlobalPosition + right, Vector3.Up);

			var steps = new List<AudioStream>();
			foreach (var name in new[] { "giant_step_01", "giant_step_02", "giant_step_03" })
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
				t += GetProcessDeltaTime();
				float u = (float)(t / DurationSeconds);
				Vector3 p = start.Lerp(end, u);
				p.Y = Ground(p);
				body.GlobalPosition = p;
				voice.GlobalPosition = p;
				if (t >= nextThud && steps.Count > 0)
				{
					nextThud = t + _rng.RandfRange(ThudInterval.X, ThudInterval.Y);
					voice.Stream = steps[_rng.RandiRange(0, steps.Count - 1)];
					voice.VolumeDb = _rng.RandfRange(4f, 8f);
					voice.PitchScale = _rng.RandfRange(0.9f, 1.1f);
					voice.Play();
				}
				await Cutscene.Frame(this, ct);
			}
		}
		finally
		{
			if (IsInstanceValid(body)) body.QueueFree();
		}
		StoryManager.Instance?.MarkGiantEventDone();
	}
}
