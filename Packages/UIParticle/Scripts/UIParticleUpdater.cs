using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Coffee.UIExtensions
{
    public static class UIParticleUpdater
    {
        static readonly List<UIParticle> s_ActiveParticles = new List<UIParticle>();
        static MaterialPropertyBlock s_Mpb;

        public static void Register(UIParticle particle)
        {
            if (!particle) return;
            s_ActiveParticles.Add(particle);
        }

        public static void Unregister(UIParticle particle)
        {
            if (!particle) return;
            s_ActiveParticles.Remove(particle);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            Canvas.willRenderCanvases -= Refresh;
            Canvas.willRenderCanvases += Refresh;
        }

        private static void Refresh()
        {
            for (var i = 0; i < s_ActiveParticles.Count; i++)
            {
                try
                {
                    Refresh(s_ActiveParticles[i]);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private static void Refresh(UIParticle particle)
        {
            if (!particle) return;

            Profiler.BeginSample("Modify scale");
            ModifyScale(particle);
            Profiler.EndSample();

            if (!particle.isValid) return;

            Profiler.BeginSample("Check materials");
            particle.CheckMaterials();
            Profiler.EndSample();

            Profiler.BeginSample("Make matrix");
            var scaledMatrix = GetScaledMatrix(particle);
            Profiler.EndSample();

            Profiler.BeginSample("Bake mesh");
            BakeMesh(particle, scaledMatrix);
            Profiler.EndSample();

            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                Profiler.BeginSample("Modify color space to linear");
                particle.bakedMesh.ModifyColorSpaceToLinear();
                Profiler.EndSample();
            }

            Profiler.BeginSample("Set mesh to CanvasRenderer");
            particle.canvasRenderer.SetMesh(particle.bakedMesh);
            Profiler.EndSample();

            Profiler.BeginSample("Update Animatable Material Properties");
            UpdateAnimatableMaterialProperties(particle);
            Profiler.EndSample();
        }

        private static void ModifyScale(UIParticle particle)
        {
            var modifiedScale = particle.m_Scale3D;

            // Ignore Canvas scaling.
            if (particle.ignoreCanvasScaler && particle.canvas)
            {
                var s = particle.canvas.rootCanvas.transform.localScale;
                var sInv = new Vector3(
                    Mathf.Approximately(s.x, 0) ? 1 : 1 / s.x,
                    Mathf.Approximately(s.y, 0) ? 1 : 1 / s.y,
                    Mathf.Approximately(s.z, 0) ? 1 : 1 / s.z);
                modifiedScale = Vector3.Scale(modifiedScale, sInv);
            }

            // Scale is already modified.
            var tr = particle.transform;
            if (Mathf.Approximately((tr.localScale - modifiedScale).sqrMagnitude, 0)) return;

            tr.localScale = modifiedScale;
        }

        private static Matrix4x4 GetScaledMatrix(UIParticle particle)
        {
            var transform = particle.transform;
            var main = particle.mainModule;
            var space = main.simulationSpace;
            if (space == ParticleSystemSimulationSpace.Custom && !main.customSimulationSpace)
                space = ParticleSystemSimulationSpace.Local;

            switch (space)
            {
                case ParticleSystemSimulationSpace.Local:
                    return Matrix4x4.Rotate(transform.rotation).inverse
                           * Matrix4x4.Scale(transform.lossyScale).inverse;
                case ParticleSystemSimulationSpace.World:
                    return transform.worldToLocalMatrix;
                case ParticleSystemSimulationSpace.Custom:
                    // #78: Support custom simulation space.
                    return transform.worldToLocalMatrix
                           * Matrix4x4.Translate(main.customSimulationSpace.position);
                default:
                    return Matrix4x4.identity;
            }
        }

        private static void BakeMesh(UIParticle particle, Matrix4x4 scaledMatrix)
        {
            // Clear mesh before bake.
            MeshHelper.Clear();
            particle.bakedMesh.Clear();

            // No particle to render.
            if (particle.cachedParticleSystem.particleCount <= 0) return;

            // Get camera for baking mesh.
            var cam = BakingCamera.GetCamera(particle.canvas);
            var renderer = particle.cachedRenderer;
            var trail = particle.trailModule;

            Profiler.BeginSample("Bake mesh");
            if (CanBakeMesh(renderer))
                renderer.BakeMesh(MeshHelper.GetTemporaryMesh(), cam, true);

            if (trail.enabled)
                renderer.BakeTrailsMesh(MeshHelper.GetTemporaryMesh(), cam, true);
            Profiler.EndSample();

            Profiler.BeginSample("Apply matrix to position");
            MeshHelper.CombineMesh(particle.bakedMesh, scaledMatrix);
            Profiler.EndSample();
        }

        private static bool CanBakeMesh(ParticleSystemRenderer renderer)
        {
            // #69: Editor crashes when mesh is set to null when `ParticleSystem.RenderMode = Mesh`
            if (renderer.renderMode == ParticleSystemRenderMode.Mesh && !renderer.mesh) return false;

            // #61: When `ParticleSystem.RenderMode = None`, an error occurs
            if (renderer.renderMode == ParticleSystemRenderMode.None) return false;

            return true;
        }

        /// <summary>
        /// Copy the value from MaterialPropertyBlock to CanvasRenderer
        /// </summary>
        private static void UpdateAnimatableMaterialProperties(UIParticle particle)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            if (0 == particle.m_AnimatableProperties.Length) return;
            if (0 == particle.canvasRenderer.materialCount) return;

            var mat = particle.canvasRenderer.GetMaterial(0);
            if (!mat) return;

            // #41: Copy the value from MaterialPropertyBlock to CanvasRenderer
            if (s_Mpb == null)
                s_Mpb = new MaterialPropertyBlock();
            particle.cachedRenderer.GetPropertyBlock(s_Mpb);
            foreach (var ap in particle.m_AnimatableProperties)
            {
                ap.UpdateMaterialProperties(mat, s_Mpb);
            }

            s_Mpb.Clear();
        }
    }
}
