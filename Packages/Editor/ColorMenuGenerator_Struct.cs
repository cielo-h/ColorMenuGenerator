using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace AvatarMenuCreatorGenerator
{
    public partial class MaterialPresetChooseMenuGenerator : EditorWindow
    {
        [System.Serializable]
        public class PrefabVariation
        {
            public string choiceName;
            public GameObject sourcePrefab;
            public bool include = true;
            public Texture2D icon;
            public List<MaterialInfo> materials = new();
            public bool isBase = false; // ベースprefabかどうか
        }

        [System.Serializable]
        public class MaterialInfo
        {
            public Material material;
            public string rendererPath;
            public int materialSlotIndex;
        }

        // Rendererの情報を格納する構造体
        [System.Serializable]
        private class RendererInfo
        {
            public Renderer renderer;
            public string path;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public Mesh sharedMesh;
            public Transform rootBone;
            public Bounds localBounds;
        }
    }
}
#endif
