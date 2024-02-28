
# Changelog

## 0.9.0

- Held objects with lights will trigger while disregarding the viewing angle
    + Flashlights are still checked to have the 30 degree viewing angle
- Activation by scanning now disregards viewing angle
- Change chasing logic to overshoot initial target position

## 0.8.0

- Fix map marker not being visible
- Improve flashlight detection logic and make normally held flashlights work too
- Implement player proximity detection and crouching to evade
- Change player kill animation
- Change colors and alignment of some textures
- Add bestiary entry

## 0.7.0

- Correctly patch networking methods to fix RPCs

## 0.6.0

- Implement reverse ping delay
- Implement map screen marker
- Implement scan node
- Improve reverse ping audio
- Reduce texture brightness and tweak materials
- Retopologize major parts of the Locker model
- Add fear effect if Locker closes or consumes near a player

## 0.5.0

- Finalize Locker behavior and connect to in-game spawns
- Fix scan activation being relative to the Locker's forward transform

## 0.4.0

- Add sound effects to individual states
- Add LockerAI to handle various states of player interaction
- Allow targeting of players holding flashlights in 45 degree field of view
- Add blood effect and consume state

## 0.3.0

- Add sparks animation
- Rig and import model into Unity and set up basic animation controller

## 0.2.0

- Create model in Blender

## 0.1.0

- Initial project setup