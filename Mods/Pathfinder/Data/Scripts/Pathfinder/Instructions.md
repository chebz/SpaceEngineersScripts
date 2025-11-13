# Pathfinder – Bi-directional Octree A* for Space Engineers

## Overview

Pathfinder is an alpha-stage navigation mod that embeds a bi-directional octree A* solver directly into
Space Engineers. It is designed to be driven by in-game scripts, delivering high-quality 3D paths that
respect voxel terrain, grids, and user-defined altitude constraints.

> ⚠️ **Alpha warning:** the mod is still in early development. APIs, behaviours, and performance
> characteristics may change without notice. Expect rough edges and use at your own risk.

## Setup Requirements

- Remote control block mounted so its forward axis matches the craft’s forward direction (Pathfinder assumes the remote’s forward vector is the desired heading).
- Thrust coverage in all six directions.
- At least one gyro for rotational control.

## Terminal Properties

- `PathfinderDestination` (`Vector3D?`): Set a world-space target. `null` clears the destination.
- `PathfinderPath` (`string`, read-only): Serialized master path returned by the solver (`(X,Y,Z)` segments).
- `DPRPath` (`string`, read-only): Serialized dynamic-refinement path (present when Dynamic Path Refinement is active).
- `PathfinderStatus` (`string`, read-only): Solver state (`NotStarted`, `Calculating`, `Ready`, `NoPath`).
- `CurrentWaypointIndex` (`int`): Current master waypoint index; scripts update this to inform Pathfinder which segment is active.
- `MinAltitude` / `MaxAltitude` (`double`): Optional altitude clamps (metres above sea level, only effective inside a planet gravity well).

Access these hidden properties through `IMyTerminalProperty<T>` inside your programmable blocks.

## Terminal Actions

- `RecomputePath` – Force a full recompute using the current destination.
- `ClearPath` – Cancel the current path and reset the solver.

Visible buttons labeled “Recompute Path” and “Clear Path” on the remote control are wired to the same actions.

## Dynamic Path Refinement (DPR)

When enabled (default), Pathfinder continuously refines the master path to avoid moving obstacles and cut
corners safely. The refined path is emitted through `DPRPath`. Disable DPR in `OctreeAStarSettings` if you
prefer the raw master path.

## Script Integration

For a working example, study the **[PathfinderPatrol](https://steamcommunity.com/sharedfiles/filedetails/?id=3604823270)** in-game script. It demonstrates how to:

- write GPS destinations into `PathfinderDestination`
- monitor `PathfinderStatus` and adjust behaviour
- use `CurrentWaypointIndex` to stay in sync with the dynamic refinement
- respond to success/failure callbacks via terminal actions

## Testing & Usage Tips

1. Ensure the remote control is powered, owned by you, and has control over the grid’s thrusters/gyros.
2. Set `PathfinderDestination` from a programmable block, e.g.:

   ```csharp
   var destProp = remote.GetProperty("PathfinderDestination") as ITerminalProperty<Vector3D?>;
   destProp?.SetValue(remote, new Vector3D(0, 0, 0));
   ```

3. Poll `PathfinderStatus` each tick; when it returns `Ready`, parse `PathfinderPath` or `DPRPath` and feed
   the waypoints to your navigation logic.
4. Call `RecomputePath` whenever you change destination or detect significant drift.
5. For global defaults, edit **PathfinderSettings.xml** in the world storage folder
   (`Storage/Pathfinder/PathfinderSettings.xml`) after Pathfinder has run once. The file controls
   `OctreeAStarSettings` such as octant debug drawing, node budgets, DPR parameters, and the
   `ShowPathfinderMessages` toggle (set it to `false` to silence HUD notifications).

## Feedback

Pathfinder’s internals are evolving quickly. If you hit serious issues, grab `Pathfinder.log` from
`Storage/Pathfinder/Pathfinder.log` in your world save and share it—diagnostics help stabilise the project.
Until then, keep backups of your craft, expect breakage, and fly safe. The Dude abides.

