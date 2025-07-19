using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Seb.Fluid.Simulation
{

	public class FluidSpawner : MonoBehaviour
	{
		public enum FluidSpawnerType { Cube, Ring, Sphere }
		public FluidSpawnerType spawnerType = FluidSpawnerType.Cube;

		[Header("Cube Spawner")]
		public SpawnRegion[] spawnRegions;

		[Header("Ring Spawner")]
		public float ringRadius = 1;

        [Header("Sphere Spawner")]
        public float sphereRadius = 1;

        [Header("Common")]
        public int particleCount = 10000;
        public float3 initialVel;
        public float jitterStrength;
        public bool showSpawnBounds;

		public SpawnData GetSpawnData()
		{
			List<float3> allPoints = new();
			List<float3> allVelocities = new();

			switch (spawnerType)
			{
				case FluidSpawnerType.Cube:

                    foreach (SpawnRegion region in spawnRegions)
                    {
                        int particlesPerAxis = region.CalculateParticleCountPerAxis(particleCount);
                        (float3[] cubePoints, float3[] cubeVelocities) = SpawnCube(particlesPerAxis, region.centre, Vector3.one * region.size);
                        allPoints.AddRange(cubePoints);
                        allVelocities.AddRange(cubeVelocities);
                    }

					break;

				case FluidSpawnerType.Ring:

                    (float3[] ringPoints, float3[] ringVelocities) = SpawnRing();
                    allPoints.AddRange(ringPoints);
                    allVelocities.AddRange(ringVelocities);

                    break;

                case FluidSpawnerType.Sphere:

                    (float3[] spherePoints, float3[] sphereVelocities) = SpawnSphere();
                    allPoints.AddRange(spherePoints);
                    allVelocities.AddRange(sphereVelocities);

                    break;
            }


			return new SpawnData() { points = allPoints.ToArray(), velocities = allVelocities.ToArray() };
		}

		(float3[] p, float3[] v) SpawnCube(int numPerAxis, Vector3 centre, Vector3 size)
		{
			int numPoints = numPerAxis * numPerAxis * numPerAxis;
			float3[] points = new float3[numPoints];
			float3[] velocities = new float3[numPoints];

			int i = 0;

			for (int x = 0; x < numPerAxis; x++)
			{
				for (int y = 0; y < numPerAxis; y++)
				{
					for (int z = 0; z < numPerAxis; z++)
					{
						float tx = x / (numPerAxis - 1f);
						float ty = y / (numPerAxis - 1f);
						float tz = z / (numPerAxis - 1f);

						float px = (tx - 0.5f) * size.x + centre.x;
						float py = (ty - 0.5f) * size.y + centre.y;
						float pz = (tz - 0.5f) * size.z + centre.z;
						float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
						points[i] = new float3(px, py, pz) + jitter;
						velocities[i] = initialVel;
						i++;
					}
				}
			}

			return (points, velocities);
		}

        (float3[] p, float3[] v) SpawnRing()
        {
            int numPoints = particleCount;
            float3[] points = new float3[numPoints];
            float3[] velocities = new float3[numPoints];

            int i = 0;

            for (int x = 0; x < numPoints; x++)
            {
				float angle = Mathf.PI * 2.0f * x / numPoints;

				float px = Mathf.Cos(angle) * ringRadius;
				float py = 0;
				float pz = Mathf.Sin(angle) * ringRadius;

                float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
                points[i] = new float3(px, py, pz) + jitter;
                velocities[i] = initialVel;
                i++;
            }

            return (points, velocities);
        }

        (float3[] p, float3[] v) SpawnSphere()
        {
            int numPoints = particleCount;
            float3[] points = new float3[numPoints];
            float3[] velocities = new float3[numPoints];

            int i = 0;

            for (int x = 0; x < numPoints; x++)
            {
                float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
                points[i] = (float3)UnityEngine.Random.onUnitSphere * sphereRadius + jitter;
                velocities[i] = initialVel;
                i++;
            }

            return (points, velocities);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
		{
			if (showSpawnBounds && !Application.isPlaying)
			{
				switch (spawnerType)
				{
					case FluidSpawnerType.Cube:

                        foreach (SpawnRegion region in spawnRegions)
                        {
                            Gizmos.color = region.debugDisplayCol;
                            Gizmos.DrawWireCube(region.centre, Vector3.one * region.size);
                        }

                        break;

					case FluidSpawnerType.Ring:

						Handles.color = Color.yellow;
						Handles.DrawWireDisc(transform.position, Vector3.up, ringRadius);

						break;
				}
			}
		}
#endif


		[System.Serializable]
		public struct SpawnRegion
		{
			public Vector3 centre;
			public float size;
			public Color debugDisplayCol;

			public float Volume => size * size * size;

			public int CalculateParticleCountPerAxis(int particleCount)
			{
				int targetParticleCount = particleCount;
				int particlesPerAxis = (int)Math.Cbrt(targetParticleCount);
				return particlesPerAxis;
			}
		}

		public struct SpawnData
		{
			public float3[] points;
			public float3[] velocities;
		}
	}
}