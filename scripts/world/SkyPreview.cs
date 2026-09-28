using System.Threading.Tasks;
using Godot;

namespace ProjectDS.World;

/// <summary>Dev harness for sky_preview.tscn: renders the dusk sky shader alone from a few directions into
/// test-output/sky/ (to check it for seams and banding), then quits. Not used by the game.</summary>
public partial class SkyPreview : Node3D
{
	public override void _Ready() => _ = Run();

	private async Task Run()
	{
		string outDir = ProjectSettings.GlobalizePath("res://test-output/sky");
		DirAccess.MakeDirRecursiveAbsolute(outDir);
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/sky_dusk.gdshader") };
		var env = new Environment { BackgroundMode = Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = mat }, TonemapMode = Environment.ToneMapper.Filmic };
		AddChild(new WorldEnvironment { Environment = env });
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-8, 160, 0) });
		var cam = new Camera3D { Fov = 75f, Current = true };
		AddChild(cam);
		(string name, Vector3 rot)[] views = { ("horizon", new Vector3(5, 0, 0)), ("up", new Vector3(45, 40, 0)), ("zenith", new Vector3(80, 0, 0)), ("toward_sun", new Vector3(10, 160, 0)) };
		foreach (var (name, rot) in views)
		{
			cam.RotationDegrees = rot;
			for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			GetViewport().GetTexture().GetImage().SavePng($"{outDir}/{name}.png");
		}
		GetTree().Quit();
	}
}
