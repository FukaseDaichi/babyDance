using System;
using NUnit.Framework;
using UnityEngine;

namespace BabyDance.Tests
{
    public class FaceDirectorTests
    {
        private static SkinnedMeshRenderer Renderer(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(SkinnedMeshRenderer));
            go.transform.SetParent(parent.transform);
            return go.GetComponent<SkinnedMeshRenderer>();
        }

        [Test]
        public void MissingOrRenamedRendererThrows()
        {
            var go = new GameObject("Dancer", typeof(FaceDirector));
            try
            {
                var face = go.GetComponent<FaceDirector>();
                Assert.Throws<InvalidOperationException>(() => face.Tick(0));
                Renderer(go, "FaceEyes");
                Assert.Throws<InvalidOperationException>(() => face.Tick(0));
                var mouth = Renderer(go, "WrongMouth");
                Assert.Throws<InvalidOperationException>(() => face.Tick(0));
                mouth.name = "FaceMouth";
                Assert.DoesNotThrow(() => face.Tick(0));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void TickWritesIndependentAtlasRectsWithoutInstantiatingMaterials()
        {
            var go = new GameObject("Dancer", typeof(FaceDirector));
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                var eyes = Renderer(go, "FaceEyes"); var mouth = Renderer(go, "FaceMouth");
                eyes.sharedMaterial = material; mouth.sharedMaterial = material;
                var face = go.GetComponent<FaceDirector>(); face.Expression = 3;
                var block = new MaterialPropertyBlock();
                // 5.2: 通常瞬きなし、目3・口1。5.5: 強制閉眼、目5・口0。
                face.Tick(5.2);
                eyes.GetPropertyBlock(block); Assert.That(block.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(.25f, .5f, .75f, .5f)));
                mouth.GetPropertyBlock(block); Assert.That(block.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(1f/3, 1, 1f/3, 0)));
                face.SwitchBeat = 5.25; face.Tick(5.5);
                eyes.GetPropertyBlock(block); Assert.That(block.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(.25f, .5f, .25f, 0)));
                mouth.GetPropertyBlock(block); Assert.That(block.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(1f/3, 1, 0, 0)));
                Assert.That(eyes.sharedMaterial, Is.SameAs(material)); Assert.That(mouth.sharedMaterial, Is.SameAs(material));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(material); }
        }
    }
}
