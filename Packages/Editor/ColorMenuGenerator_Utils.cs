using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace AvatarMenuCreatorGenerator
{
    public partial class ColorMenuGenerator : EditorWindow
    {
        private string GetRelativePath(GameObject obj, GameObject root)
        {
            if (obj == root) return obj.name;

            List<string> path = new();
            Transform current = obj.transform;

            while (current != null && current.gameObject != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        private string CleanupName(string name)
        {
            if (useCustomNameParse && !string.IsNullOrWhiteSpace(nameParsePattern))
            {
                return ParseNameWithPattern(name, nameParsePattern);
            }

            // "Avatar_ColorRed" -> "ColorRed" のような変換
            if (name.Contains("_"))
            {
                var parts = name.Split('_');
                if (parts.Length > 1)
                {
                    return parts[^1];
                }
            }
            return name;
        }

        private string ParseNameWithPattern(string name, string pattern)
        {
            var parts = name.Split('_', '-', ' ', ',');
            var result = pattern;
            result = Regex.Replace(result, @"\{(\d+)\}", match =>
            {
                int index = int.Parse(match.Groups[1].Value);
                int actualIndex = index - 1;
                return actualIndex >= 0 && actualIndex < parts.Length ? parts[actualIndex] : string.Empty;
            });
            return result;
        }

        // Transform値が等しいかチェック
        private bool IsTransformEqual(RendererInfo a, RendererInfo b)
        {
            const float epsilon = 0.0001f;

            if (!Vector3.Distance(a.localPosition, b.localPosition).Equals(0f) &&
                Vector3.Distance(a.localPosition, b.localPosition) > epsilon)
            {
                return false;
            }

            if (Quaternion.Angle(a.localRotation, b.localRotation) > epsilon)
            {
                return false;
            }

            if (!Vector3.Distance(a.localScale, b.localScale).Equals(0f) &&
                Vector3.Distance(a.localScale, b.localScale) > epsilon)
            {
                return false;
            }

            return true;
        }

        // Boundsが等しいか
        private bool IsBoundsEqual(Bounds a, Bounds b)
        {
            const float epsilon = 0.0001f;

            if (Vector3.Distance(a.center, b.center) > epsilon)
            {
                return false;
            }

            if (Vector3.Distance(a.size, b.size) > epsilon)
            {
                return false;
            }

            return true;
        }

        private void AddPrefabsFromSameFolder()
        {
            if (basePrefab == null) return;

            // basePrefabの元となるPrefabアセットを取得
            GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(basePrefab);

            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog("エラー", "ベースオブジェクトがPrefabインスタンスではありません。", "OK");
                return;
            }

            // Prefabのパスを取得
            string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(prefabPath))
            {
                EditorUtility.DisplayDialog("エラー", "Prefabのパスが取得できませんでした。", "OK");
                return;
            }

            // フォルダパスを取得
            string folderPath = System.IO.Path.GetDirectoryName(prefabPath);

            // フォルダ内の全てのPrefabを検索
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });

            int addedCount = 0;
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                // 同じフォルダ内のみ（サブフォルダを除外）
                if (System.IO.Path.GetDirectoryName(assetPath) != folderPath)
                {
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                if (prefab != null && prefab != prefabAsset && !variationPrefabs.Contains(prefab))
                {
                    variationPrefabs.Add(prefab);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                detectedVariations.Clear();
                Debug.Log($"{addedCount}個のPrefabを追加しました: {folderPath}");
            }
            else
            {
                EditorUtility.DisplayDialog("情報", "追加できるPrefabが見つかりませんでした。", "OK");
            }
        }
    }
}
#endif