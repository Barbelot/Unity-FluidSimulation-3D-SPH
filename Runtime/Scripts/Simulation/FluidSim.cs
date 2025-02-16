using System;
using UnityEngine;
using Seb.GPUSorting;
using Unity.Mathematics;
using System.Collections.Generic;
using Seb.Helpers;
using static Seb.Helpers.ComputeHelper;
using UnityEngine.VFX;

namespace Seb.Fluid.Simulation
{
	public class FluidSim : MonoBehaviour
	{
		public event Action<FluidSim> SimulationInitCompleted;

		[Header("ID")]
		public string id = "Main";

		[Header("Time Step")] 
		public float timeScale = 1;
		public float maxTimestepFPS = 60; // if time-step dips lower than this fps, simulation will run slower (set to 0 to disable)
		public int iterationsPerFrame = 3;

		[Header("Simulation Settings")]
		public float smoothingRadius = 0.2f;
		public float targetDensity = 630;
		public float pressureMultiplier = 288;
		public float nearPressureMultiplier = 2.15f;
		public float viscosityStrength = 0;
		[Range(0, 1)] public float collisionDamping = 0.95f;
		public float maxVelocity = 100;

		[Header("External Forces")]
        public float gravity = -10;
		public float noiseStrength = 0;
		public float noiseScale = .2f;
		public Vector3 noiseSpeed = Vector3.up;
		[Range(1, 8)] public int noiseOctaves = 1;

        [Header("Foam Settings")] public bool foamActive;
		public int maxFoamParticleCount = 1000;
		public float trappedAirSpawnRate = 70;
		public float spawnRateFadeInTime = 0.75f;
		public float spawnRateFadeStartTime = 0.1f;
		public Vector2 trappedAirVelocityMinMax = new(5, 25);
		public Vector2 foamKineticEnergyMinMax = new(15, 80);
		public float bubbleBuoyancy = 1.4f;
		public int sprayClassifyMaxNeighbours = 5;
		public int bubbleClassifyMinNeighbours = 15;
		public float bubbleScale = 0.5f;
		public float bubbleChangeScaleSpeed = 7;

		[Header("Volumetric Render Settings")] public bool renderToTex3D;
		public int densityTextureRes;

		[Header("Particles Ordering")]
		public bool preserveParticlesOrder = false;

		[Header("References")] public ComputeShader compute;
		public FluidSpawner spawner;

		[HideInInspector] public RenderTexture DensityMap;
		public Vector3 Scale => transform.localScale;

		// Buffers
		public GraphicsBuffer foamBuffer { get; private set; }
		public GraphicsBuffer foamSortTargetBuffer { get; private set; }
		public GraphicsBuffer foamCountBuffer { get; private set; }
		public GraphicsBuffer positionBuffer { get; private set; }
		public GraphicsBuffer velocityBuffer { get; private set; }
		public GraphicsBuffer densityBuffer { get; private set; }
		public GraphicsBuffer predictedPositionsBuffer;
		public GraphicsBuffer debugBuffer { get; private set; }

		public GraphicsBuffer colliderBuffer { get; private set; }
		public GraphicsBuffer effectorBuffer { get; private set; }

		GraphicsBuffer sortTarget_positionBuffer;
		GraphicsBuffer sortTarget_velocityBuffer;
		GraphicsBuffer sortTarget_predictedPositionsBuffer;

		GraphicsBuffer previousIndicesBuffer;

		// Kernel IDs
		const int externalForcesKernel = 0;
		const int spatialHashKernel = 1;
		const int reorderKernel = 2;
		const int reorderCopybackKernel = 3;
		const int densityKernel = 4;
		const int pressureKernel = 5;
		const int viscosityKernel = 6;
		const int updatePositionsKernel = 7;
		const int renderKernel = 8;
		const int foamUpdateKernel = 9;
		const int foamReorderCopyBackKernel = 10;
        const int reverseReorderKernel = 11;

        SpatialHash spatialHash;

		// Colliders
		List<FluidCollider> colliders = new List<FluidCollider>();
		FluidColliderInternal[] collidersArray;

        // Effector
        List<FluidEffector> effectors = new List<FluidEffector>();
        FluidEffectorInternal[] effectorsArray;

        // State
        bool isPaused;
		bool pauseNextFrame;
		float smoothRadiusOld;
		float simTimer;
		bool inSlowMode;
		FluidSpawner.SpawnData spawnData;
		Dictionary<GraphicsBuffer, string> bufferNameLookup;

		void Start()
		{
			Debug.Log("Controls: Space = Play/Pause, R = Reset");
			isPaused = false;

			Initialize();
		}

		void Initialize()
		{
			spawnData = spawner.GetSpawnData();
			int numParticles = spawnData.points.Length;

			spatialHash = new SpatialHash(numParticles);
			
			// Create buffers
			positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			velocityBuffer = CreateStructuredBuffer<float3>(numParticles);
			densityBuffer = CreateStructuredBuffer<float2>(numParticles);
			foamBuffer = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamSortTargetBuffer = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamCountBuffer = CreateStructuredBuffer<uint>(4096);
			debugBuffer = CreateStructuredBuffer<float3>(numParticles);

			sortTarget_positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_velocityBuffer = CreateStructuredBuffer<float3>(numParticles);

			previousIndicesBuffer = CreateStructuredBuffer<uint>(numParticles);

			bufferNameLookup = new Dictionary<GraphicsBuffer, string>
			{
				{ positionBuffer, "Positions" },
				{ predictedPositionsBuffer, "PredictedPositions" },
				{ velocityBuffer, "Velocities" },
				{ densityBuffer, "Densities" },
				{ spatialHash.SpatialKeys, "SpatialKeys" },
				{ spatialHash.SpatialOffsets, "SpatialOffsets" },
				{ spatialHash.SpatialIndices, "SortedIndices" },
				{ sortTarget_positionBuffer, "SortTarget_Positions" },
				{ sortTarget_predictedPositionsBuffer, "SortTarget_PredictedPositions" },
				{ sortTarget_velocityBuffer, "SortTarget_Velocities" },
				{ previousIndicesBuffer, "PreviousIndices" },
				{ foamCountBuffer, "WhiteParticleCounters" },
				{ foamBuffer, "WhiteParticles" },
				{ foamSortTargetBuffer, "WhiteParticlesCompacted" },
				{ debugBuffer, "Debug" },
			};

			// Set buffer data
			SetInitialBufferData(spawnData);

			// External forces kernel
			SetBuffers(compute, externalForcesKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				positionBuffer,
				predictedPositionsBuffer,
				velocityBuffer
			});

			// Spatial hash kernel
			SetBuffers(compute, spatialHashKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
				predictedPositionsBuffer,
				spatialHash.SpatialIndices
			});

			// Reorder kernel
			SetBuffers(compute, reorderKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				positionBuffer,
				sortTarget_positionBuffer,
				predictedPositionsBuffer,
				sortTarget_predictedPositionsBuffer,
				velocityBuffer,
				sortTarget_velocityBuffer,
				spatialHash.SpatialIndices,
				previousIndicesBuffer,
			});

			// Reverse Reorder kernel
            SetBuffers(compute, reverseReorderKernel, bufferNameLookup, new GraphicsBuffer[]
{
                positionBuffer,
                sortTarget_positionBuffer,
                predictedPositionsBuffer,
                sortTarget_predictedPositionsBuffer,
                velocityBuffer,
                sortTarget_velocityBuffer,
                previousIndicesBuffer,
			});

            // Reorder copyback kernel
            SetBuffers(compute, reorderCopybackKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				positionBuffer,
				sortTarget_positionBuffer,
				predictedPositionsBuffer,
				sortTarget_predictedPositionsBuffer,
				velocityBuffer,
				sortTarget_velocityBuffer,
				spatialHash.SpatialIndices
			});

			// Density kernel
			SetBuffers(compute, densityKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets
			});

			// Pressure kernel
			SetBuffers(compute, pressureKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
				foamBuffer,
				foamCountBuffer,
				debugBuffer
			});

			// Viscosity kernel
			SetBuffers(compute, viscosityKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets
			});

			// Update positions kernel
			SetBuffers(compute, updatePositionsKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				positionBuffer,
				velocityBuffer
			});

			// Render to 3d tex kernel
			SetBuffers(compute, renderKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
			});

			// Foam update kernel
			SetBuffers(compute, foamUpdateKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				foamBuffer,
				foamCountBuffer,
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
				foamSortTargetBuffer,
				//debugBuffer
			});


			// Foam reorder copyback kernel
			SetBuffers(compute, foamReorderCopyBackKernel, bufferNameLookup, new GraphicsBuffer[]
			{
				foamBuffer,
				foamSortTargetBuffer,
				foamCountBuffer,
			});

			compute.SetInt("numParticles", positionBuffer.count);
			compute.SetInt("MaxWhiteParticleCount", maxFoamParticleCount);

			UpdateSmoothingConstants();

			// Run single frame of sim with deltaTime = 0 to initialize density texture
			// (so that display can work even if paused at start)
			if (renderToTex3D)
			{
				RunSimulationFrame(0);
			}

			SimulationInitCompleted?.Invoke(this);
		}

		void Update()
		{
			// Run simulation
			if (!isPaused)
			{
				float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity; // If framerate dips too low, run the simulation slower than real-time
				float dt = Mathf.Min(Time.deltaTime * timeScale, maxDeltaTime);
				RunSimulationFrame(dt);
			}

			if (pauseNextFrame)
			{
				isPaused = true;
				pauseNextFrame = false;
			}

			HandleInput();
		}

		void RunSimulationFrame(float frameDeltaTime)
		{
			float subStepDeltaTime = frameDeltaTime / iterationsPerFrame;
			UpdateSettings(subStepDeltaTime, frameDeltaTime);

            UpdateEffectors();
            UpdateColliders();

            // Simulation sub-steps
            for (int i = 0; i < iterationsPerFrame; i++)
			{
				simTimer += subStepDeltaTime;
				RunSimulationStep();
			}

            // Foam and spray particles
            if (foamActive)
			{
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamUpdateKernel);
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamReorderCopyBackKernel);
			}

			// 3D density map
			if (renderToTex3D)
			{
				UpdateDensityMap();
			}
		}

		void UpdateDensityMap()
		{
			float maxAxis = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
			int w = Mathf.RoundToInt(transform.localScale.x / maxAxis * densityTextureRes);
			int h = Mathf.RoundToInt(transform.localScale.y / maxAxis * densityTextureRes);
			int d = Mathf.RoundToInt(transform.localScale.z / maxAxis * densityTextureRes);
			CreateRenderTexture3D(ref DensityMap, w, h, d, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat, TextureWrapMode.Clamp);
			//Debug.Log(w + " " + h + "  " + d);
			compute.SetTexture(renderKernel, "DensityMap", DensityMap);
			compute.SetInts("densityMapSize", DensityMap.width, DensityMap.height, DensityMap.volumeDepth);
			Dispatch(compute, DensityMap.width, DensityMap.height, DensityMap.volumeDepth, renderKernel);
		}

		void RunSimulationStep()
		{
			Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);

			Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
			spatialHash.Run();
			
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderCopybackKernel);

			Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
			if (viscosityStrength != 0) Dispatch(compute, positionBuffer.count, kernelIndex: viscosityKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: updatePositionsKernel);

			if (preserveParticlesOrder)
			{
				Dispatch(compute, positionBuffer.count, kernelIndex: reverseReorderKernel);
				Dispatch(compute, positionBuffer.count, kernelIndex: reorderCopybackKernel);
			}
        }

		void UpdateSmoothingConstants()
		{
			float r = smoothingRadius;
			float spikyPow2 = 15 / (2 * Mathf.PI * Mathf.Pow(r, 5));
			float spikyPow3 = 15 / (Mathf.PI * Mathf.Pow(r, 6));
			float spikyPow2Grad = 15 / (Mathf.PI * Mathf.Pow(r, 5));
			float spikyPow3Grad = 45 / (Mathf.PI * Mathf.Pow(r, 6));

			compute.SetFloat("K_SpikyPow2", spikyPow2);
			compute.SetFloat("K_SpikyPow3", spikyPow3);
			compute.SetFloat("K_SpikyPow2Grad", spikyPow2Grad);
			compute.SetFloat("K_SpikyPow3Grad", spikyPow3Grad);
		}

		void UpdateSettings(float stepDeltaTime, float frameDeltaTime)
		{
			if (smoothingRadius != smoothRadiusOld)
			{
				smoothRadiusOld = smoothingRadius;
				UpdateSmoothingConstants();
			}

			Vector3 simBoundsSize = transform.localScale;
			Vector3 simBoundsCentre = transform.position;

			compute.SetFloat("deltaTime", stepDeltaTime);
			compute.SetFloat("whiteParticleDeltaTime", frameDeltaTime);
			compute.SetFloat("simTime", simTimer);
			compute.SetFloat("collisionDamping", collisionDamping);
			compute.SetFloat("smoothingRadius", smoothingRadius);
			compute.SetFloat("targetDensity", targetDensity);
			compute.SetFloat("pressureMultiplier", pressureMultiplier);
			compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
			compute.SetFloat("viscosityStrength", viscosityStrength);
			compute.SetFloat("maxVelocity", maxVelocity);
			compute.SetVector("boundsSize", simBoundsSize);
			compute.SetVector("centre", simBoundsCentre);

            compute.SetFloat("gravity", gravity);
			compute.SetFloat("NoiseStrength", noiseStrength);
			compute.SetFloat("NoiseScale", noiseScale);
			compute.SetVector("NoiseSpeed", noiseSpeed);
			compute.SetInt("NoiseOctaves", noiseOctaves);

            compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
			compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);

			// Foam settings
			float fadeInT = (spawnRateFadeInTime <= 0) ? 1 : Mathf.Clamp01((simTimer - spawnRateFadeStartTime) / spawnRateFadeInTime);
			compute.SetVector("trappedAirParams", new Vector3(trappedAirSpawnRate * fadeInT * fadeInT, trappedAirVelocityMinMax.x, trappedAirVelocityMinMax.y));
			compute.SetVector("kineticEnergyParams", foamKineticEnergyMinMax);
			compute.SetFloat("bubbleBuoyancy", bubbleBuoyancy);
			compute.SetInt("sprayClassifyMaxNeighbours", sprayClassifyMaxNeighbours);
			compute.SetInt("bubbleClassifyMinNeighbours", bubbleClassifyMinNeighbours);
			compute.SetFloat("bubbleScaleChangeSpeed", bubbleChangeScaleSpeed);
			compute.SetFloat("bubbleScale", bubbleScale);
		}

		void SetInitialBufferData(FluidSpawner.SpawnData spawnData)
		{
			positionBuffer.SetData(spawnData.points);
			predictedPositionsBuffer.SetData(spawnData.points);
			velocityBuffer.SetData(spawnData.velocities);

			foamBuffer.SetData(new FoamParticle[foamBuffer.count]);

			debugBuffer.SetData(new float3[debugBuffer.count]);
			foamCountBuffer.SetData(new uint[foamCountBuffer.count]);
			simTimer = 0;
		}

		void HandleInput()
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				isPaused = !isPaused;
			}

			if (Input.GetKeyDown(KeyCode.RightArrow))
			{
				isPaused = false;
				pauseNextFrame = true;
			}

			if (Input.GetKeyDown(KeyCode.R))
			{
				pauseNextFrame = true;
				SetInitialBufferData(spawnData);
				// Run single frame of sim with deltaTime = 0 to initialize density texture
				// (so that display can work even if paused at start)
				if (renderToTex3D)
				{
					RunSimulationFrame(0);
				}
			}

			if (Input.GetKeyDown(KeyCode.Q))
			{
				inSlowMode = !inSlowMode;
			}
		}

		void OnDestroy()
		{
			foreach (var kvp in bufferNameLookup)
			{
				Release(kvp.Key);
			}

			spatialHash.Release();
		}


        [VFXType(VFXTypeAttribute.Usage.GraphicsBuffer)]
        public struct FoamParticle
		{
			public Vector3 position;
			public Vector3 velocity;
			public float lifetime;
			public float scale;
		}



        void OnDrawGizmos()
		{
			// Draw Bounds
			var m = Gizmos.matrix;
			Gizmos.matrix = transform.localToWorldMatrix;
			Gizmos.color = new Color(0, 1, 0, 0.5f);
			Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
			Gizmos.matrix = m;
		}

        #region Colliders
        public struct FluidColliderInternal
        {
            public Vector3 position;
            public float size; //Negative size used to invert collider (collide inside the sphere)
        }

        public void RegisterCollider(FluidCollider fluidCollider)
		{
			if(!colliders.Contains(fluidCollider))
				colliders.Add(fluidCollider);
		}

		public void UnregisterCollider(FluidCollider fluidCollider)
		{
            if (colliders.Contains(fluidCollider))
                colliders.Remove(fluidCollider);
        }

        void UpdateColliders()
        {
			//Update collider buffer and array sizes
			UpdateCollidersBuffer();

			//Update collider data
            for (int i = 0; i < colliders.Count; i++)
            {
				collidersArray[i].position = colliders[i].transform.position;
				collidersArray[i].size = colliders[i].invert ? -colliders[i].transform.localScale.x * .5f : colliders[i].transform.localScale.x * .5f;
            }

			colliderBuffer.SetData(collidersArray);

			//Bind to kernel
			compute.SetInt("CollidersCount", colliders.Count);
			compute.SetBuffer(updatePositionsKernel, "Colliders", colliderBuffer);
			compute.SetBuffer(foamUpdateKernel, "Colliders", colliderBuffer);
        }

		void UpdateCollidersBuffer()
		{
			int activeCollidersCount = colliders.Count;

			if(colliderBuffer != null && activeCollidersCount > 0 && activeCollidersCount != colliderBuffer.count)
			{
				colliderBuffer.Release();
				colliderBuffer = null;
			}

			if(colliderBuffer == null)
			{
				colliderBuffer = CreateStructuredBuffer<FluidColliderInternal>(Mathf.Max(1, colliders.Count));
			}

			if(collidersArray == null || activeCollidersCount != collidersArray.Length)
			{
				collidersArray = new FluidColliderInternal[activeCollidersCount];
			}
		}

        #endregion

        #region Effectors

        public struct FluidEffectorInternal
        {
			public float type;
            public Vector3 position;
			public Vector4 data1;
			public Vector4 data2;
        }

        public void RegisterEffector(FluidEffector fluidEffector)
        {
            if (!effectors.Contains(fluidEffector))
                effectors.Add(fluidEffector);
        }

        public void UnregisterEffector(FluidEffector fluidEffector)
        {
            if (effectors.Contains(fluidEffector))
                effectors.Remove(fluidEffector);
        }

        void UpdateEffectors()
        {
            //Update Effector buffer and array sizes
            UpdateEffectorsBuffer();

            //Update Effector data
            for (int i = 0; i < effectors.Count; i++)
            {
				effectorsArray[i].type = (int)effectors[i].effectorType;
                effectorsArray[i].position = effectors[i].transform.position;

				switch (effectors[i].effectorType)
				{
					case FluidEffector.EffectorType.Gravitational:
                        effectorsArray[i].data1.x = effectors[i].radius;
                        effectorsArray[i].data1.y = effectors[i].attractionStrength;
                        break;

                    case FluidEffector.EffectorType.FarAttractor:
                        effectorsArray[i].data1.x = effectors[i].radius;
                        effectorsArray[i].data1.y = effectors[i].attractionStrength;
                        break;

                    case FluidEffector.EffectorType.Vortex:
						effectorsArray[i].data1.x = effectors[i].radius;
						effectorsArray[i].data1.y = effectors[i].attractionStrength;
						effectorsArray[i].data1.z = effectors[i].vortexStrength;
						effectorsArray[i].data1.w = effectors[i].channelStrength;
						effectorsArray[i].data2.x = effectors[i].transform.forward.x;
						effectorsArray[i].data2.y = effectors[i].transform.forward.y;
						effectorsArray[i].data2.z = effectors[i].transform.forward.z;
                        break;

					default:
						break;
				}
            }

            effectorBuffer.SetData(effectorsArray);

            //Bind to kernel
            //TODO : Investigate why if enable at first frame it glitch the simulation.
            compute.SetInt("EffectorsCount", simTimer > 0 ? effectors.Count : 0); 
            compute.SetBuffer(externalForcesKernel, "Effectors", effectorBuffer);
        }

        void UpdateEffectorsBuffer()
        {
            int activeEffectorsCount = effectors.Count;

            if (effectorBuffer != null && activeEffectorsCount > 0 && activeEffectorsCount != effectorBuffer.count)
            {
                effectorBuffer.Release();
                effectorBuffer = null;
            }

            if (effectorBuffer == null)
            {
                effectorBuffer = CreateStructuredBuffer<FluidEffectorInternal>(Mathf.Max(1, effectors.Count));
            }

            if (effectorsArray == null || activeEffectorsCount != effectorsArray.Length)
            {
                effectorsArray = new FluidEffectorInternal[activeEffectorsCount];
            }
        }

        #endregion
    }
}