using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Coffee.UIExtensions
{
    /// <summary>
    /// Render maskable and sortable particle effect ,without Camera, RenderTexture or Canvas.
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIParticle : MaskableGraphic
    {
        //################################
        // Serialize Members.
        //################################
        [Tooltip("The ParticleSystem rendered by CanvasRenderer")] [SerializeField]
        ParticleSystem m_ParticleSystem;

        [HideInInspector] [SerializeField] bool m_IsTrail = false;

        [Tooltip("Ignore canvas scaler")] [SerializeField]
        bool m_IgnoreCanvasScaler = false;

        [Tooltip("Ignore parent scale")] [SerializeField]
        bool m_IgnoreParent = false;

        [Tooltip("Particle effect scale")] [SerializeField]
        float m_Scale = 0;

        [Tooltip("Animatable material properties. If you want to change the material properties of the ParticleSystem in Animation, enable it.")] [SerializeField]
        internal AnimatableProperty[] m_AnimatableProperties = new AnimatableProperty[0];

        [Tooltip("Particle effect scale")] [SerializeField]
        internal Vector3 m_Scale3D = Vector3.one;

        private readonly Material[] _maskMaterials = new Material[2];
        private DrivenRectTransformTracker _tracker;
        private Mesh _bakedMesh;
        private ParticleSystemRenderer _renderer;
        private int _cachedSharedMaterialId;
        private int _cachedTrailMaterialId;
        private bool _cachedSpritesMode;

        //################################
        // Public/Protected Members.
        //################################
        /// <summary>
        /// Should this graphic be considered a target for raycasting?
        /// </summary>
        public override bool raycastTarget
        {
            get { return false; }
            set { base.raycastTarget = value; }
        }

        /// <summary>
        /// Cached ParticleSystem.
        /// </summary>
        public ParticleSystem cachedParticleSystem
        {
            get { return m_ParticleSystem ? m_ParticleSystem : (m_ParticleSystem = GetComponent<ParticleSystem>()); }
        }

        /// <summary>
        /// Cached ParticleSystem.
        /// </summary>
        internal ParticleSystemRenderer cachedRenderer
        {
            get { return _renderer; }
        }

        public bool ignoreCanvasScaler
        {
            get { return m_IgnoreCanvasScaler; }
            set { m_IgnoreCanvasScaler = value; }
        }

        /// <summary>
        /// Particle effect scale.
        /// </summary>
        public float scale
        {
            get { return m_Scale3D.x; }
            set { m_Scale3D.Set(value, value, value); }
        }

        /// <summary>
        /// Particle effect scale.
        /// </summary>
        public Vector3 scale3D
        {
            get { return m_Scale3D; }
            set { m_Scale3D = value; }
        }

        private ParticleSystem.TextureSheetAnimationModule textureSheetAnimationModule
        {
            get { return cachedParticleSystem.textureSheetAnimation; }
        }

        internal ParticleSystem.TrailModule trailModule
        {
            get { return cachedParticleSystem.trails; }
        }

        internal ParticleSystem.MainModule mainModule
        {
            get { return cachedParticleSystem.main; }
        }

        public bool isValid
        {
            get { return m_ParticleSystem && _renderer && canvas; }
        }

        public Mesh bakedMesh
        {
            get { return _bakedMesh; }
        }

        protected override void UpdateMaterial()
        {
            if (!_renderer) return;

            canvasRenderer.materialCount = trailModule.enabled ? 2 : 1;

            // Regenerate main material.
            var mainMat = _renderer.sharedMaterial;
            var hasAnimatableProperties = 0 < m_AnimatableProperties.Length;
            var tex = GetTextureForSprite(cachedParticleSystem);
            if (hasAnimatableProperties || tex)
            {
                mainMat = new Material(mainMat);
                if (tex)
                {
                    mainMat.mainTexture = tex;
                }
            }

            canvasRenderer.SetMaterial(GetModifiedMaterial(mainMat, 0), 0);

            // Trail material
            if (trailModule.enabled)
                canvasRenderer.SetMaterial(GetModifiedMaterial(_renderer.trailMaterial, 1), 1);
        }

        private Material GetModifiedMaterial(Material baseMaterial, int index)
        {
            if (!baseMaterial || 1 < index || !isActiveAndEnabled) return baseMaterial;

            var baseMat = baseMaterial;
            if (m_ShouldRecalculateStencil)
            {
                m_ShouldRecalculateStencil = false;

                if (maskable)
                {
                    var sortOverrideCanvas = MaskUtilities.FindRootSortOverrideCanvas(transform);
                    m_StencilValue = MaskUtilities.GetStencilDepth(transform, sortOverrideCanvas) + index;
                }
                else
                {
                    m_StencilValue = 0;
                }
            }

            var component = GetComponent<Mask>();
            if (m_StencilValue <= 0 || (component != null && component.IsActive())) return baseMat;

            var stencilId = (1 << m_StencilValue) - 1;
            var maskMaterial = StencilMaterial.Add(baseMat, stencilId, StencilOp.Keep, CompareFunction.Equal, ColorWriteMask.All, stencilId, 0);
            StencilMaterial.Remove(_maskMaterials[index]);
            _maskMaterials[index] = maskMaterial;
            baseMat = _maskMaterials[index];

            return baseMat;
        }


        private static Texture2D GetTextureForSprite(ParticleSystem particle)
        {
            if (!particle) return null;

            // Get sprite's texture.
            var tsaModule = particle.textureSheetAnimation;
            if (!tsaModule.enabled || tsaModule.mode != ParticleSystemAnimationMode.Sprites) return null;

            for (var i = 0; i < tsaModule.spriteCount; i++)
            {
                var sprite = tsaModule.GetSprite(i);
                if (!sprite) continue;

                return sprite.GetActualTexture();
            }

            return null;
        }

        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        protected override void OnEnable()
        {
            if (m_IsTrail)
            {
                gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(gameObject);
                else
                    DestroyImmediate(gameObject);
                return;
            }

            UpdateVersionIfNeeded();

            _tracker.Add(this, rectTransform, DrivenTransformProperties.Scale);

            // Initialize.
            _renderer = cachedParticleSystem ? cachedParticleSystem.GetComponent<ParticleSystemRenderer>() : null;
            if (_renderer != null)
                _renderer.enabled = false;

            CheckMaterials();

            // Create objects.
            _bakedMesh = new Mesh();
            _bakedMesh.MarkDynamic();

            MeshHelper.Register();
            BakingCamera.Register();
            UIParticleUpdater.Register(this);

            base.OnEnable();
        }

        /// <summary>
        /// This function is called when the behaviour becomes disabled.
        /// </summary>
        protected override void OnDisable()
        {
            _tracker.Clear();

            // Destroy object.
            DestroyImmediate(_bakedMesh);
            _bakedMesh = null;

            MeshHelper.Unregister();
            BakingCamera.Unregister();
            UIParticleUpdater.Unregister(this);

            CheckMaterials();

            // Remove mask materials.
            for (var i = 0; i < _maskMaterials.Length; i++)
            {
                StencilMaterial.Remove(_maskMaterials[i]);
                _maskMaterials[i] = null;
            }

            base.OnDisable();
        }

        /// <summary>
        /// Call to update the geometry of the Graphic onto the CanvasRenderer.
        /// </summary>
        protected override void UpdateGeometry()
        {
        }

        /// <summary>
        /// This function is called when the parent property of the transform of the GameObject has changed.
        /// </summary>
        protected override void OnTransformParentChanged()
        {
        }

        /// <summary>
        /// Callback for when properties have been changed by animation.
        /// </summary>
        protected override void OnDidApplyAnimationProperties()
        {
        }


        //################################
        // Private Members.
        //################################
        private static bool HasMaterialChanged(Material material, ref int current)
        {
            var old = current;
            current = material ? material.GetInstanceID() : 0;
            return current != old;
        }

        internal void CheckMaterials()
        {
            if (!_renderer) return;

            var matChanged = HasMaterialChanged(_renderer.sharedMaterial, ref _cachedSharedMaterialId);
            var matChanged2 = HasMaterialChanged(_renderer.trailMaterial, ref _cachedTrailMaterialId);
            var isSpritesMode = textureSheetAnimationModule.enabled && textureSheetAnimationModule.mode == ParticleSystemAnimationMode.Sprites;
            var modeChanged = _cachedSpritesMode != isSpritesMode;
            _cachedSpritesMode = isSpritesMode;

            if (matChanged || matChanged2 || modeChanged)
                SetMaterialDirty();
        }

        private void UpdateVersionIfNeeded()
        {
            if (Mathf.Approximately(m_Scale, 0)) return;

            var parent = GetComponentInParent<UIParticle>();
            if (m_IgnoreParent || !parent)
                scale3D = m_Scale * transform.localScale;
            else
                scale3D = transform.localScale;
            m_Scale = 0;
        }
    }
}
