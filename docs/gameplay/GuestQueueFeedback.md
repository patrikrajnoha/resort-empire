# Guest Queues and Satisfaction Bubbles

`GuestQueueVisualizer` (on `IslandContent/TouristVisuals`, next to `TouristVisualSpawner`)
draws waiting guests and occasional satisfaction bubbles during a wave. It is presentation
only: it reads `CurrentWaveSnapshot` and `NeedCompleted`, and it never calls a gameplay
command. Demand, service, queue order, satisfaction and rewards are unchanged.

## Waiting guests

- Every 0.25 s the visualizer reads each owned building's `WaitingQueueLength`. Guests in a
  service seat are never drawn as waiting.
- Up to `MaxVisibleGuests` (default 4) tourists stand in line per building. They reuse the
  roaming tourist prefab and sprites, and they render in the tourist container's layer.
- Each building has an authored queue line under `IslandContent/GuestQueueLines`
  (`ReceptionQueue`, `RestaurantQueue`, `BeachBarQueue`, `PoolQueue`, `BungalowsQueue`). Its
  child points run from the front of the queue to the back. Guests stand on the line, one
  every `guestSpacing` (30 px). To move a queue, move its points: they work like the
  `BarSeatEntry_*` markers.
- A spot is skipped when any part of the guest's body would overlap a building's opaque
  artwork (its sprite's tight mesh), or when it would crowd another building's line. Larger
  art at level 2 or 3, or a newly built Pool or Bungalows, pushes guests further down the line
  rather than drawing them behind the building. If the first clear spot is more than
  `maxFrontGap` (45 px) from the line's start, that building shows no guests rather than
  a queue somewhere unrelated. Lines are rebuilt each wave, and when buildings are bought
  or upgraded or the debug level changes.
- A building without an authored line uses the walkable route from its tourist entry marker
  toward the pier as a fallback.
- When the queue shrinks, the front guest walks into the entrance and fades out, and the
  others step forward. New arrivals walk in at the back.
- Guests show only while the roaming tourists are on the island. They freeze while the wave
  is paused and are cleared when the wave ends.

## Bubbles

| Icon | Shown when | Where |
| --- | --- | --- |
| Happy | A guest was served after waiting no longer than `PatienceGrace`, so their unit score is 100 | Above the building entrance |
| Waiting (hourglass) | Guests are queuing and the front guest is still within `PatienceGrace` | Above the front guest |
| Unhappy | The front guest has waited longer than `PatienceGrace`, so the satisfaction score is dropping | Above the front guest |
| Crowded | More guests are waiting than the line shows | Above the last guest in line |

`PatienceGrace` comes from the economy asset (`DemoEconomy.asset`, 10 s), so bubbles turn
negative at the same moment the existing satisfaction formula starts to reduce the score.
The front guest's wait is estimated from a visual copy of the queue's arrival order.

Pacing (all values are in `settings` in the Inspector):

- The first bubble over a new queue waits `FirstNegativeDelay` (1.5 s). A queue that clears
  quickly stays quiet. After that, the queue gets a bubble every `NegativeInterval` (5 s).
- A long, slow queue alternates the Unhappy and Crowded bubbles.
- Each building shows at most one bubble at a time. Happy bubbles at the same building are at
  least `HappyCooldown` (4 s) apart. The island shows at most `MaxConcurrentBubbles` (3).
- Bubbles use a nested canvas, so they draw above building art without re-sorting the
  island. Their art is in `Art/World/Island/SatisfactionBubbles/`, at most 256 px and
  compressed per platform.

## Manual check (Play Mode and Android)

1. Start a wave and leave a building without a Worker, or let a Sudden Rush build a queue.
2. Waiting guests line up outside that building, on the path and clear of its artwork. After
   about 1.5 s an hourglass bubble appears over the front guest, and after 10 s of waiting it
   becomes a sad face.
3. Assign a Worker. As the queue drains, the front guests walk into the building and the line
   shortens. Quickly served guests pop happy bubbles above the entrance.
4. Pause the wave: guests and bubbles freeze. Finish the wave: all of them disappear.
