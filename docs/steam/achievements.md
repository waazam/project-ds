# Achievements

Twenty, defined in `scripts/systems/Achievements.cs` (the list below is generated from it). The game keeps them in
`user://achievements.cfg` across every save slot. For Steam:

1. Add each one in Steamworks (App Admin, Stats & Achievements) with the **API name** below. Mark hidden ones as hidden.
2. Icons are 256x256 JPG, an earned and an unearned (greyscale) one each.
3. Hook up: in `Achievements.Unlock`, call `Steam.setAchievement(id)` then `Steam.storeStats()` (GodotSteam). Nothing else
   changes: the triggers are already in place.

A save from before achievements were in grants what it is already past, the first time it is loaded.

| API name | Name | Description | Hidden |
|---|---|---|---|
| `ACH_THE_STAIRS` | The Stairs | Climb the stairs in the woods. |  |
| `ACH_THE_CABIN` | Someone's Home | Find the cabin. |  |
| `ACH_THE_BRIDGE` | Don't Look Down | Cross the bridge. |  |
| `ACH_THE_BUNKER` | Below | Go down into the bunker. |  |
| `ACH_THE_GIANT` | Something Big | Stand before the giant. | yes |
| `ACH_THE_LAKE` | Still Water | Cross the lake. |  |
| `ACH_THE_STATION` | Signal | Leave the station. |  |
| `ACH_THE_CHURCH` | Sanctuary | Reach the church in the snow. |  |
| `ACH_THE_LODGE` | Checkout | Get out of the lodge. | yes |
| `ACH_THE_WENDIGO` | Dead Silent | End the hunt. | yes |
| `ACH_FINISHED` | Back to the Car | Finish the story. |  |
| `ACH_ENDING_1` | The Trunk | See the first ending. | yes |
| `ACH_ENDING_2` | Far Off | See the second ending. | yes |
| `ACH_ENDING_3` | Burning | See the third ending. | yes |
| `ACH_ENDING_4` | Hell Run | See the fourth ending. | yes |
| `ACH_ENDING_5` | The Perfect Run | See the fifth ending. | yes |
| `ACH_FIRST_PICTURE` | Say Cheese | Take a picture. |  |
| `ACH_TEN_SUBJECTS` | Field Notes | Photograph ten different subjects. |  |
| `ACH_ALL_SUBJECTS` | The Whole Album | Photograph every subject. |  |
| `ACH_FIVE_STARS` | Hold Still | Take a five-star picture. |  |

**Triggers.** The story's are checkpoints (`Achievements.AtCheckpoint`). The endings are granted when the ending has played
out to the credits (the `ending_N` flag at `GameFinished`). The camera's are checked every 2 s from the photo log.
