using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using net.narazaka.avatarmenucreator.components;
using nadena.dev.modular_avatar.core;

#if UNITY_EDITOR
namespace AvatarMenuCreatorGenerator
{
    /// <summary>
    /// シーン内のベースprefabと複数のバリエーションprefabからマテリアル変更メニューを生成するツール
    /// </summary>
    public partial class MaterialPresetChooseMenuGenerator : EditorWindow
    {
        private GameObject targetAvatar;
        private GameObject basePrefab; // シーン内の既存オブジェクト
        private readonly List<GameObject> variationPrefabs = new(); // プロジェクトのprefab
        private string menuName = "色メニュー";
        private bool saved = true;
        private bool synced = true;
        private bool choiceNameOnlyNumber = false;
        private int defaultChoiceIndex = 0;

        private readonly List<PrefabVariation> detectedVariations = new();
        private Vector2 scrollPos;
        private bool showDetectedMaterials = true;

        private void DetectMaterials()
        {
            detectedVariations.Clear();

            if (basePrefab == null)
            {
                EditorUtility.DisplayDialog("エラー", "ベースオブジェクトを選択してください", "OK");
                return;
            }

            // ベースオブジェクトから構造を取得
            var baseRenderers = basePrefab.GetComponentsInChildren<Renderer>(true);
            if (baseRenderers.Length == 0)
            {
                EditorUtility.DisplayDialog("エラー", "ベースオブジェクトにRendererが見つかりません", "OK");
                return;
            }

            // 1. ベースオブジェクトを最初の選択肢として追加
            var baseMaterials = ExtractMaterialsFromObject(basePrefab);
            if (baseMaterials.Count > 0)
            {
                detectedVariations.Add(new PrefabVariation
                {
                    choiceName = CleanupName(basePrefab.name),
                    sourcePrefab = basePrefab,
                    materials = baseMaterials,
                    include = true,
                    icon = null,
                    isBase = true
                });
            }

            // 2. バリエーションprefabを処理
            var validPrefabs = variationPrefabs.Where(p => p != null).ToList();
            foreach (var prefab in validPrefabs)
            {
                var materials = ExtractMaterialsFromObject(prefab);

                if (materials.Count > 0)
                {
                    string choiceName = CleanupName(prefab.name);

                    detectedVariations.Add(new PrefabVariation
                    {
                        choiceName = choiceName,
                        sourcePrefab = prefab,
                        materials = materials,
                        include = true,
                        icon = null,
                        isBase = false
                    });
                }
            }

            if (detectedVariations.Count == 0)
            {
                EditorUtility.DisplayDialog("警告",
                    "マテリアルが検出できませんでした。\n\n" +
                    "オブジェクトにマテリアルを持つRendererが存在するか確認してください。",
                    "OK");
            }
            else
            {
                Debug.Log($"✓ {detectedVariations.Count}個のプリセットを検出しました");
                defaultChoiceIndex = 0;
            }
        }

        private List<MaterialInfo> ExtractMaterialsFromObject(GameObject obj)
        {
            var materials = new List<MaterialInfo>();
            var renderers = obj.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                // ベースオブジェクトからの相対パスを取得
                string rendererPath = GetRelativePath(renderer.gameObject, obj);
                var sharedMaterials = renderer.sharedMaterials;

                for (int slotIndex = 0; slotIndex < sharedMaterials.Length; slotIndex++)
                {
                    var material = sharedMaterials[slotIndex];

                    materials.Add(new MaterialInfo
                    {
                        material = material,
                        rendererPath = rendererPath,
                        materialSlotIndex = slotIndex
                    });
                }
            }

            return materials;
        }

        private void CreateChooseMenu()
        {
            if (targetAvatar == null)
            {
                EditorUtility.DisplayDialog("エラー", "対象アバターを指定してください", "OK");
                return;
            }

            var includedVariations = detectedVariations.Where(v => v.include).ToList();
            if (includedVariations.Count == 0)
            {
                EditorUtility.DisplayDialog("エラー", "少なくとも1つの選択肢を有効にしてください", "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Create AvatarChooseMenuCreator");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                // メニューオブジェクトを作成
                GameObject menuObject = new(menuName);
                Undo.RegisterCreatedObjectUndo(menuObject, "Create Menu Object");

                menuObject.transform.SetParent(targetAvatar.transform);
                menuObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                menuObject.transform.localScale = Vector3.one;

                //MA Menu Installerを追加
                VRC.Core.ExtensionMethods.GetOrAddComponent<ModularAvatarMenuInstaller>(menuObject);

                var choiceNames = new HashSet<string>();
                foreach (var variation in includedVariations)
                {
                    string originalName = variation.choiceName;
                    string uniqueName = originalName;
                    int counter = 2;

                    while (choiceNames.Contains(uniqueName))
                    {
                        uniqueName = $"{originalName}{counter}";
                        counter++;
                    }

                    choiceNames.Add(uniqueName);
                    variation.choiceName = uniqueName;
                }

                // AvatarChooseMenuCreatorコンポーネントを追加
                var chooseMenuCreator = Undo.AddComponent<AvatarChooseMenuCreator>(menuObject);

                // SerializedObjectを使用して設定
                SerializedObject so = new(chooseMenuCreator);
                SerializedProperty avatarChooseMenuProp = so.FindProperty("AvatarChooseMenu") ?? throw new System.Exception("AvatarChooseMenuプロパティが見つかりません。AvatarMenuCreatorForMAが正しくインストールされているか確認してください。");

                // 基本設定
                var savedProp = avatarChooseMenuProp.FindPropertyRelative("Saved");
                var syncedProp = avatarChooseMenuProp.FindPropertyRelative("Synced");
                var defaultValueProp = avatarChooseMenuProp.FindPropertyRelative("ChooseDefaultValue");
                var chooseCountProp = avatarChooseMenuProp.FindPropertyRelative("ChooseCount");

                if (savedProp == null || syncedProp == null || defaultValueProp == null || chooseCountProp == null)
                {
                    throw new System.Exception("必要なプロパティが見つかりません。コンポーネントの構造を確認してください。");
                }

                savedProp.boolValue = saved;
                syncedProp.boolValue = synced;
                defaultValueProp.intValue = defaultChoiceIndex;
                chooseCountProp.intValue = includedVariations.Count;

                // ChooseMaterialsプロパティを設定
                SerializedProperty chooseMaterialsProp = avatarChooseMenuProp.FindPropertyRelative("ChooseMaterials") ?? throw new System.Exception("ChooseMaterialsプロパティが見つかりません。");

                // keys1 = Rendererパス, keys2 = スロットインデックス, values = マテリアル辞書
                var matKeys1ArrProp = chooseMaterialsProp.FindPropertyRelative("keys1");
                var matKeys2ArrProp = chooseMaterialsProp.FindPropertyRelative("keys2");
                var matValuesArrProp = chooseMaterialsProp.FindPropertyRelative("values");

                if (matKeys1ArrProp == null || matKeys2ArrProp == null || matValuesArrProp == null)
                {
                    throw new System.Exception("ChooseMaterialsの辞書プロパティが見つかりません。");
                }

                matKeys1ArrProp.ClearArray();
                matKeys2ArrProp.ClearArray();
                matValuesArrProp.ClearArray();

                // 全ての(Renderer, Slot)の組み合わせを収集
                var allRendererSlots = new HashSet<(string rendererPath, int slotIndex)>();
                foreach (var variation in includedVariations)
                {
                    foreach (var mat in variation.materials)
                    {
                        allRendererSlots.Add((mat.rendererPath, mat.materialSlotIndex));
                    }
                }

                var baseRendererMap = BuildRendererMap(basePrefab);

                // 各(Renderer, Slot)をチェックして、バリエーションがあるもののみ追加
                int arrayIndex = 0;
                int skippedSlots = 0;
                List<(string rendererPath, int slotIndex)> uniqueRenderes = new();

                foreach (var (rendererPath, slotIndex) in allRendererSlots)
                {
                    // basePrefabに含まれていないrendererはスキップ
                    if (!baseRendererMap.ContainsKey(rendererPath))
                    {
                        uniqueRenderes.Add((rendererPath, slotIndex));
                        arrayIndex++;
                        continue;
                    }

                    // このスロットの全マテリアルを収集
                    var materialsForSlot = new List<Material>();
                    bool hasNullMaterial = false;

                    foreach (var variation in includedVariations)
                    {
                        var matchingMat = FindMatchingMaterial(variation.materials, rendererPath, slotIndex, variation.sourcePrefab, baseRendererMap);
                        if (matchingMat != null && matchingMat.material != null)
                        {
                            materialsForSlot.Add(matchingMat.material);
                            if (matchingMat.material == null)
                            {
                                hasNullMaterial = true;
                            }
                        }
                    }

                    // マテリアルが収集できなかった、または全てnullの場合はスキップ
                    if (materialsForSlot.Count == 0)
                    {
                        //Debug.LogWarning($"スキップ: {rendererPath} [スロット{slotIndex}] - マテリアルが見つかりません");
                        skippedSlots++;
                        continue;
                    }

                    // 全て同じマテリアルかチェック
                    var firstMaterial = materialsForSlot[0];
                    bool allSame = materialsForSlot.All(m => m == firstMaterial);

                    if (allSame && !hasNullMaterial)
                    {
                        // 全て同一の有効なマテリアル → スキップ
                        //Debug.Log($"スキップ: {rendererPath} [スロット{slotIndex}]\n全ての選択肢で同一マテリアル ({(firstMaterial != null ? firstMaterial.name : "null")})");
                        skippedSlots++;
                        continue;
                    }

                    // アバター内のRendererを探す (ベースオブジェクトから相対パスで)
                    string fullRendererPath = GetGameObjectPath(basePrefab, targetAvatar) + "/" + rendererPath;

                    // 先頭のスラッシュを削除
                    if (fullRendererPath.StartsWith("/"))
                    {
                        fullRendererPath = fullRendererPath[1..];
                    }

                    matKeys1ArrProp.InsertArrayElementAtIndex(arrayIndex);
                    matKeys2ArrProp.InsertArrayElementAtIndex(arrayIndex);
                    matValuesArrProp.InsertArrayElementAtIndex(arrayIndex);

                    SerializedProperty key1Prop = matKeys1ArrProp.GetArrayElementAtIndex(arrayIndex);
                    SerializedProperty key2Prop = matKeys2ArrProp.GetArrayElementAtIndex(arrayIndex);

                    if (key1Prop == null || key2Prop == null)
                    {
                        throw new System.Exception($"キープロパティ[{arrayIndex}]が取得できません。");
                    }

                    key1Prop.stringValue = fullRendererPath;
                    key2Prop.intValue = slotIndex;

                    SerializedProperty valueProp = matValuesArrProp.GetArrayElementAtIndex(arrayIndex) ?? throw new System.Exception($"バリュープロパティ[{arrayIndex}]が取得できません。");

                    // valuePropはマテリアル辞書 (int -> Material)
                    SerializedProperty intMatDicProp = valueProp;
                    var intMatKeysArrProp = intMatDicProp.FindPropertyRelative("keys");
                    var intMatValuesArrProp = intMatDicProp.FindPropertyRelative("values");

                    if (intMatKeysArrProp == null || intMatValuesArrProp == null)
                    {
                        throw new System.Exception("マテリアル辞書のkeys/valuesプロパティが見つかりません。");
                    }

                    intMatKeysArrProp.ClearArray();
                    intMatValuesArrProp.ClearArray();

                    // 各選択肢のマテリアルを設定
                    for (int i = 0; i < includedVariations.Count; i++)
                    {
                        var variation = includedVariations[i];
                        var matchingMat = FindMatchingMaterial(variation.materials, rendererPath, slotIndex, variation.sourcePrefab, baseRendererMap);

                        if (matchingMat != null)
                        {
                            int matIndex = intMatKeysArrProp.arraySize;
                            intMatKeysArrProp.InsertArrayElementAtIndex(matIndex);
                            intMatValuesArrProp.InsertArrayElementAtIndex(matIndex);

                            var intMatKeyProp = intMatKeysArrProp.GetArrayElementAtIndex(matIndex);
                            var intMatValueProp = intMatValuesArrProp.GetArrayElementAtIndex(matIndex);

                            if (intMatKeyProp != null && intMatValueProp != null)
                            {
                                intMatKeyProp.intValue = i;
                                intMatValueProp.objectReferenceValue = matchingMat.material;
                            }
                        }
                    }

                    arrayIndex++;
                }

                // ChooseNamesを設定
                SerializedProperty chooseNamesProp = avatarChooseMenuProp.FindPropertyRelative("ChooseNames") ?? throw new System.Exception("ChooseNamesプロパティが見つかりません。");
                var namesKeysArrProp = chooseNamesProp.FindPropertyRelative("keys");
                var namesValuesArrProp = chooseNamesProp.FindPropertyRelative("values");

                if (namesKeysArrProp == null || namesValuesArrProp == null)
                {
                    throw new System.Exception("ChooseNamesの辞書プロパティが見つかりません。");
                }

                namesKeysArrProp.ClearArray();
                namesValuesArrProp.ClearArray();

                for (int i = 0; i < includedVariations.Count; i++)
                {
                    var variation = includedVariations[i];

                    namesKeysArrProp.InsertArrayElementAtIndex(i);
                    namesValuesArrProp.InsertArrayElementAtIndex(i);

                    var nameKeyProp = namesKeysArrProp.GetArrayElementAtIndex(i);
                    var nameValueProp = namesValuesArrProp.GetArrayElementAtIndex(i);

                    if (nameKeyProp != null && nameValueProp != null)
                    {
                        nameKeyProp.intValue = i;
                        nameValueProp.stringValue = choiceNameOnlyNumber ? (i + 1).ToString() : variation.choiceName;
                    }
                }

                // ChooseIconsを設定
                SerializedProperty chooseIconsProp = avatarChooseMenuProp.FindPropertyRelative("ChooseIcons") ?? throw new System.Exception("ChooseIconsプロパティが見つかりません。");
                var iconsKeysArrProp = chooseIconsProp.FindPropertyRelative("keys");
                var iconsValuesArrProp = chooseIconsProp.FindPropertyRelative("values");

                if (iconsKeysArrProp == null || iconsValuesArrProp == null)
                {
                    throw new System.Exception("ChooseIconsの辞書プロパティが見つかりません。");
                }

                iconsKeysArrProp.ClearArray();
                iconsValuesArrProp.ClearArray();

                for (int i = 0; i < includedVariations.Count; i++)
                {
                    var variation = includedVariations[i];
                    if (variation.icon != null)
                    {
                        int iconIndex = iconsKeysArrProp.arraySize;
                        iconsKeysArrProp.InsertArrayElementAtIndex(iconIndex);
                        iconsValuesArrProp.InsertArrayElementAtIndex(iconIndex);

                        var iconKeyProp = iconsKeysArrProp.GetArrayElementAtIndex(iconIndex);
                        var iconValueProp = iconsValuesArrProp.GetArrayElementAtIndex(iconIndex);

                        if (iconKeyProp != null && iconValueProp != null)
                        {
                            iconKeyProp.intValue = i;
                            iconValueProp.objectReferenceValue = variation.icon;
                        }
                    }
                }

                so.ApplyModifiedProperties();

                // 選択
                Selection.activeGameObject = menuObject;
                EditorGUIUtility.PingObject(menuObject);

                Undo.CollapseUndoOperations(undoGroup);

                EditorUtility.DisplayDialog("成功",
                    $"{menuName} を作成しました!\n\n" +
                    $"選択肢数: {includedVariations.Count}\n" +
                    $"対象Renderer数: {arrayIndex}\n" +
                    (skippedSlots > 0 ? $"スキップ: {skippedSlots}個 (同一マテリアル)\n" : "") +
                    (uniqueRenderes.Count > 0 ? $"固有: {uniqueRenderes.Count}個 (固有マテリアル)\n" : ""),
                    "OK");

                if (uniqueRenderes.Count > 0)
                {
                    string output = $"{uniqueRenderes.Count}個の固有Renderer:\n";
                    foreach (var (rendererPath, slotIndex) in uniqueRenderes)
                    {
                        output += $"slot[{slotIndex}]: {rendererPath}\n";
                    }
                    Debug.LogWarning(output);
                }
            }
            catch (System.Exception e)
            {
                Undo.RevertAllInCurrentGroup();
                EditorUtility.DisplayDialog("エラー",
                    $"コンポーネントの作成中にエラーが発生しました:\n\n{e.Message}\n\n" +
                    "AvatarMenuCreatorForMAが正しくインストールされているか確認してください。",
                    "OK");
                Debug.LogError($"Error creating ChooseMenu: {e}");
            }
        }

        private string GetGameObjectPath(GameObject obj, GameObject root)
        {
            if (obj == root) return "";

            List<string> path = new();
            Transform current = obj.transform;

            while (current != null && current.gameObject != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        // ベースオブジェクトのRendererマッピングを作成
        private Dictionary<string, RendererInfo> BuildRendererMap(GameObject baseObject)
        {
            var map = new Dictionary<string, RendererInfo>();
            var renderers = baseObject.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                string path = GetRelativePath(renderer.gameObject, baseObject);
                var info = new RendererInfo
                {
                    renderer = renderer,
                    path = path,
                    localPosition = renderer.transform.localPosition,
                    localRotation = renderer.transform.localRotation,
                    localScale = renderer.transform.localScale
                };

                if (renderer is SkinnedMeshRenderer smr)
                {
                    info.sharedMesh = smr.sharedMesh;
                    info.rootBone = smr.rootBone;
                    info.localBounds = smr.localBounds;
                }

                map[path] = info;
            }

            return map;
        }

        // マッチするマテリアルを検索
        private MaterialInfo FindMatchingMaterial(List<MaterialInfo> materials, string targetPath, int targetSlot, GameObject sourceObject, Dictionary<string, RendererInfo> baseRendererMap)
        {
            // まず完全一致を探す
            var exactMatch = materials.FirstOrDefault(m =>
                m.rendererPath == targetPath && m.materialSlotIndex == targetSlot);

            if (exactMatch != null)
            {
                return exactMatch;
            }

            // ベースのRenderer情報を取得
            if (!baseRendererMap.TryGetValue(targetPath, out var baseInfo))
            {
                return null;
            }

            // baseInfoがSkinnedMeshRendererでない場合は完全一致のみ
            if (baseInfo.renderer is not SkinnedMeshRenderer baseSMR)
            {
                return null;
            }

            // sourceObject内の全てのSkinnedMeshRendererを取得
            var sourceRenderers = sourceObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            // Meshが一致するRendererを検索
            var matchingRenderers = new List<(SkinnedMeshRenderer renderer, string path)>();

            foreach (var sourceRenderer in sourceRenderers)
            {
                if (sourceRenderer.sharedMesh == baseSMR.sharedMesh &&
                    sourceRenderer.sharedMaterials.Length > targetSlot)
                {
                    string path = GetRelativePath(sourceRenderer.gameObject, sourceObject);
                    matchingRenderers.Add((sourceRenderer, path));
                }
            }

            // 一致するものが1つだけの場合はそのマテリアルを返す
            if (matchingRenderers.Count == 1)
            {
                var (renderer, path) = matchingRenderers[0];
                return new MaterialInfo
                {
                    material = renderer.sharedMaterials[targetSlot],
                    rendererPath = path,
                    materialSlotIndex = targetSlot
                };
            }

            // 複数ある場合はパラメーターの比較を行う
            foreach (var (sourceRenderer, sourcePath) in matchingRenderers)
            {
                var sourceInfo = new RendererInfo
                {
                    renderer = sourceRenderer,
                    path = sourcePath,
                    localPosition = sourceRenderer.transform.localPosition,
                    localRotation = sourceRenderer.transform.localRotation,
                    localScale = sourceRenderer.transform.localScale,
                    sharedMesh = sourceRenderer.sharedMesh,
                    rootBone = sourceRenderer.rootBone,
                    localBounds = sourceRenderer.localBounds
                };

                // Transformの比較
                if (!IsTransformEqual(sourceInfo, baseInfo))
                {
                    continue;
                }

                // RootBoneの比較
                if (sourceRenderer.rootBone != baseSMR.rootBone)
                {
                    continue;
                }

                // Boundsの比較
                if (!IsBoundsEqual(sourceRenderer.localBounds, baseSMR.localBounds))
                {
                    continue;
                }

                // 全ての条件を満たした
                return new MaterialInfo
                {
                    material = sourceRenderer.sharedMaterials[targetSlot],
                    rendererPath = sourcePath,
                    materialSlotIndex = targetSlot
                };
            }

            return null;
        }
    }
}
#endif