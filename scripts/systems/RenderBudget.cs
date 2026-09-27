using System.Threading.Tasks;
using Godot;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// The optimization pass (the owner's alpha polish): the Hollow's underground (the station, its rooms and
/// stairwell, the long hallway, the sewer, the pit, the library, the round room, the long stair and the
/// church) all sit within the camera's 400 m reach of each other, walled off by nothing but rock, and
/// nothing culls what's behind rock: every one of them was being drawn from every other. Once they've
/// built, this gives every mesh, particle system and multimesh under the station a visibility range (its
/// own reach plus a wide margin, so nothing ever pops in view of where it can be seen from), and every
/// light a distance fade (its shadow fading sooner). Nothing changes up close.
/// </summary>
public partial class RenderBudget : Node
{
	public int Meshes { get; private set; }
	public int Lights { get; private set; }
	public bool Done { get; private set; }

	public const float Margin = 140f, MinEnd = 180f, LightFade = 90f, ShadowFade = 45f;

	public override void _Ready() => _ = Apply();

	private async Task Apply()
	{
		for (int i = 0; i < 90 && !IsInsideTree(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		for (int i = 0; i < 300; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church?.FontKeyPickup != null) break;
		}
		for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (StationInterior.Instance is Node3D st) Walk(st);
		Done = true;
		GD.Print($"[render] budget: {Meshes} meshes ranged, {Lights} lights faded under the station");
	}

	private void Walk(Node n)
	{
		switch (n)
		{
			case GeometryInstance3D g when g.VisibilityRangeEnd <= 0f:
			{
				// the farthest this instance reaches from its own origin, whichever way the engine measures
				var box = g switch
				{
					MeshInstance3D mi when mi.Mesh != null => mi.Mesh.GetAabb(),
					MultiMeshInstance3D mm when mm.Multimesh != null => mm.Multimesh.GetAabb(),
					GpuParticles3D gp => gp.VisibilityAabb,
					_ => new Aabb(Vector3.Zero, Vector3.Zero),
				};
				float reach = 0f;
				for (int c = 0; c < 8; c++) reach = Mathf.Max(reach, box.GetEndpoint(c).Length());
				reach *= g.GlobalBasis.Scale.X;
				g.VisibilityRangeEnd = Mathf.Max(MinEnd, reach + Margin);
				g.VisibilityRangeEndMargin = 20f;
				g.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
				Meshes++;
				break;
			}
			case Light3D l when l is not DirectionalLight3D && !l.DistanceFadeEnabled:
				l.DistanceFadeEnabled = true;
				l.DistanceFadeBegin = LightFade;
				l.DistanceFadeLength = 20f;
				l.DistanceFadeShadow = ShadowFade;
				Lights++;
				break;
		}
		foreach (var c in n.GetChildren()) Walk(c);
	}
}
