using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 7: once the clearing's business is done, the cabin the player left
/// behind is on fire when they finally get back. Flame tongues flicker along
/// the roofline, smoke puffs drift and fade, warm light spills out — the
/// checkpoint fires the moment the player comes close enough to see it.
/// </summary>
public partial class CabinFireEvent : Node
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float Radius = 30f;

	private struct Puff { public Vector3 Base; public float Phase; public float Speed; public float Scale; }

	private Node3D _cabin;
	private PlayerController _player;
	private bool _fired;
	private readonly List<MeshInstance3D> _flames = new();
	private readonly List<StandardMaterial3D> _flameMats = new();
	private readonly List<(MeshInstance3D mesh, StandardMaterial3D mat, Puff puff)> _smoke = new();
	private OmniLight3D _glow;
	private double _clock;

	public override void _Ready() => _cabin = GetNode<Node3D>(CabinPath);

	public override void _Process(double delta)
	{
		if (!_fired)
		{
			if (StoryManager.Instance is not { ClearingVoiceHeard: true }) return;
			_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
			if (_player == null) return;
			float d = new Vector2(_player.GlobalPosition.X - _cabin.GlobalPosition.X, _player.GlobalPosition.Z - _cabin.GlobalPosition.Z).Length();
			if (d > Radius) return;
			_fired = true;
			Ignite();
			StoryManager.Instance.ReachCheckpoint(Checkpoint.Act7CabinBurning, _player.GlobalPosition, _player.CameraRig.Yaw);
			GD.Print("[story] Act 7: the cabin is burning");
			return;
		}
		AnimateFire(delta);
	}

	private void Ignite()
	{
		var rng = new RandomNumberGenerator { Seed = 55 };
		float hw = 2.1f, hd = 2.6f, roofY = 2.5f;
		var flameSpots = new[]
		{
			new Vector3(-hw + 0.3f, roofY, 0), new Vector3(hw - 0.3f, roofY, -0.5f),
			new Vector3(0, roofY + 0.6f, hd - 0.4f), new Vector3(0.6f, roofY, -hd + 0.5f),
			new Vector3(-0.4f, 1.6f, hd - 0.1f), new Vector3(0.5f, 1.6f, hd - 0.1f),
		};
		foreach (var spot in flameSpots)
		{
			var mat = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.4f, 0.08f, 0.85f),
				EmissionEnabled = true,
				Emission = new Color(1f, 0.35f, 0.05f),
				EmissionEnergyMultiplier = 2.2f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			};
			var mesh = new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = 0.28f, Height = 0.9f, Material = mat, RadialSegments = 6, Rings = 4 },
				Position = spot,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			_cabin.AddChild(mesh);
			_flames.Add(mesh);
			_flameMats.Add(mat);
		}

		for (int i = 0; i < 10; i++)
		{
			var mat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.3f, 0.29f, 0.28f, 0f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			};
			var mesh = new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, Material = mat, RadialSegments = 6, Rings = 4 },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			_cabin.AddChild(mesh);
			var puff = new Puff
			{
				Base = new Vector3(rng.RandfRange(-1.3f, 1.3f), 2.7f, rng.RandfRange(-1.5f, 1.5f)),
				Phase = rng.RandfRange(0f, 6f),
				Speed = rng.RandfRange(0.35f, 0.55f),
				Scale = rng.RandfRange(0.8f, 1.6f),
			};
			_smoke.Add((mesh, mat, puff));
		}

		_glow = new OmniLight3D { LightColor = new Color(1f, 0.5f, 0.15f), LightEnergy = 2.5f, OmniRange = 12f, Position = new Vector3(0, 2f, 0) };
		_cabin.AddChild(_glow);

		var crackleBed = new AudioStreamPlayer3D { UnitSize = 6f, MaxDistance = 40f, Position = new Vector3(0, 1.4f, 0) };
		_cabin.AddChild(crackleBed);
		crackleBed.AddChild(new AmbienceLoop { StreamPath = "res://assets/audio/ambient/fire_crackle_loop.wav", BaseVolumeDb = -4f });
	}

	private void AnimateFire(double delta)
	{
		_clock += delta;
		for (int i = 0; i < _flameMats.Count; i++)
		{
			float flick = 1.6f + Mathf.Sin((float)_clock * (5f + i) + i) * 0.5f + Mathf.Sin((float)_clock * (11f + i * 2)) * 0.25f;
			_flameMats[i].EmissionEnergyMultiplier = Mathf.Max(0.4f, flick);
			float s = 1f + Mathf.Sin((float)_clock * (6f + i) + i * 1.7f) * 0.12f;
			_flames[i].Scale = new Vector3(s, 1f + Mathf.Sin((float)_clock * (7f + i)) * 0.18f, s);
		}
		if (_glow != null) _glow.LightEnergy = 2.2f + Mathf.Sin((float)_clock * 9f) * 0.4f + Mathf.Sin((float)_clock * 3.3f) * 0.3f;

		for (int i = 0; i < _smoke.Count; i++)
		{
			var (mesh, mat, puff) = _smoke[i];
			float t = Mathf.PosMod((float)_clock * puff.Speed + puff.Phase, 6f) / 6f;
			mesh.Position = puff.Base + new Vector3(Mathf.Sin(puff.Phase + t * 4f) * 0.5f, t * 5f, Mathf.Cos(puff.Phase + t * 3f) * 0.4f);
			float scale = puff.Scale * (0.4f + t * 1.4f);
			mesh.Scale = new Vector3(scale, scale, scale);
			float alpha = (1f - t) * 0.4f;
			var c = mat.AlbedoColor; c.A = Mathf.Max(0f, alpha); mat.AlbedoColor = c;
		}
	}
}
