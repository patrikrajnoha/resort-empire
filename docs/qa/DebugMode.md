# Debug Mode

In the Unity Editor or a desktop Development Build, press F9 to toggle the panel and Escape to close it. Enable Development Build in Unity Build Settings. The panel is instantiated under the existing GameUI canvas and is hidden until opened.

In gameplay, pressing R replaces the currently loaded active slot with a fresh game and reloads the gameplay scene. R is ignored while the debug panel is open. The reset requires the gameplay scene in Build Settings and a verified active slot; it reports an error if those conditions or the save fail. This is a developer-only action and overwrites that slot's progress.

F1, F2 and F3 set every owned building to level 1, 2 or 3 between waves. The Building Info panel can stay open and updates immediately. Leave text fields and close other gameplay dialogs before using the shortcuts. Level changes are free and update capacity, building art and building details.

| Command | Behavior |
| --- | --- |
| `help [page]` | List commands and descriptions, three per page. |
| `add_coins 100`, `add_energy 50` | Add a positive integer amount within the resource limit. |
| `set_buildings_level <1-3>` | Set the level of every owned building for free. |
| `set_building_level <building_id> <1-3>` | Set one owned building. IDs are Reception, Restaurant, BeachBar, Pool and Bungalows. |
| `unlock_all` | Construct eligible buildings for free while awaiting a wave. Pool and Bungalows still require their normal wave milestones. Each receives its usual worker. |
| `wave_status`, `game_status` | Inspect the wave and checkpoint. Game status also shows resources, buildings, workers and the verified active slot when available. |
| `next_wave` | Start the next wave. Any outstanding reward must be claimed manually first. |
| `complete_wave` | Simulate the current unpaused wave through arrivals, service and resolution. The reward remains unclaimed. |
| `set_wave <number>` | Unavailable until validated wave presets exist. |
| `load_preset <name>` | Requires a configured shared preset adapter. |
| `set_workers_max` | Report the prototype staffing limit. All existing workers are already assigned, with one owned building vacant. |
| `reset_workers` | Restore default assignments between waves, preserving worker IDs and headcount. |
| `game_speed <1-3>` | Set the current wave simulation speed. Use 1 for normal speed. Pause and application suspension still stop simulation. |

Resource, building and worker changes use the current gameplay runtime and persist at a normal save point. Wave commands use the normal transition and checkpoint rules. The active slot selection stays in Save Manager.

To extend the registry, call `Register` in `DebugCommandManager` with the command name, parameter syntax, description and handler. Help entries come from the registry.

In a test slot, verify F1–F3 change owned building art and capacity, refresh an open Building Info panel, leave locked buildings untouched, and are ignored while typing or while the debug or save overlay is open. Verify F9 still blocks background clicks, R resets the active slot, and F9 still opens after R. Try invalid values, complete a wave twice, verify rewards can be claimed only once, test speed while paused, and check the shortcuts in a non-development build.
