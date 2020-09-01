using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Coffee.UIExtensions
{
    internal static class SpriteExtensions
    {
#if UNITY_EDITOR
        private static Type tSpriteEditorExtension = Type.GetType("UnityEditor.Experimental.U2D.SpriteEditorExtension, UnityEditor")
                                                     ?? Type.GetType("UnityEditor.U2D.SpriteEditorExtension, UnityEditor");

        private static MethodInfo miGetActiveAtlasTexture = tSpriteEditorExtension
            .GetMethod("GetActiveAtlasTexture", BindingFlags.Static | BindingFlags.NonPublic);

        public static Texture2D GetActualTexture(this Sprite self)
        {
            if (!self) return null;

            if (Application.isPlaying) return self.texture;
            var ret = miGetActiveAtlasTexture.Invoke(null, new[] {self}) as Texture2D;
            return ret ? ret : self.texture;
        }
#else
        internal static Texture2D GetActualTexture(this Sprite self)
        {
            return self ? self.texture : null;
        }
#endif
    }

    internal static class LongExtensions
    {
        public static int BitCount(this long self)
        {
            self = self - ((self >> 1) & 0x5555555555555555L);
            self = (self & 0x3333333333333333L) + ((self >> 2) & 0x3333333333333333L);
            return (int)(unchecked(((self + (self >> 4)) & 0xF0F0F0F0F0F0F0FL) * 0x101010101010101L) >> 56);
        }
    }

    internal static class MeshExtensions
    {
        static readonly List<Color32> s_Colors = new List<Color32>();

        public static void ModifyColorSpaceToLinear(this Mesh self)
        {
            self.GetColors(s_Colors);

            for (var i = 0; i < s_Colors.Count; i++)
                s_Colors[i] = ((Color) s_Colors[i]).gamma;

            self.SetColors(s_Colors);
            s_Colors.Clear();
        }
    }

    internal static class ParticleSystemExtensions
    {
        public static void SortForRendering(this List<ParticleSystem> self, Transform transform)
        {
            self.Sort((a, b) =>
            {
                var tr = transform;
                var ra = a.GetComponent<ParticleSystemRenderer>();
                var rb = b.GetComponent<ParticleSystemRenderer>();

                if (!Mathf.Approximately(ra.sortingFudge, rb.sortingFudge))
                    return ra.sortingFudge < rb.sortingFudge ? 1 : -1;

                var pa = tr.InverseTransformPoint(a.transform.position).z;
                var pb = tr.InverseTransformPoint(b.transform.position).z;

                return Mathf.Approximately(pa, pb)
                    ? 0
                    : pa < pb
                        ? 1
                        : -1;
            });
        }

        public static Texture2D GetTextureForSprite(this ParticleSystem self)
        {
            if (!self) return null;

            // Get sprite's texture.
            var tsaModule = self.textureSheetAnimation;
            if (!tsaModule.enabled || tsaModule.mode != ParticleSystemAnimationMode.Sprites) return null;

            for (var i = 0; i < tsaModule.spriteCount; i++)
            {
                var sprite = tsaModule.GetSprite(i);
                if (!sprite) continue;

                return sprite.GetActualTexture();
            }

            return null;
        }

        public static void Exec(this List<ParticleSystem> self, Action<ParticleSystem> action)
        {
            self.RemoveAll(p => !p);
            self.ForEach(action);
        }
    }

}
