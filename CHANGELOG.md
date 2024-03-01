
# Changelog #

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