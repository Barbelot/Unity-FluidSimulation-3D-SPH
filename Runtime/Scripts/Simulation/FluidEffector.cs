using NaughtyAttributes;
using UnityEditor;
using UnityEngine;

namespace Seb.Fluid.Simulation
{

    public class FluidEffector : MonoBehaviour
    {
        public enum EffectorType { 
            Gravitational, 
            FarAttractor,
            Vortex,
            Ring
        }

        [Header("Simulation")]
        public string simulationID = "Main";

        [Header("Effector")]
        [Tooltip("Gravitational : Attraction increasing up to radius, then decreasing with distance.\n" +
            "FarAttractor : Attraction increasing with distance, from radius to infinity.")]
        public EffectorType effectorType;
        [Space]
        public float radius = 1;
        public float attractionStrength = 1;
        [Tooltip("Exponent on the effect of distance to the effector.")]public float distancePower = 2;
        [Space]
        [ShowIf("IsVortex")]
        public float vortexStrength = 1;
        [ShowIf("IsVortex")]
        public float channelStrength = 0;

        [Header("Orientation")]
        public bool alignWithVelocity = false;

        [Header("Debug")]
        public bool showGizmos = false;

        public bool IsVortex => effectorType == EffectorType.Vortex;

        FluidSim fluidSim;
        Vector3 previousPosition;

        #region Gizmos

#if UNITY_EDITOR

        private void OnDrawGizmos()
        {
            if (showGizmos)
                DrawGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            DrawGizmos();
        }

        void DrawGizmos()
        {
            switch (effectorType)
            {
                case EffectorType.Vortex:
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(transform.position, radius);
                    Gizmos.DrawLine(transform.position - transform.forward * radius * 2, transform.position + transform.forward * radius * 2);
                    break;

                case EffectorType.Ring:
                    Handles.color = Color.yellow;
                    Handles.DrawWireDisc(transform.position, Vector3.up, radius);
                    break;

                default:
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(transform.position, radius);
                    break;

            }
        }

#endif

        #endregion

        private void OnEnable()
        {
            FindSimulation();
        }

        private void Update()
        {
            if(!fluidSim)
                FindSimulation();
        }

        private void LateUpdate()
        {
            if (alignWithVelocity)
                AlignWithVelocity();

            previousPosition = transform.position;
        }

        private void OnDisable()
        {
            if (fluidSim)
            {
                fluidSim.UnregisterEffector(this);
            }
        }

        void FindSimulation()
        {
            if (!fluidSim)
            {
                foreach (var sim in FindObjectsByType<FluidSim>(FindObjectsSortMode.None))
                {
                    if (sim.id == simulationID)
                    {
                        fluidSim = sim;
                        break;
                    }
                }
            }

            if (fluidSim)
            {
                fluidSim.RegisterEffector(this);
            } else
            {
                Debug.LogError("Fluid Effector " + name + " could not find a fluid simulation with ID " + simulationID);
            }
        }

        void AlignWithVelocity()
        {
            if(transform.position != previousPosition)
            {
                transform.forward = transform.position - previousPosition;
            }
        }
    }
}
