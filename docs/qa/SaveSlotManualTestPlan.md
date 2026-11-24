# Save slots — manual verification

Prerequisite: instantiate `Assets/_ResortEmpire/Prefabs/UI/SaveManagerPopup.prefab` beneath an existing Canvas in a dedicated test scene, then call `Open()` on its `SaveSlotOverlayController`. Main Menu and Pause Menu integration are intentionally out of scope.

1. On Slot 1, select **New Game**. Confirm Slot 1 shows `ACTIVE`, `Saved Game`, Wave 1 and a timestamp.
2. Earn or change progress in Slot 1, then use **Save Active**.
3. Select empty Slot 2 and create a new game. Verify it starts at the initial resources and Wave 1.
4. Select Slot 1, choose **Load**, and verify its resources, buildings, workers, rewards and wave/checkpoint state match Step 2.
5. Load Slot 2 and verify Slot 1's progress was not changed.
6. Save Slot 2, restart the application, and load each slot. Verify both remain independent.
7. Attempt **New Game** on an occupied slot. Confirm that a second explicit New Game press is required before replacing it.
8. Select a non-active occupied slot and press **Delete** twice. Verify it becomes Empty.
9. Press Delete only once, then select another slot. Verify the original slot remains unchanged.
10. Attempt to delete the active slot. Verify the UI refuses and asks the player to load another slot first.
11. Replace a slot file under `Application.persistentDataPath/save-slots/` with invalid text. Verify it displays `Corrupted Save`, cannot be loaded, and other slots still load.
12. During a saveable active wave (with an Energy boost), save, restart, load the slot and verify the wave checkpoint, queues, workers and boost state restore.
13. During an ordinary active wave, attempt to switch slots. Verify the switch is rejected rather than discarding a non-saveable in-progress wave.
14. When a Main Menu is added, connect New Game to `OpenNewGameSlotSelection` and Continue to `OpenContinueSlotSelection`. When a Pause Menu is added, connect its Save action to `OpenPauseSaveSlotSelection` or `TrySaveProgress`.
15. Verify the existing UI and island look unchanged while the overlay is closed; it must never obscure gameplay until opened.
