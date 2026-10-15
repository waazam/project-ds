using System.Threading.Tasks;
using Godot;
using ProjectDS.World;
using ProjectDS.World.HallwayParts;

namespace ProjectDS.Entities;

/// <summary>`--kits` (2026-10-15): the small shared kits as modelled in Blender (tools/Blender/kits.py): the janitor's
/// tins, jugs, sprayers and bottles with their labels, the room clutter, a service door in its frame, the failing bulb,
/// the hurricane lamp, the woodpile and the wall crumbs, in even light. Shots go to test-output/creatures/kits_*.</summary>
public partial class CreaturePreview
{
	private async Task KitShots()
	{
		env.BackgroundColor = new Color(0.4f, 0.42f, 0.45f);
		env.AmbientLightEnergy = 0.9f;
		AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(40, 40) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.28f, 0.26f) } });
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), LightEnergy = 1.2f, ShadowEnabled = true });
		// the janitor's stock: each kind, three seeds
		var shelf = new MeshKit();
		var rng = new RandomNumberGenerator { Seed = 5 };
		var kinds = new[] { JanitorShelf.Kind.Gallon, JanitorShelf.Kind.Quart, JanitorShelf.Kind.Pint, JanitorShelf.Kind.Jug, JanitorShelf.Kind.Spray, JanitorShelf.Kind.Bottle };
		for (int i = 0; i < kinds.Length; i++)
			for (int j = 0; j < 3; j++)
				JanitorShelf.Add(shelf, kinds[i], new Vector3(-1.1f + i * 0.22f, 0, j * 0.24f), 0.3f * (j - 1), rng);
		JanitorShelf.Spilled(shelf, new Vector3(0.4f, 0, 0.9f), 0.6f, new Color(0.6f, 0.55f, 0.4f), rng);
		JanitorShelf.Bucket(shelf, new Vector3(0.9f, 0, 0.2f), 0.4f);
		shelf.CommitTo(this, "Shelf", false);
		// the clutter
		var card = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.48f, 0.34f), VertexColorUseAsAlbedo = true };
		var wood = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.32f, 0.2f), VertexColorUseAsAlbedo = true };
		var iron = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.35f, 0.36f), Metallic = 0.6f, Roughness = 0.5f, VertexColorUseAsAlbedo = true };
		var ck = new MeshKit();
		ck.Color = Colors.White;
		ClutterKit.Carton(card, new Vector3(0.44f, 0.32f, 0.34f)).Build(ck, new Transform3D(Basis.Identity, new Vector3(-1f, 0, -1.2f)));
		ClutterKit.Carton(card, new Vector3(0.3f, 0.22f, 0.26f), new Color(0.85f, 0.8f, 0.72f)).Build(ck, new Transform3D(new Basis(Vector3.Up, 0.3f), new Vector3(-1f, 0.33f, -1.2f)));
		ClutterKit.Bundle(card, card, new Vector3(0.36f, 0.16f, 0.27f), new Color(0.82f, 0.78f, 0.66f)).Build(ck, new Transform3D(Basis.Identity, new Vector3(-0.4f, 0, -1.2f)));
		ClutterKit.Bucket(iron).Build(ck, new Transform3D(Basis.Identity, new Vector3(0.1f, 0, -1.2f)));
		ClutterKit.Broom(wood, card).Build(ck, new Transform3D(new Basis(Vector3.Right, -0.2f), new Vector3(0.5f, 0, -1.3f)));
		ClutterKit.Frame(wood, card, 0.55f, 0.72f).Build(ck, new Transform3D(new Basis(Vector3.Right, -0.15f), new Vector3(0.95f, 0, -1.3f)));
		ClutterKit.Board(wood, 1.7f).Build(ck, new Transform3D(new Basis(Vector3.Right, -0.2f), new Vector3(1.4f, 0, -1.4f)));
		ck.CommitTo(this, "Clutter", false);
		// a service door in its frame, ajar
		var leaf = new MeshKit(); leaf.Mat(ServiceDoor.LeafMat);
		var hw = new MeshKit(); hw.Mat(ServiceDoor.HardwareMat);
		leaf.Xf = hw.Xf = new Transform3D(new Basis(Vector3.Up, -0.5f), new Vector3(2.5f, 0, -1.5f));
		ServiceDoor.Leaf(leaf, hw, 0.976f, 2.12f, 0.05f);
		leaf.CommitTo(this, "Leaf", false);
		hw.CommitTo(this, "Hardware", false);
		var frame = new MeshKit(); frame.Mat(ServiceDoor.FrameMat);
		frame.Xf = new Transform3D(Basis.Identity, new Vector3(3f, 0, -1.6f));
		ServiceDoor.Frame(frame, Vector3.Zero, 1.0f, 2.15f, -0.1f, 0.1f, 0.014f, 0.055f, 0.0f);
		frame.CommitTo(this, "Frame", false);
		// the bulb, the hurricane lamp
		AddChild(new FailingBulb { Position = new Vector3(-2f, 1.4f, 0), Radius = 0.06f, Energy = 0.5f });
		var lf = new MeshKit(); var lit = new MeshKit(); var dark = new MeshKit();
		var flame = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.7f, 0.3f) };
		HollowProps.HangingLamp(lf, lit, dark, new Vector3(-2.6f, 1.0f, 0), new Vector3(-2.6f, 1.8f, 0), iron, BuildingTextures.LitGlassMat, BuildingTextures.GlassMat, flame);
		lf.CommitTo(this, "Lamp", false);
		lit.CommitTo(this, "LampGlass", false);
		// the woodpile and the crumbs
		AddChild(new Woodpile { Position = new Vector3(-1.5f, 0, 2.6f) });
		DebrisKit.Scatter(this, new RandomNumberGenerator { Seed = 3 }, 1f, 3f, 1.5f, 3.5f, 0f, new Color(0.7f, 0.68f, 0.64f), 40, 0);
		await Seconds(0.8);
		await Shot("kits_janitor", new Vector3(-0.3f, 0.7f, 1.6f), new Vector3(-0.4f, 0.1f, 0.2f));
		await Shot("kits_clutter", new Vector3(0.2f, 1.2f, 1.2f), new Vector3(0.2f, 0.4f, -1.3f));
		await Shot("kits_door", new Vector3(2.2f, 1.4f, 1.6f), new Vector3(2.8f, 1.1f, -1.5f));
		await Shot("kits_lamps", new Vector3(-2.3f, 1.3f, 1.0f), new Vector3(-2.3f, 1.2f, 0));
		await Shot("kits_woodpile", new Vector3(0.5f, 1.4f, 5.2f), new Vector3(0.2f, 0.2f, 2.6f));
	}
}
