
# Changelog #

## 1.2.8 ##

- Bump version number to indicate v66 compatibility

## 1.2.7 ##

- Bump version number to indicate v65 compatibility

## 1.2.6 ##

- Bump version number to indicate v64 compatibility

## 1.2.5 ##

- Patch for v61

## 1.2.4 ##

- Patch for v60

## 1.2.3 ##

- Fix player scan searching over all GameObjects instead of just Lockers possibly causing performance issues in large levels

## 1.2.2 ##

- Patch for v56
- Improve performance by only getting doors on spawn not during chase

## 1.2.1 ##

- Patch for v55

## 1.2.0 ##

- Rewrote state management to be mounted on the game's EnemyAI system to improve stability and mod compatibility
    + Thanks [Xu Xiaolan](https://github.com/XuuXiao) for the initial rewrite and motivation as well as good feedback!

## 1.1.2 ##

- Add reaction to hit events
- Fix Locker pathfinding getting stuck
- Fix path calculation if off navmesh

## 1.1.1 ##

- Patch for v50

## 1.1.0 ##

- Add corpse deletion behavior in line with the consume animation
- Fix reactivating for another chase if not moved during last

## 1.0.0 ##

- Add spawn configuration options
- Add volume adjustment configuration option
- Adjust eye collider to make it trigger even if something is in direct line-of-sight
- Adjust navigation checks to make movement more reliable
- Add new post-chase mechanic to make chases more dynamic and dangerous
- Fix doors on custom levels not destroying causing infinite explosions
- Fix fear effect triggering on all players on death
- Slightly adjust scanner linecast to avoid thin obstacles
- Confirmed compatibility on the following custom interiors
    + Scoopy's Variety Mod: Dungeon
    + Scoopy's Variety Mod: Sewer
    + Dantors Mental Hospital
    + MoreInteriors: Bunker

## 0.13.1 ##

- Fix unkillable enemies getting stuck if hit by the locker

## 0.13.0 ##

- Add normal map to model
- Add more detail to the side of the model
- Add overshooting again but only on scan and touch interactions
- Add rotating in the direction of movement when turning corners

## 0.12.0 ##

- Add killing other enemies mid chase
- Add exploding if destroyed / killed
- Add screen shake to close encounter with player
- Add destruction of doors during chases
- Fix lights line of sight
- Fix chasing infinitely if stuck

## 0.11.2 ##

- Add video preview to README.md

## 0.11.1 ##

- Make kill trigger less forgiving by adding a distance check which should fix getting squished into walls but not being killed
- Reduce the height of enemy agent to make light detection more likely

## 0.11.0 ##

- Fix non host players not being killed
- Fix non host players holding lights not activating the enemy
- Implement enemy re-targeting the previous target if it's activated again

## 0.10.0 ##

- Switched to using nav mesh for traversal to better avoid obstacles
- Adjusted spawn curves to have highest probability at the start of a day

## 0.9.0 ##

- Held objects with lights will trigger while disregarding the viewing angle
    + Flashlights are still checked to have the 30 degree viewing angle
- Activation by scanning now disregards viewing angle
- Change chasing logic to overshoot initial target position

## 0.8.0 ##

- Fix map marker not being visible
- Improve flashlight detection logic and make normally held flashlights work too
- Implement player proximity detection and crouching to evade
- Change player kill animation
- Change colors and alignment of some textures
- Add bestiary entry

## 0.7.0 ##

- Correctly patch networking methods to fix RPCs

## 0.6.0 ##

- Implement reverse ping delay
- Implement map screen marker
- Implement scan node
- Improve reverse ping audio
- Reduce texture brightness and tweak materials
- Retopologize major parts of the Locker model
- Add fear effect if Locker closes or consumes near a player

## 0.5.0 ##

- Finalize Locker behavior and connect to in-game spawns
- Fix scan activation being relative to the Locker's forward transform

## 0.4.0 ##

- Add sound effects to individual states
- Add LockerAI to handle various states of player interaction
- Allow targeting of players holding flashlights in 45 degree field of view
- Add blood effect and consume state

## 0.3.0 ##

- Add sparks animation
- Rig and import model into Unity and set up basic animation controller

## 0.2.0 ##

- Create model in Blender

## 0.1.0 ##

- Initial project setup