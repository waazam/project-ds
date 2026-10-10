using System.Collections.Generic;
using System.Linq;

namespace ProjectDS.Systems;

/// <summary>
/// The owner: sixty important pictures to take across the game, each scored for clarity, focus,
/// framing and zoom; the monsters are the hard ones, and worth the most. This is the list: every
/// subject's id (its <see cref="World.PhotoSubject.Id"/> and save flag), its pencil caption on the
/// print, how big it is (a radius in metres, for judging the zoom and the framing) and whether it's a
/// monster (its points count double). In the order the game meets them.
/// </summary>
public static class PhotoCatalog
{
	public sealed record Entry(string Id, string Caption, float Radius, bool Monster = false);

	public static readonly IReadOnlyList<Entry> All = new List<Entry>
	{
		// Act 1: the trail
		new("bird_red", "a red bird", 0.15f), new("bird_blue", "a blue bird", 0.15f), new("bird_purple", "a purple bird", 0.15f),
		new("bird_black", "a black bird", 0.15f), new("deer", "a deer", 0.8f), new("frog", "a frog", 0.06f),
		new("wildflowers", "wildflowers", 1.1f), new("mushrooms", "mushrooms", 0.2f), new("waterfall", "the waterfall", 3.5f),
		new("weird_stone", "a strange stone", 1.2f), new("stairs", "stairs?", 1.6f),
		// the Hollow
		new("camp", "R.H.'s camp", 2.2f), new("cabin", "the cabin", 4f), new("shed", "the shed", 2f), new("woodpile", "a woodpile", 1.2f),
		new("signpost", "a trail sign", 0.7f), new("tree_number", "a number on a tree", 0.6f), new("footbridge", "the footbridge", 3f),
		new("fallen_tree", "the fallen tree", 5f), new("friend", "him", 0.9f), new("bunker_door", "the bunker door", 1.6f),
		new("walkie", "the walkie", 0.12f), new("crt_room", "the screens", 1.5f), new("vine_door", "the vine door", 1.2f),
		new("the_lake", "the lake", 40f), new("forester_station", "the forester station", 5f),
		// the station, and down
		new("lobby", "the lobby", 3f), new("writing_on_wall", "the writing", 2f), new("red_room", "the red room", 2.2f),
		new("cryptex", "the cryptex", 0.25f), new("flooded_basement", "the basement", 3f), new("the_well", "the stairwell", 5f),
		new("hallway", "the long hallway", 2f), new("closet", "the closet", 1.3f), new("sewer", "the sewer", 2f),
		new("the_hole", "the hole", 1.4f), new("the_pit", "the pit", 8f), new("valve", "a valve", 0.45f),
		new("puzzle_box", "the puzzle box", 0.35f), new("fireplace", "the fireplace", 1f), new("secret_bookcase", "the bookcase", 1.3f),
		new("journal", "the journal", 0.15f), new("the_ring", "the ring", 3f), new("bricked_windows", "bricked windows", 4f),
		new("switches", "the switches", 0.45f), new("room_above", "the room above", 3f),
		// the church (Act 21)
		new("church_nave", "the church", 12f), new("font", "the font", 0.8f), new("altar", "the altar", 2f),
		new("crypt", "the crypt", 3f), new("church_door", "the great door", 2.5f),
		// the winter woods (Act 22)
		new("snowman", "a snowman", 1.2f), new("plow", "the snowplow", 3f), new("antler_tree", "antlers in a tree", 3f),
		new("ski_tracks", "tracks that stop", 1.5f), new("buried_arm", "an arm in the snow", 1.4f), new("frozen_deer", "a frozen deer", 1f),
		new("lodge_sign", "the lodge sign", 1f), new("the_lodge", "the ski lodge", 20f),
		// the things that hunt you: hard to get, worth double
		new("stalker", "IT", 1.2f, true), new("giant", "the giant", 12f, true), new("room_thing", "the thing in the room", 1f, true),
		new("bunker_creature", "it followed me", 1.2f, true), new("leviathan", "the lake thing", 9f, true), new("dead_eye", "the eye", 0.7f, true),
		new("shadowman", "the shadow man", 1.1f, true), new("pit_beast", "the beast in the pit", 7f, true),
		new("lake_creature", "something in the water", 2f, true),
		new("wendigo", "it wears antlers", 1.8f, true),
	};

	public const int Total = 69;   // sixty, and the winter woods' nine (Act 22)

	/// <summary>(2026-10-10) The album's notes: a line in pencil for each thing on the list the first time it's on film, in the
	/// photographer's own hand (dry, tired, more frightened as it goes). Shown on the album's last page.</summary>
	public static readonly IReadOnlyDictionary<string, string> Notes = new Dictionary<string, string>
	{
		["bird_red"] = "Cardinal, I think. First shot of the trip.",
		["bird_blue"] = "Blue jay. Loud little thing.",
		["bird_purple"] = "Never seen a purple one before.",
		["bird_black"] = "The black one came out wrong. Red eyes. Lens flare?",
		["deer"] = "Doe on the trail. She didn't run.",
		["frog"] = "Tiny frog. Took me five tries.",
		["wildflowers"] = "Wildflowers. Something normal.",
		["mushrooms"] = "Didn't touch them.",
		["waterfall"] = "The falls. Louder than I thought.",
		["weird_stone"] = "Carved? Too regular to be natural.",
		["stairs"] = "Stairs in the woods. Nobody builds stairs out here.",
		["camp"] = "His camp. Cold fire. Everything left behind.",
		["cabin"] = "The cabin. Was there smoke from the chimney?",
		["shed"] = "Shed. Padlocked from the inside.",
		["woodpile"] = "Someone split all this wood and never burned it.",
		["signpost"] = "The sign points the wrong way. I checked.",
		["tree_number"] = "Numbers on the trees. Somebody's code.",
		["footbridge"] = "Footbridge. It held.",
		["fallen_tree"] = "Came down right behind me. No wind.",
		["friend"] = "I don't want to talk about this one.",
		["bunker_door"] = "A bunker. Government, old. Dial lock.",
		["walkie"] = "A walkie. Still warm.",
		["crt_room"] = "The screens were showing me. From behind.",
		["vine_door"] = "Vines grown shut over a door. Then not.",
		["the_lake"] = "The lake. Too still.",
		["forester_station"] = "Forester station. Lights on. Nobody home.",
		["lobby"] = "The lobby. Dust on everything but the floor.",
		["writing_on_wall"] = "The writing. Same hand as the trees.",
		["red_room"] = "Red room. My eyes still hurt.",
		["cryptex"] = "Brass puzzle. Somebody wanted this hard to open.",
		["flooded_basement"] = "Water to the knees. Something moved in it.",
		["the_well"] = "The stairwell. I couldn't see the bottom.",
		["hallway"] = "The hallway kept going.",
		["closet"] = "Hid in here. Didn't help.",
		["sewer"] = "Sewer. Older than the station.",
		["the_hole"] = "The hole. I'm going down it.",
		["the_pit"] = "The pit. Full of blood. Full.",
		["valve"] = "Valves. Ten of them. Hands still shaking.",
		["puzzle_box"] = "A box that wants to be opened.",
		["fireplace"] = "The fireplace was warm. Nobody lit it.",
		["secret_bookcase"] = "Bookcase on a hinge. Of course.",
		["journal"] = "The journal. I read the last page twice.",
		["the_ring"] = "The ring. Round and round.",
		["bricked_windows"] = "Bricked up from the inside.",
		["switches"] = "Switches. Wrong order, everything hums.",
		["room_above"] = "The room above. It was waiting.",
		["church_nave"] = "A church, down here. Pews full of dust.",
		["font"] = "The font isn't water.",
		["altar"] = "Altar. Candles burned down to nothing.",
		["crypt"] = "The crypt. Names scratched out.",
		["church_door"] = "The great door. Snow on the other side.",
		["snowman"] = "Someone built a snowman. Recently.",
		["plow"] = "Plow. Engine cold, door open.",
		["antler_tree"] = "Antlers up in the branches. High.",
		["ski_tracks"] = "Ski tracks. They just stop.",
		["buried_arm"] = "An arm in the snow. I didn't dig.",
		["frozen_deer"] = "Frozen standing up.",
		["lodge_sign"] = "The lodge. Closed for the season.",
		["the_lodge"] = "The lodge. Every window dark.",
		["stalker"] = "IT. It was closer than I thought.",
		["giant"] = "Too big. The trees came up to its chest.",
		["room_thing"] = "The thing in the room. Still there when I looked again.",
		["bunker_creature"] = "It followed me. It's still following me.",
		["leviathan"] = "The lake has a bottom. Something lives on it.",
		["dead_eye"] = "The eye. It blinked after I took it.",
		["shadowman"] = "Him. Red light, green light.",
		["pit_beast"] = "The beast in the pit. Every eye was on me.",
		["lake_creature"] = "Something in the water. Under the boat.",
		["wendigo"] = "It wears antlers. It talks in voices.",
	};

	public static string NoteFor(string id) => id != null && Notes.TryGetValue(id, out var n) ? n : null;

	private static readonly Dictionary<string, Entry> _byId = All.ToDictionary(e => e.Id);

	public static Entry Get(string id) => id != null && _byId.TryGetValue(id, out var e) ? e : null;
	public static bool Contains(string id) => Get(id) != null;
}
