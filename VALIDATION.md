# Validation history

## Version 0.2.0 — room size

Tested against the same installed Steam build described below, using a private game copy and synthetic input. Progression writes and Steam achievement calls were blocked in the test harness.

- The local lobby creates a Room Size button; clicking cycles the saved setting, and left navigation decreases it.
- A Spacious Small ship loads into gameplay and spawns a player, with doors on every generated room.
- Ten seeds for each combination of Small/Medium/Large and Standard/Roomy/Spacious/Vast exercise the generation algorithm (120 combinations). These are generation checks, not 120 full playthroughs.
- Spacious averaged about 24–25 floor cells per regular room, compared with roughly 6 for Standard. Vast averaged about 44–45. Exact results depend on generation and seed.
- Reapplying the generation patch preserves dimensions rather than compounding the multiplier during retries.
- The stock entrance-floor comparator failed with larger rooms; the plugin now sorts local entrance floor cells using a valid distance comparison.
- Standard generation sometimes fails to find an entrance on the first attempt; the original game retries in that case. The harness permits up to ten attempts rather than incorrectly requiring every first attempt to succeed.

The completed run reported zero failed assertions; the concise results are in `smoke-rooms.txt`. The game's pre-existing material warning and retired web-endpoint 404s also appeared during the run.

Full-round balance, furniture accessibility across every seed, and sixteen-person physical-controller play with the larger rooms still need playtesting. Earlier player-count results below refer to the earlier build.

## Version 0.1.0

Tested on this computer against the installed Steam build 6616527 (v1.2.4b13), Unity 2018.2.21f1, Windows x64, BepInEx 5.4.23.5.

## Confirmed in the running game

- Sixteen Rewired game-player slots, with the reserved system player preserved.
- Sixteen complete lobby UI entries and character previews.
- Players 2–16 join through the normal local-player scan using simulated button input.
- The final player leaves and rejoins the lobby.
- Sixteen ready states and an offline room with capacity 16.
- Sixteen spawned local characters, each with a unique game identity and Rewired player.
- Sixteen camera targets.
- Separate movement input and measurable movement for every character. Other characters receive zero movement input during each isolated check.
- Four-player `GameValue` settings remain valid with sixteen characters.
- A return to the main menu followed by a fresh, correctly reset lobby.
- Controller ownership survives the lobby-to-game transition. All six assigned hardware devices retain enabled input maps.
- Player 16 picks up and drops a live bucket through the game's grab/drop methods.
- Player 16 opens the shop, with an expanded color array.
- The game-over flow accepts the expanded roster.
- Restarting the match spawns all sixteen players again.

The eight-player intermediate build also spawned eight local characters with unique identities and camera targets.

The completed sixteen-player run reported **zero failed assertions**. Its concise assertion log is included as `smoke-16.txt`.

A four-player regression run also completed the same lifecycle, movement, interaction, shop, game-over, restart, and fresh-lobby checks with **zero failed assertions**. Its assertion log is included as `smoke-4.txt`.

The installed Steam copy was then launched normally with only the release plugin. BepInEx loaded it successfully, all verified patches applied, and the main menu reported 16 logical players and six detected controllers. The installed plugin's SHA-256 matches the release below. No automated test harness is installed in the Steam copy.

## Physical-device observations

Rewired detected four XInput devices, a DualSense Wireless Controller, and a Sony DualShock 4. Windows also listed three wireless Xbox receiver controller channels and a wired Xbox One controller. This is consistent with the extra XInput device being an additional Xbox pad, rather than evidence of a PlayStation duplicate.

These checks verify enumeration and assignment. The automated movement tests inject values at the Rewired player API; they do **not** establish that sixteen physical controllers deliver independent input. A human couch session and disconnect/reconnect tests with the final controller mix remain necessary.

## Scope and existing messages

The mod is local-only above four players. Full-round balance, victory progression, Steam Input configurations, arbitrary Bluetooth mixes, and sixteen physical controllers are not certified by these smoke checks. Difficulty calculations outside `GameValue` continue using the actual player count.

The game emitted an existing material/texture-property message and HTTP 404 errors from its old online statistics endpoints during tests. These messages were also present during the initial inspection runs.

## Binary fingerprints

Original `Assembly-CSharp.dll` SHA-256:

`4D5E474A5AD7D6227DFB7CEF7D09FFA357A19CCF601D65503CFE01C68EAE934C`

Original `Rewired_Core.dll` SHA-256:

`9B28BB0BF216DB66E0ED424ED45872087B413D538D4EBF38BAA1C2EC9519C764`

The installed game assembly remains unchanged. The mod is loaded from `BepInEx/plugins/MorePlayers/OutOfSpace.MorePlayers.dll`.

Release plugin SHA-256:

`D115750D8080567C91708CDC2945395438ACA408ADCC3ED2775DE6248E8AF393`
