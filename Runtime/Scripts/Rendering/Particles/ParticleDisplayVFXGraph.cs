using Seb.Fluid.Simulation;
using UnityEngine;
using UnityEngine.VFX;

namespace Seb.Fluid.Rendering
{
    public class ParticleDisplayVFXGraph : MonoBehaviour
    {
        public VisualEffect displayVFX;
        public FluidSim fluidSim;

        [Header("Buffers to Bind")]
        public bool bindPosition = true;
        public bool bindVelocity = true;
        public bool bindDensity = true;
        public bool bindFoam = true;

        private void LateUpdate()
        {
            if (bindPosition)
                displayVFX.SetGraphicsBuffer("PositionBuffer", fluidSim.positionBuffer);

            if (bindVelocity)
                displayVFX.SetGraphicsBuffer("VelocityBuffer", fluidSim.velocityBuffer);

            if (bindDensity)
                displayVFX.SetGraphicsBuffer("DensityBuffer", fluidSim.densityBuffer);

            if (bindFoam)
            {
                displayVFX.SetGraphicsBuffer("FoamBuffer", fluidSim.foamSortTargetBuffer);
                displayVFX.SetGraphicsBuffer("FoamCountBuffer", fluidSim.foamCountBuffer);
            }
        }
    }
}
