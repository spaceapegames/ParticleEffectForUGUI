using UnityEditor;
using UnityEditor.UI;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using ShaderPropertyType = Coffee.UIExtensions.AnimatableProperty.ShaderPropertyType;

namespace Coffee.UIExtensions
{
    [CustomEditor(typeof(UIParticle))]
    [CanEditMultipleObjects]
    public class UIParticleEditor : GraphicEditor
    {
        //################################
        // Constant or Static Members.
        //################################
        static readonly GUIContent s_ContentParticleMaterial = new GUIContent("Particle Material", "The material for rendering particles");
        static readonly GUIContent s_ContentTrailMaterial = new GUIContent("Trail Material", "The material for rendering particle trails");
        static readonly List<ParticleSystem> s_ParticleSystems = new List<ParticleSystem>();
        static readonly Color s_GizmoColor = new Color(1f, 0.7f, 0.7f, 0.9f);

        static readonly List<string> s_MaskablePropertyNames = new List<string>()
        {
            "_Stencil",
            "_StencilComp",
            "_StencilOp",
            "_StencilWriteMask",
            "_StencilReadMask",
            "_ColorMask",
        };

        //################################
        // Public/Protected Members.
        //################################
        /// <summary>
        /// This function is called when the object becomes enabled and active.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            _spParticleSystem = serializedObject.FindProperty("m_ParticleSystem");
            _spTrailParticle = serializedObject.FindProperty("m_TrailParticle");
            _spScale = serializedObject.FindProperty("m_Scale");
            _spIgnoreParent = serializedObject.FindProperty("m_IgnoreParent");
            _spAnimatableProperties = serializedObject.FindProperty("m_AnimatableProperties");
            _particles = targets.Cast<UIParticle>().ToArray();
            _shapeModuleUIs = null;

            var targetsGos = targets.Cast<UIParticle>().Select(x => x.gameObject).ToArray();
            _inspector = Resources.FindObjectsOfTypeAll<ParticleSystemInspector>()
                .FirstOrDefault(x => x.targets.Cast<ParticleSystem>().Select(x => x.gameObject).SequenceEqual(targetsGos));
        }

        /// <summary>
        /// Implement this function to make a custom inspector.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(_spParticleSystem);
            EditorGUI.EndDisabledGroup();

            EditorGUI.indentLevel++;
            var ps = _spParticleSystem.objectReferenceValue as ParticleSystem;
            if (ps)
            {
                var pr = ps.GetComponent<ParticleSystemRenderer>();
                var sp = new SerializedObject(pr).FindProperty("m_Materials");

                EditorGUI.BeginChangeCheck();
                {
                    EditorGUILayout.PropertyField(sp.GetArrayElementAtIndex(0), s_ContentParticleMaterial);
                    if (2 <= sp.arraySize)
                    {
                        EditorGUILayout.PropertyField(sp.GetArrayElementAtIndex(1), s_ContentTrailMaterial);
                    }
                }
                if (EditorGUI.EndChangeCheck())
                {
                    sp.serializedObject.ApplyModifiedProperties();
                }

                if (!Application.isPlaying && pr.enabled)
                {
                    EditorGUILayout.HelpBox("UIParticles disable the RendererModule in ParticleSystem at runtime to prevent double rendering.", MessageType.Warning);
                }
            }

            EditorGUI.indentLevel--;

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(_spTrailParticle);
            EditorGUI.EndDisabledGroup();

            var current = target as UIParticle;

            EditorGUILayout.PropertyField(_spIgnoreParent);

            EditorGUI.BeginDisabledGroup(!current.isRoot);
            EditorGUILayout.PropertyField(_spScale);
            EditorGUI.EndDisabledGroup();

            // AnimatableProperties
            AnimatedPropertiesEditor.DrawAnimatableProperties(_spAnimatableProperties, current.material);

            current.GetComponentsInChildren<ParticleSystem>(true, s_ParticleSystems);
            if (s_ParticleSystems.Any(x => x.GetComponent<UIParticle>() == null))
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox("There are child ParticleSystems that does not have a UIParticle component.\nAdd UIParticle component to them.", MessageType.Warning);
                GUILayout.BeginVertical();
                if (GUILayout.Button("Fix"))
                {
                    foreach (var p in s_ParticleSystems.Where(x => !x.GetComponent<UIParticle>()))
                    {
                        p.gameObject.AddComponent<UIParticle>();
                    }
                }

                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }

            s_ParticleSystems.Clear();

            if (current.maskable && current.material && current.material.shader)
            {
                var mat = current.material;
                var shader = mat.shader;
                foreach (var propName in s_MaskablePropertyNames)
                {
                    if (!mat.HasProperty(propName))
                    {
                        EditorGUILayout.HelpBox(string.Format("Shader {0} doesn't have '{1}' property. This graphic is not maskable.", shader.name, propName), MessageType.Warning);
                        break;
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }


        //################################
        // Private Members.
        //################################
        SerializedProperty _spParticleSystem;
        SerializedProperty _spTrailParticle;
        SerializedProperty _spScale;
        SerializedProperty _spIgnoreParent;
        SerializedProperty _spAnimatableProperties;
        UIParticle[] _particles;
        ShapeModuleUI[] _shapeModuleUIs;
        ParticleSystemInspector _inspector;

        void OnSceneGUI()
        {
            _shapeModuleUIs = _shapeModuleUIs ?? _inspector?.m_ParticleEffectUI?.m_Emitters?.SelectMany(x => x.m_Modules).OfType<ShapeModuleUI>()?.ToArray();
            if (_shapeModuleUIs == null || _shapeModuleUIs.Length == 0 || _shapeModuleUIs[0].GetParticleSystem() != (target as UIParticle).cachedParticleSystem)
                return;

            Action postAction = () => { };
            Color origin = ShapeModuleUI.s_GizmoColor.m_Color;
            Color originDark = ShapeModuleUI.s_GizmoColor.m_Color;
            ShapeModuleUI.s_GizmoColor.m_Color = s_GizmoColor;
            ShapeModuleUI.s_GizmoColor.m_OptionalDarkColor = s_GizmoColor;

            _particles
                .Distinct()
                .Select(x => new {canvas = x.canvas, ps = x.cachedParticleSystem, scale = x.scale})
                .Where(x => x.ps && x.canvas)
                .ToList()
                .ForEach(x =>
                {
                    var trans = x.ps.transform;
                    var hasChanged = trans.hasChanged;
                    var localScale = trans.localScale;
                    postAction += () => trans.localScale = localScale;
                    trans.localScale = Vector3.Scale(localScale, x.canvas.rootCanvas.transform.localScale * x.scale);
                });

            try
            {
                foreach (var ui in _shapeModuleUIs)
                    ui.OnSceneViewGUI();
            }
            catch
            {
            }

            postAction();
            ShapeModuleUI.s_GizmoColor.m_Color = origin;
            ShapeModuleUI.s_GizmoColor.m_OptionalDarkColor = originDark;
        }
    }
}
