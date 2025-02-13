using UnityEngine;

namespace Seb.Fluid.Simulation
{

    public class FluidCollider : MonoBehaviour
    {
        [Header("Simulation")]
        public string simulationID = "Main";

        [Header("Collider")]
        [Tooltip("Should particles collide inside the collider instead of outside.")] public bool invert = false;

        [Header("Debug")]
        public bool showGizmos = false;

        FluidSim fluidSim;

        #region Gizmos

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
            Gizmos.color = Color.yellow;

            if (invert)
            {
                Gizmos.DrawWireSphere(transform.position, transform.localScale.x * .5f);
            } else
            {
                Gizmos.DrawSphere(transform.position, transform.localScale.x * .5f);
            }
        }

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

        private void OnDisable()
        {
            if (fluidSim)
            {
                fluidSim.UnregisterCollider(this);
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
                fluidSim.RegisterCollider(this);
            }
            else
            {
                Debug.LogError("Fluid Collider " + name + " could not find a fluid simulation with ID " + simulationID);
            }
        }
    }
}
