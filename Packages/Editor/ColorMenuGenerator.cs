using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using net.narazaka.avatarmenucreator.components;

#if UNITY_EDITOR
namespace AvatarMenuCreatorGenerator
{
    /// <summary>
    /// シーン内のベースprefabと複数のバリエーションprefabからマテリアル変更メニューを生成するツール
    /// </summary>
    public class MaterialPresetChooseMenuGenerator : EditorWindow
    {
        private GameObject targetAvatar;
        private GameObject basePrefab; // シーン内の既存オブジェクト
        private readonly List<GameObject> variationPrefabs = new(); // プロジェクトのprefab
        private string menuName = "色メニュー";
        private bool saved = true;
        private bool synced = true;
        private int defaultChoiceIndex = 0;

        private readonly List<PrefabVariation> detectedVariations = new();
        private Vector2 scrollPos;
        private bool showDetectedMaterials = true;

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

        [MenuItem("Tools/Color Menu Generator")]
        public static void ShowWindow()
        {
            var window = GetWindow<MaterialPresetChooseMenuGenerator>("Material Preset Generator");
            window.minSize = new Vector2(480, 600);
        }

        void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            GUILayout.Label("マテリアルプリセット ChooseMenu 生成", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "シーン内のベースオブジェクトと複数のバリエーションprefabから\n" +
                "マテリアル変更メニューを生成します。\n" +
                "※ベースオブジェクトは既にシーンに配置されている必要があります。",
                MessageType.Info);
            EditorGUILayout.Space(10);

            // 基本設定
            EditorGUILayout.LabelField("基本設定", EditorStyles.boldLabel);
            targetAvatar = EditorGUILayout.ObjectField("対象アバター", targetAvatar, typeof(GameObject), true) as GameObject;

            if (targetAvatar != null)
            {
                if (!targetAvatar.TryGetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(out var _))
                {
                    EditorGUILayout.HelpBox("選択されたオブジェクトにVRCAvatarDescriptorがありません", MessageType.Warning);
                }
            }

            EditorGUILayout.Space(10);

            // ベースオブジェクト (シーン内)
            EditorGUILayout.LabelField("ベースオブジェクト (シーン内)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("シーンに既に配置されているオブジェクトを選択してください", MessageType.None);
            
            GameObject previousBase = basePrefab;
            basePrefab = EditorGUILayout.ObjectField("ベースオブジェクト", basePrefab, typeof(GameObject), true) as GameObject;

            if (basePrefab != null && basePrefab != previousBase)
            {
                // シーン内のオブジェクトかチェック
                if (!basePrefab.scene.IsValid())
                {
                    EditorUtility.DisplayDialog("エラー", "シーン内のオブジェクトを選択してください。\nプロジェクトのprefabは使用できません。", "OK");
                    basePrefab = null;
                }
                else
                {
                    detectedVariations.Clear();
                }
            }

            EditorGUILayout.Space(10);

            // バリエーションPrefab
            EditorGUILayout.LabelField("バリエーションPrefab", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("マテリアルバリエーションを含むprefabをドラッグ&ドロップで追加してください", MessageType.None);

            // ドラッグ&ドロップエリア
            Rect dropArea = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Prefabをここにドラッグ&ドロップ", EditorStyles.helpBox);

            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        break;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
                        {
                            GameObject go = draggedObject as GameObject;
                            if (go != null && !variationPrefabs.Contains(go))
                            {
                                // プロジェクトのprefabかチェック
                                if (PrefabUtility.IsPartOfPrefabAsset(go))
                                {
                                    variationPrefabs.Add(go);
                                }
                                else
                                {
                                    Debug.LogWarning($"{go.name} はプロジェクトのprefabではありません。スキップします。");
                                }
                            }
                        }
                        detectedVariations.Clear();
                    }
                    evt.Use();
                    break;
            }

            EditorGUILayout.Space(5);

            for (int i = 0; i < variationPrefabs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                variationPrefabs[i] = EditorGUILayout.ObjectField($"Prefab {i + 1}", variationPrefabs[i], typeof(GameObject), false) as GameObject;
                if (GUILayout.Button("-", GUILayout.Width(25)))
                {
                    variationPrefabs.RemoveAt(i);
                    detectedVariations.Clear();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Prefabを追加"))
            {
                variationPrefabs.Add(null);
            }
            if (variationPrefabs.Count > 0 && GUILayout.Button("全てクリア", GUILayout.Width(100)))
            {
                if (EditorUtility.DisplayDialog("確認", "全てのPrefabをクリアしますか?", "はい", "いいえ"))
                {
                    variationPrefabs.Clear();
                    detectedVariations.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // メニュー設定
            EditorGUILayout.LabelField("メニュー設定", EditorStyles.boldLabel);
            menuName = EditorGUILayout.TextField("メニュー名", menuName);

            EditorGUILayout.Space(5);

            saved = EditorGUILayout.Toggle("パラメーターを保存", saved);
            synced = EditorGUILayout.Toggle("パラメーターを同期", synced);

            EditorGUILayout.Space(15);

            // 検出ボタン
            GUI.backgroundColor = Color.cyan;
            EditorGUI.BeginDisabledGroup(basePrefab == null);
            if (GUILayout.Button("全マテリアルを検出", GUILayout.Height(35)))
            {
                DetectMaterials();
            }
            EditorGUI.EndDisabledGroup();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(10);

            // 検出されたプリセット表示
            if (detectedVariations.Count > 0)
            {
                showDetectedMaterials = EditorGUILayout.Foldout(showDetectedMaterials,
                    $"検出されたプリセット ({detectedVariations.Count}個)", true, EditorStyles.foldoutHeader);

                if (showDetectedMaterials)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginVertical("box");

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("デフォルト選択肢:", GUILayout.Width(120));
                    defaultChoiceIndex = EditorGUILayout.IntSlider(defaultChoiceIndex, 0, detectedVariations.Count - 1);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(5);

                    for (int i = 0; i < detectedVariations.Count; i++)
                    {
                        var variation = detectedVariations[i];

                        GUI.backgroundColor = i == defaultChoiceIndex ? new Color(0.8f, 1f, 0.8f) : 
                                            variation.isBase ? new Color(0.9f, 0.9f, 1f) : Color.white;
                        EditorGUILayout.BeginVertical("box");
                        GUI.backgroundColor = Color.white;

                        EditorGUILayout.BeginHorizontal();
                        variation.include = EditorGUILayout.Toggle(variation.include, GUILayout.Width(20));
                        EditorGUILayout.LabelField($"選択肢 {i}", EditorStyles.boldLabel, GUILayout.Width(80));
                        if (variation.isBase)
                        {
                            EditorGUILayout.LabelField("(ベース)", EditorStyles.miniLabel, GUILayout.Width(60));
                        }
                        if (i == defaultChoiceIndex)
                        {
                            EditorGUILayout.LabelField("(デフォルト)", EditorStyles.miniLabel);
                        }
                        EditorGUILayout.EndHorizontal();

                        variation.choiceName = EditorGUILayout.TextField("選択肢名", variation.choiceName);

                        EditorGUI.BeginDisabledGroup(true);
                        if (variation.isBase)
                        {
                            EditorGUILayout.ObjectField("ソース (シーン内)", variation.sourcePrefab, typeof(GameObject), true);
                        }
                        else
                        {
                            EditorGUILayout.ObjectField("ソースPrefab", variation.sourcePrefab, typeof(GameObject), false);
                        }
                        EditorGUILayout.LabelField($"マテリアル数: {variation.materials.Count}");
                        EditorGUI.EndDisabledGroup();

                        variation.icon = EditorGUILayout.ObjectField("アイコン (任意)", variation.icon, typeof(Texture2D), false) as Texture2D;

                        // マテリアル詳細表示
                        if (variation.materials.Count > 0)
                        {
                            EditorGUILayout.LabelField("含まれるマテリアル:", EditorStyles.miniLabel);
                            EditorGUI.indentLevel++;
                            foreach (var mat in variation.materials.Take(5))
                            {
                                EditorGUILayout.LabelField($"• {mat.rendererPath} [スロット{mat.materialSlotIndex}]: {(mat.material != null ? mat.material.name : null ?? "null")}", EditorStyles.miniLabel);
                            }
                            if (variation.materials.Count > 5)
                            {
                                EditorGUILayout.LabelField($"... 他 {variation.materials.Count - 5}個", EditorStyles.miniLabel);
                            }
                            EditorGUI.indentLevel--;
                        }

                        EditorGUILayout.EndVertical();
                        EditorGUILayout.Space(3);
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space(20);

            // 生成ボタン
            GUI.enabled = detectedVariations.Count > 0 && targetAvatar != null;
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button($"{menuName} を生成", GUILayout.Height(40)))
            {
                CreateChooseMenu();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            EditorGUILayout.EndScrollView();
        }

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
            var baseMaterials = ExtractMaterialsFromObject(basePrefab, basePrefab);
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
                var materials = ExtractMaterialsFromObject(prefab, basePrefab);

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

        private List<MaterialInfo> ExtractMaterialsFromObject(GameObject obj, GameObject _)
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

        string CleanupName(string name)
        {
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

                // 各(Renderer, Slot)をチェックして、バリエーションがあるもののみ追加
                int arrayIndex = 0;
                int skippedSlots = 0;
                foreach (var (rendererPath, slotIndex) in allRendererSlots)
                {
                    // このスロットの全マテリアルを収集
                    var materialsForSlot = new List<Material>();
                    bool hasNullMaterial = false;
                    
                    foreach (var variation in includedVariations)
                    {
                        var matchingMat = variation.materials.FirstOrDefault(m => 
                            m.rendererPath == rendererPath && m.materialSlotIndex == slotIndex);
                        if (matchingMat != null)
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
                        Debug.LogWarning($"スキップ: {rendererPath} [スロット{slotIndex}] - マテリアルが見つかりません");
                        skippedSlots++;
                        continue;
                    }

                    // 全て同じマテリアルかチェック
                    var firstMaterial = materialsForSlot[0];
                    bool allSame = materialsForSlot.All(m => m == firstMaterial);
                    
                    if (allSame && !hasNullMaterial)
                    {
                        // 全て同一の有効なマテリアル → スキップ
                        Debug.Log($"スキップ: {rendererPath} [スロット{slotIndex}] - 全ての選択肢で同一マテリアル ({(firstMaterial != null ? firstMaterial.name : null ?? "null")})");
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
                        var matchingMat = variation.materials.FirstOrDefault(m => 
                            m.rendererPath == rendererPath && m.materialSlotIndex == slotIndex);

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
                        nameValueProp.stringValue = variation.choiceName;
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
                    $"AvatarChooseMenuCreatorを作成しました!\n\n" +
                    $"名前: {menuName}\n" +
                    $"選択肢数: {includedVariations.Count}\n" +
                    $"対象Renderer数: {arrayIndex}\n" +
                    (skippedSlots > 0 ? $"スキップ: {skippedSlots}個 (同一マテリアル)\n" : "") +
                    $"\nビルド時に自動的にメニューが生成されます。",
                    "OK");

                Debug.Log($"✓ ChooseMenu '{menuName}' を作成しました");
                Debug.Log($"  選択肢:");
                foreach (var variation in includedVariations)
                {
                    Debug.Log($"    - {variation.choiceName}: {variation.materials.Count}個のマテリアル");
                }
                if (skippedSlots > 0)
                {
                    Debug.Log($"  {skippedSlots}個のスロットをスキップしました (全ての選択肢で同一マテリアル)");
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
    }
}
#endif