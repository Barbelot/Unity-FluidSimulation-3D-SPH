using UnityEngine;

namespace Seb.Fluid.Simulation
{

    public class FluidEffector : MonoBehaviour
    {
        public enum EffectorType { 
            Gravitational, 
            FarAttractor 
        }

        [Header("Simulation")]
        public string simulationID = "Main";

        [Header("Effector")]
        [Tooltip("Gravitational : Attraction increasing up to radius, then decreasing with distance.\n" +
            "FarAttractor : Attraction increasing with distance, from radius to infinity.")]
        public EffectorType effectorType;
        public float strength = 1;
        public float radius = 1;

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
            Gizmos.color = Color.magenta;

            Gizmos.DrawWireSphere(transform.position, radius);
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
                fluidSim.UnregisterEffector(this);
            }
        }

        void FindSimulation()
        {
            if (fluidSim)
                return;

            foreach(var sim in FindObjectsByType<FluidSim>(FindObjectsSortMode.None))
            {
                if(sim.id == simulationID)
                {
                    fluidSim = sim;

                    fluidSim.RegisterEffector(this);

                    return;
                }
            }

            Debug.LogError("Fluid Effector "+name+" could not find a fluid simulation with ID "+simulationID);
        }
    }
}
