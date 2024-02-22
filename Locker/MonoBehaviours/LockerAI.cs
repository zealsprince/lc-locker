using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Locker.MonoBehaviours
{
    public class LockerAI : MonoBehaviour
    {
        public enum LockerState : ushort
        {
            Dormant = 1,
            Active = 2,
            Chasing = 3,
            Closing = 4,
            Consuming = 5,
        }

        // Make definitions for the eye colors.
        private static readonly Color eyeColorDormant = Color.black;
        private static readonly Color eyeColorScan = Color.cyan;
        private static readonly Color eyeColorDetect = new Color(1, .4f, 0);
        private static readonly Color eyeColorChase = Color.red;

        // Store components for easy access.
        private AudioSource audioSource;
        private Animator animationController;
        private Material eyeMaterial;
        private Light internalLight;
        private List<Light> scrapeLights;

        // Store the scrape/chase VFXs so they can be toggled during chase.
        private VisualEffect[] visualEffects;

        // The two chase triggers we will work with.
        static VFXExposedProperty chaseVFXBeginTrigger;
        static VFXExposedProperty chaseVFXEndTrigger;

        // Define trigger names for our scrape VFX.
        private static readonly string chaseVFXBeginTriggerName = "BeginChase";
        private static readonly string chaseVFXEndTriggerName = "EndChase";

        // The two consume triggers we will work with.
        static VFXExposedProperty consumeVFXBeginTrigger;
        static VFXExposedProperty consumeVFXEndTrigger;

        // Define trigger names for our blood/consume VFX.
        private static readonly string consumeVFXBeginTriggerName = "BeginConsume";
        private static readonly string consumeVFXEndTriggerName = "EndConsume";

        private float Speed = 1f;

        // Store the last state for easier transitions.
        private LockerState lastState;

        // Store the current target player and last target chase location.
        private GameObject targetPlayer;
        private Vector3 targetLocation;

        // Store the current eye color and intensity.
        private Color currentEyeColor;
        private float currentEyeIntensity;

        // Store the current state publicly so I can switch it in the inspector.
        public LockerState state;

        void Start()
        {
            // Set the default eye color and intensity.
            currentEyeColor = eyeColorDormant;
            currentEyeIntensity = 0;

            // Assign our components.
            audioSource = GetComponent<AudioSource>();
            animationController = GetComponent<Animator>();

            // Get the materials in the child mesh.
            SkinnedMeshRenderer mesh = gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            foreach (Material material in mesh.materials)
            {
                // Get the eye material by name.
                if (material.name.ToLower().Contains("eye"))
                {
                    eyeMaterial = material;
                    eyeMaterial.SetColor("_EmissiveColor", currentEyeColor);
                    break;
                }
            }

            // Initialize our scrape lights array.
            scrapeLights = new List<Light> { };

            // Get the internal light component and scrape lights.
            Light[] lights = gameObject.GetComponentsInChildren<Light>();
            foreach (Light light in lights)
            {
                if (light.gameObject.name == "InternalLight")
                {
                    internalLight = light;
                }
                else // This is a scrape light.
                {
                    scrapeLights.Add(light);

                    // Make sure to disable these lights.
                    light.enabled = false;
                }
            }

            // Make sure to enable the Locker's main light but set the intensity to zero.
            internalLight.intensity = 0;
            internalLight.enabled = true;

            targetPlayer = gameObject;
            targetLocation = Vector3.zero;

            // Capture all visual effects that are a part of this.
            visualEffects = gameObject.GetComponentsInChildren<VisualEffect>();

            // Make sure to assign our constants.
            chaseVFXBeginTrigger.name = chaseVFXBeginTriggerName;
            chaseVFXEndTrigger.name = chaseVFXEndTriggerName;
            consumeVFXBeginTrigger.name = consumeVFXBeginTriggerName;
            consumeVFXEndTrigger.name = consumeVFXEndTriggerName;

            // Begin the initial state.
            SwitchState(LockerState.Dormant);
        }

        private void Update()
        {
            // Override in case I want to manually change states through the inspector.
            if (state != lastState)
            {
                SwitchState(state);
            }

            // Handle over-time state changes.
            switch (state)
            {
                case LockerState.Dormant:
                    currentEyeColor = Color.Lerp(currentEyeColor, eyeColorDormant, Time.deltaTime);
                    currentEyeIntensity = Mathf.Lerp(currentEyeIntensity, 0, Time.deltaTime);

                    // Fade out our lights entirely.
                    internalLight.intensity = Mathf.Lerp(
                        internalLight.intensity,
                        0,
                        Time.deltaTime * 8
                    );

                    break;

                case LockerState.Active:
                    currentEyeColor = Color.Lerp(currentEyeColor, eyeColorDetect, Time.deltaTime);
                    currentEyeIntensity = Mathf.Lerp(currentEyeIntensity, 100000, Time.deltaTime);

                    // Fade in our internal lights slowly.
                    internalLight.intensity = Mathf.Lerp(
                        internalLight.intensity,
                        40000,
                        Time.deltaTime / 4
                    );

                    break;

                case LockerState.Chasing:
                    currentEyeColor = Color.Lerp(currentEyeColor, eyeColorChase, Time.deltaTime);
                    currentEyeIntensity = Mathf.Lerp(
                        currentEyeIntensity,
                        500000,
                        Time.deltaTime * 2
                    );

                    // Fade in our internal lights quickly.
                    internalLight.intensity = Mathf.Lerp(
                        internalLight.intensity,
                        40000,
                        Time.deltaTime
                    );

                    // Fade in the scrape lights during the chase sparks. Additionally flicker them.
                    foreach (Light light in scrapeLights)
                    {
                        light.intensity = Mathf.Lerp(
                            light.intensity + Random.Range(-30000, 30000),
                            40000,
                            Time.deltaTime * 2
                        );
                    }

                    break;

                case LockerState.Closing:
                case LockerState.Consuming:
                    currentEyeColor = Color.Lerp(currentEyeColor, eyeColorChase, Time.deltaTime);
                    currentEyeIntensity = Mathf.Lerp(currentEyeIntensity, 0, Time.deltaTime);

                    // Fade out our lights.
                    internalLight.intensity = Mathf.Lerp(
                        internalLight.intensity,
                        0,
                        Time.deltaTime * 2
                    );

                    // Quickly fade out the scrape lights.
                    foreach (Light light in scrapeLights)
                    {
                        light.intensity = Mathf.Lerp(light.intensity, 0, Time.deltaTime * 2);
                    }

                    break;

                default:
                    break;
            }

            eyeMaterial.SetColor("_EmissiveColor", currentEyeColor * currentEyeIntensity);
        }

        private void SwitchState(LockerState state)
        {
            // Handle one-shot state changes.
            this.state = state;
            if (state != lastState)
            {
                switch (state)
                {
                    case LockerState.Dormant:
                        // Make sure to disable the scrape lights.
                        foreach (Light light in scrapeLights)
                        {
                            light.intensity = 0;
                            light.enabled = false;
                        }

                        animationController.SetTrigger("Deactivate");
                        animationController.SetBool("Chasing", false);

                        // End all our VFXs.
                        foreach (VisualEffect vfx in visualEffects)
                        {
                            vfx.SendEvent(consumeVFXEndTrigger.name);
                            vfx.SendEvent(chaseVFXEndTrigger.name);
                        }

                        break;

                    case LockerState.Active:
                        animationController.SetTrigger("Activate");

                        break;

                    case LockerState.Chasing:
                        // Play the chasing sound.
                        audioSource.pitch = Random.Range(1f, 1f);
                        audioSource.Play();

                        // Flip open the doors for the chase to begin.
                        animationController.SetTrigger("OpenDoors");

                        // Make sure to enable the scrape lights.
                        foreach (Light light in scrapeLights)
                        {
                            light.enabled = true;
                        }

                        // Begin the chasing.
                        animationController.SetTrigger("Chase");
                        animationController.SetBool("Chasing", true);

                        // Trigger the scrape particle effects.
                        foreach (VisualEffect vfx in visualEffects)
                        {
                            vfx.SendEvent(chaseVFXBeginTrigger.name);
                        }

                        break;

                    case LockerState.Closing:
                        animationController.SetBool("Chasing", false);
                        animationController.SetTrigger("CloseDoors");

                        // End the scrape particle effects. Begin the blood effects.
                        foreach (VisualEffect vfx in visualEffects)
                        {
                            vfx.SendEvent(chaseVFXEndTrigger.name);
                        }

                        break;

                    case LockerState.Consuming:
                        // Set the consuming VFX.
                        foreach (VisualEffect vfx in visualEffects)
                        {
                            vfx.SendEvent(consumeVFXBeginTrigger.name);
                        }

                        break;

                    default:
                        break;
                }

                lastState = state;
            }
        }

        public void PlayerScan(GameObject player, Camera camera) { }

        public void Activate(GameObject player)
        {
            // Activate the locker and begin the attack sequence.
            targetPlayer = player;
        }

        public void Chase(GameObject player)
        {
            // Chase to a player's last position.
            targetPlayer = player;
        }
    }
}
