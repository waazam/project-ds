# Project DS: Story

The authoritative outline of the game's story, act by act. Implementation follows this document. If the game and this document disagree, this document wins; raise the difference with Dan before changing either.

---

## Act 1: Vacation
**Checkpoint 1**

Nothing should be overtly scaring the player yet except for the atmosphere in the game. We want the player to feel as if they are being watched, but not from the beginning of their exploration. It needs to gradually feed into the player's subconscious that they are being watched. Nothing overtly can be seen by the player yet, just implied. The audio and atmosphere carry the eeriness.

The real horror and scares only start happening after the player finds the stairs. Yet these scares still need to feel like they aren't real at first. The player should be thinking that they are just seeing things due to their imagination of how creepy the woods is, but stuff is really getting weird.

**Bird photo minigame.** The player has a camera and can take pictures of birds. There should only be 3 birds in the game, because this minigame becomes irrelevant once the player finds the stairs. Each bird resembles a cardinal, in red, blue and purple. There is a 4th bird, black with a red eye glow. Taking a picture of it triggers a distant but harsh, loud scream, and the birds all disappear. The camera disappears once the player takes their first step on the stairs and is pulled to the top of them. The pictures are entirely optional content, there to add realism: it's what someone could be doing on a trip to the woods before things kick off with the stairs.

## Act 2: Stare
Triggered when the player finds the stairs for the first time.

When the player gets within a radius of the stairs, all of the ambient audio from the forest goes dead silent. A low hum, barely audible, emanates from the stairs, growing the closer the player gets.

Stepping on the stairs makes the game scarier in a less subtle way than not stepping on them. Stepping on them is a trigger for more horror than the subtlety of just seeing them.

When the player steps on the first step:
- They lose control of their movement and ascend the stairs to the top in an unnatural, levitating way that feels very off and odd, then get control back at the top.
- Their view darkens completely by the middle of the ascent and only opens back up fully at the top, making the climb feel dreamlike.
- At the top they still can't move for 10 more seconds while the camera pans down, revealing they are looking straight down off the stairs.
- Then the screen goes all the way black. The player is teleported back to the footbridge (the cabin side) and wakes up slowly, face down, sight clearing as they lift their head. Their first taste of lost time.

After that the player can move freely again, which starts the quest: **get back to the cabin, it's getting too late.**

## Act 3: Homebase
**Checkpoint 3**

The player gets back to the cabin and walks onto the deck. The door has been completely boarded up (this is triggered when the player steps on the stairs). In a panic they notice a lantern and a compass on the front porch.

**Compass.** Equipping it points to the player's current objective, but it gradually goes haywire the closer you get to the stairs. It is an unobtrusive little UI element at the very top of the screen, similar to Skyrim's compass but a bit more translucent, with N, S, E, W shown accurately. It reads N, S, E, W normally until quests mess with it or the stairs get too near. When the objective changes, the marker must never snap or swing quickly; it sweeps to the new heading over a few seconds. Fast direction changes are disorienting and not fun.

**Leaving the safe zone.** After picking these up, the moment you step outside the cabin's adequately sized radius (the "safe zone"), the game goes dead silent, as if you were by the stairs. Rain starts to drizzle and low thunder rumbles, with lightning crackling across the sky, distant but bright.

**Lantern.** A radial light source. Its beam can be focused by holding right-click, like the other focus mechanic.

All we hear is the patter of the rain while we still search for our friend, guided by the compass. All of this is to get the player into place for the Act 4 set-piece.

## Act 4: Der Riese
After 3-5 minutes out in the woods, a set-piece moment triggers at random. A GIANT version of the stalker walks through the fog in the distance, to add scale and dread. You can hear the low thuds of the giant landing its stride. It walks through the map for 10 seconds in the distance, then never appears again. When the event ends, the compass points back to the house.

## Act 5: Let Me In
**Checkpoint 4**, when the player gets back to the cabin again.

This is the breaking-into-the-cabin puzzle. The compass points directly at the cabin from every angle as the player walks around it.

**Tools.** Tools lie around for the player to pick up. The player can carry any number of tools, and every item they carry works at any time without selecting it. A tool is "used up" when it's used correctly in the puzzle.
- **Axe:** very hard to find. With it, the player can cut the wood planks barricading the front door to get in.
- **Key:** found on the main path if the player travels far enough, and it opens a shed by the house. Inside the shed is a hammer.
- **Hammer:** you can pry the nails out of the front door. It's a little minigame, so it takes longer to get in than the axe.
- Other suggestions for getting inside the house are welcome.

**Inside.** The player finds their friend. He's the one who boarded it up. He is sitting in a chair at the table, dead, with his hand cut off and bandaged, but he bled out. On the table in front of him is a newel post bulb. When the player interacts with it, the compass points in a new direction (somewhere across the bridge).

When they get outside, the player says to himself, as text: *"He thrusts his fists against the posts"*.

## Act 6: Mourning
**Checkpoint 5**

**Dawn.** Stepping outside, it is dawn: a nice orange glow, no rain, fog.

**The forest turns menacing.** After the newel post quest triggers, the whole forest shifts into a more menacing tone. The trees are noticeably thicker and taller, casting more shadows. The deeper you go, the ground turns veiny: first something that could be worms, then an interconnected, pulsating veiny mesh on the ground.

**The clearing of stairs.** Once the player crosses the bridge, the compass points them to a small clearing with 15 small variations of staircases: spiral stairs, short stairs, wood stairs, concrete stairs, stairs sliced in half, burned stairs, all kinds of variations. This is a key horror moment.
- When the player steps into the clearing's radius, a voice says clearly, but as if from a dreamlike distance: *"Come up and see"*.
- After the voice, the newel post in the player's inventory flies away from their perspective and fuses onto one of the staircases, with a weird wooden creak and a purple flash of light at the connection point.
- The compass then points the player back to the cabin.

**Optional: climbing a clearing staircase.** If the player walks onto any of those stairs:
1. They get the same rise-to-the-top animation as the first time they stepped on the first stairs.
2. All the other 15 stairs disappear.
3. Everything goes really dark, so you can't see, for 5 seconds.
4. A spotlight hits you from above, revealing you are on top of the original stairs again. They seem to have even more steps than the first time, a much longer version of the original stairs.
5. Time of day shifts to night, as if you were on those stairs for hours and lost track of time.

Then the quest is: head back to the cabin again (still after that optional stairs segment).

## Act 7: Safety
If the player doesn't step on those stairs, the day gradually turns to night before they get back to the cabin for the fire reveal.

**The fire.** When the player gets back to the cabin, it is on fire. Smoke billows from the windows and flames lick the roof. **Checkpoint 6** triggers once you step into the radius where you can see your cabin burning.

**The whispers.** After the checkpoint, the player starts hearing *"come up and see"* whispered to them. The game should be properly terrifying by now: "come up and see" echoes through the forest in different vocal intonations, speeds and multiplicity.

**The bunker.** The compass now points to a new structure, a bunker deep in the woods that the player has likely not explored yet. If they find it earlier in the game, it is locked. When they reach it after the house fire, the bunker door is open.

## Act 8: The Bunker
**Checkpoint 7**, when entering the bunker.

Inside, the player is in a long hallway with overhead lights lining the way forward. The hallway is almost an optical illusion in the way it seems to stretch on forever.

**The lights.** The further the player walks down the hallway, the white LED lights start to flicker, then eventually transition to a deep, unsettling fluorescent red. If the player turns around after the red-light trigger, all the lights behind them back to the entrance change to red too, to add to the feeling of the player's sanity slipping.

**The end.** At the end of the hallway, the clean industrial bunker floor gives way to an overgrown, viny door that is barely cracked open, waiting for the player to push it open (E to interact) and walk inside.

## Act 9: Belly of the Beast
When the player goes through the overgrown door, they see a room filled with old CRT TVs stacked on top of each other, like that scene in The Matrix, showing mass surveillance of the woods.

**The screens.**
1. The compass points to one specific screen showing your cabin smouldering.
2. An interact prompt appears to turn it off. When the player presses E and turns off that TV, they all turn off.
3. After 5 seconds they all turn back on, showing one image in unison. It starts fuzzy, then slowly clears until it is all the stairs in the woods.

The compass then points to the entrance (of the bunker) after the TVs show the stairs.

## Act 10: Escaping the Bunker
When the player goes back through the overgrown door, the long straight hallway has turned into a maze. The player needs to navigate the maze to find the original entrance to the bunker.

**The choir.** *"Come and see"* is chanted in unison by a choir of unsteady, wavering voices. (Dan can record audio for this if it can't be made to work.)

**The maze.** It should be horrifying, with psychological horror themes happening to the player as they move through it: deep lighting colour changes, distorted realities, and feeling or hearing something following you, with only ever tiny glimpses of the creature.

**The walkie-talkie.** Once the player finally exits the maze through the bunker's original front entrance, they hear the static of a walkie-talkie. They follow the sound, find it, and press E to pick it up off the ground. Picking it up triggers **Checkpoint 8**.

## Act 11: The Third Man
**The radio.** Once you pick up the radio, a static-y voice begins to ask you questions for the rest of the game.
- *"Did you see them?"* the radio scoffs out.
- *"Who are you!"* the player says.
- Static. Radio silence follows.

The compass points to the main stairs, far away.

**The climb.** This time, when the player reaches the stairs, they are far bigger and take a lot longer to climb. They seem to go impossibly high up into the trees. When the player starts up the stairs, the hum is very loud. They walk straight up for minutes.

**The top.**
1. When they finally reach the top, the treetops are covered in thick fog.
2. The giant monster that lumbered through the woods in Act 4 stands at the top of the stairs, its head looking directly at the player.
3. The player doesn't recognise it until it opens its eyes as they reach the top step: the giant's great glowing red eyes stare at the player, piercing with their gaze.
4. The hum is so loud now, and the giant draws closer.
5. It reaches out a hand to touch you as you stand frozen in fear.

**Waking.** When the giant touches them, the player is teleported back into the field of the 14 stairs. It transitions to early morning with light fog, to show time passing, or lost time.
