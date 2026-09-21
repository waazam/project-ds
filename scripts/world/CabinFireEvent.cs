using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 7: once the clearing's business is done, the cabin the player left
/// behind is fully engulfed when they finally get back. A roaring column of
/// flame tears through the roofline, embers spit upward, thick black smoke
/// pours off in a heavy column, and the glow throws warm, unstable light for
/// a long way through the trees. The checkpoint fires the moment the player
/// comes close enough to see it.
/// </summary>
public partial class CabinFireEvent : Node
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float Radius = 30f;

	private struct Puff { public Vector3 Base; public float Phase; public float Speed; public float Scale; }
	private struct Ember { public Vector3 Base; public float Phase; public float Speed; }

	private Node3D _cabin;
	private PlayerController _player;
	private bool _fired;
	private readonly List<MeshInstance3D> _flames = new();
	private readonly List<StandardMaterial3D> _flameMats = new();
	private readonly List<(MeshInstance3D mesh, StandardMaterial3D mat, Puff puff)> _smoke = new();
	private readonly List<(MeshInstance3D mesh, StandardMaterial3D mat, Ember ember)> _embers = new();
	private OmniLight3D _glow;
	private OmniLight3D _flicker;
	private ShaderMaterial _postMat;
	private Color _postBaseTint;
	private double _clock;
	private double _nextGroan;
	private readonly RandomNumberGenerator _rng2 = new();

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
		// A dense ring of tongues along the whole roofline, plus a tall raging core above the peak.
		var flameSpots = new List<(Vector3 pos, float baseRadius)>
		{
			(new Vector3(-hw + 0.3f, roofY, 0), 0.34f), (new Vector3(hw - 0.3f, roofY, -0.5f), 0.34f),
			(new Vector3(0, roofY + 0.6f, hd - 0.4f), 0.38f), (new Vector3(0.6f, roofY, -hd + 0.5f), 0.32f),
			(new Vector3(-0.4f, 1.6f, hd - 0.1f), 0.3f), (new Vector3(0.5f, 1.6f, hd - 0.1f), 0.3f),
			(new Vector3(-1.2f, roofY + 0.2f, -hd + 0.6f), 0.3f), (new Vector3(1.1f, roofY + 0.3f, 0.8f), 0.3f),
			(new Vector3(0, roofY + 1.6f, 0), 0.5f), (new Vector3(0.15f, roofY + 2.3f, 0.1f), 0.4f), (new Vector3(-0.15f, roofY + 3.0f, -0.1f), 0.3f),
		};
		foreach (var (spot, radius) in flameSpots)
		{
			var mat = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.42f, 0.06f, 0.9f),
				EmissionEnabled = true,
				Emission = new Color(1f, 0.32f, 0.03f),
				EmissionEnergyMultiplier = 3.2f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			};
			var mesh = new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = radius, Height = radius * 3.2f, Material = mat, RadialSegments = 6, Rings = 4 },
				Position = spot,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			_cabin.AddChild(mesh);
			_flames.Add(mesh);
			_flameMats.Add(mat);
		}

		for (int i = 0; i < 18; i++)
		{
			var mat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.06f, 0.055f, 0.05f, 0f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			};
			var mesh = new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = 0.55f, Height = 1.1f, Material = mat, RadialSegments = 6, Rings = 4 },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			_cabin.AddChild(mesh);
			var puff = new Puff
			{
				Base = new Vector3(rng.RandfRange(-1.5f, 1.5f), 2.8f, rng.RandfRange(-1.7f, 1.7f)),
				Phase = rng.RandfRange(0f, 6f),
				Speed = rng.RandfRange(0.3f, 0.5f),
				Scale = rng.RandfRange(1.1f, 2.4f),
			};
			_smoke.Add((mesh, mat, puff));
		}

		for (int i = 0; i < 22; i++)
		{
			var mat = new StandardMaterial3D
			{
				AlbedoColor = Colors.White,
				EmissionEnabled = true,
				Emission = new Color(1f, 0.55f, 0.15f),
				EmissionEnergyMultiplier = 4f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			};
			var mesh = new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = 0.045f, Height = 0.09f, Material = mat, RadialSegments = 4, Rings = 2 },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			_cabin.AddChild(mesh);
			var ember = new Ember
			{
				Base = new Vector3(rng.RandfRange(-1.6f, 1.6f), roofY + rng.RandfRange(-0.2f, 0.4f), rng.RandfRange(-1.6f, 1.6f)),
				Phase = rng.RandfRange(0f, 6f),
				Speed = rng.RandfRange(0.9f, 1.6f),
			};
			_embers.Add((mesh, mat, ember));
		}

		_glow = new OmniLight3D { LightColor = new Color(1f, 0.48f, 0.12f), LightEnergy = 4.5f, OmniRange = 22f, Position = new Vector3(0, 2.2f, 0) };
		_cabin.AddChild(_glow);
		_flicker = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.2f), LightEnergy = 1.5f, OmniRange = 9f, Position = new Vector3(0, 3.4f, 0) };
		_cabin.AddChild(_flicker);

		var crackleBed = new AudioStreamPlayer3D { UnitSize = 7f, MaxDistance = 55f, Position = new Vector3(0, 1.4f, 0) };
		_cabin.AddChild(crackleBed);
		crackleBed.AddChild(new AmbienceLoop { StreamPath = "res://assets/audio/ambient/fire_crackle_loop.wav", BaseVolumeDb = 3f });

		if (GetTree().Root.FindChild("Screen", true, false) is ColorRect screen && screen.Material is ShaderMaterial sm)
		{
			_postMat = sm;
			_postBaseTint = (Color)_postMat.GetShaderParameter("shadow_tint");
		}
	}

	private void AnimateFire(double delta)
	{
		_clock += delta;
		for (int i = 0; i < _flameMats.Count; i++)
		{
			float flare = _rng2.Randf() < 0.01 ? 1.6f : 1f;
			float flick = 2.2f + Mathf.Sin((float)_clock * (5f + i) + i) * 0.7f + Mathf.Sin((float)_clock * (11f + i * 2)) * 0.4f;
			_flameMats[i].EmissionEnergyMultiplier = Mathf.Max(0.5f, flick) * flare;
			float s = 1f + Mathf.Sin((float)_clock * (6f + i) + i * 1.7f) * 0.16f;
			_flames[i].Scale = new Vector3(s, 1f + Mathf.Sin((float)_clock * (7f + i)) * 0.24f, s);
		}
		if (_glow != null) _glow.LightEnergy = 4.2f + Mathf.Sin((float)_clock * 9f) * 0.8f + Mathf.Sin((float)_clock * 3.3f) * 0.6f;
		if (_flicker != null) _flicker.LightEnergy = Mathf.Max(0f, 1.5f + Mathf.Sin((float)_clock * 17f) * 1.4f);

		for (int i = 0; i < _smoke.Count; i++)
		{
			var (mesh, mat, puff) = _smoke[i];
			float t = Mathf.PosMod((float)_clock * puff.Speed + puff.Phase, 7f) / 7f;
			mesh.Position = puff.Base + new Vector3(Mathf.Sin(puff.Phase + t * 4f) * 0.6f, t * 6.5f, Mathf.Cos(puff.Phase + t * 3f) * 0.5f);
			float scale = puff.Scale * (0.4f + t * 1.6f);
			mesh.Scale = new Vector3(scale, scale, scale);
			float alpha = (1f - t) * 0.6f;
			var c = mat.AlbedoColor; c.A = Mathf.Max(0f, alpha); mat.AlbedoColor = c;
		}

		for (int i = 0; i < _embers.Count; i++)
		{
			var (mesh, mat, ember) = _embers[i];
			float t = Mathf.PosMod((float)_clock * ember.Speed + ember.Phase, 3.5f) / 3.5f;
			mesh.Position = ember.Base + new Vector3(Mathf.Sin(ember.Phase * 3f + t * 9f) * 0.4f, t * 5.5f, Mathf.Cos(ember.Phase * 2f + t * 7f) * 0.4f);
			float alpha = 1f - t;
			var c = mat.AlbedoColor; c.A = Mathf.Max(0f, alpha * alpha); mat.AlbedoColor = c;
		}

		if (_clock >= _nextGroan)
		{
			_nextGroan = _clock + _rng2.RandfRange(5.0f, 11.0f);
			string path = $"res://assets/audio/sfx/trunk_creak_{_rng2.RandiRange(1, 3):00}.wav";
			if (ResourceLoader.Exists(path))
			{
				var groan = new AudioStreamPlayer3D
				{
					Stream = GD.Load<AudioStream>(path), UnitSize = 6f, MaxDistance = 45f,
					PitchScale = _rng2.RandfRange(0.55f, 0.7f), VolumeDb = _rng2.RandfRange(2f, 6f),
					Position = new Vector3(_rng2.RandfRange(-1.5f, 1.5f), 1.2f, _rng2.RandfRange(-1.5f, 1.5f)),
				};
				_cabin.AddChild(groan);
				groan.Finished += groan.QueueFree;
				groan.Play();
			}
		}

		if (_postMat != null && _player != null)
		{
			float d = new Vector2(_player.GlobalPosition.X - _cabin.GlobalPosition.X, _player.GlobalPosition.Z - _cabin.GlobalPosition.Z).Length();
			float near = 1f - Mathf.Clamp((d - Radius * 0.5f) / (Radius * 2.5f), 0f, 1f);
			var heat = new Color(0.16f, 0.03f, 0.0f);
			_postMat.SetShaderParameter("shadow_tint", _postBaseTint.Lerp(heat, near * 0.8f));
		}
	}
}
