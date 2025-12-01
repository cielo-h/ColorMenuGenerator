using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace AvatarMenuCreatorGenerator
{
    public partial class ColorMenuGenerator : EditorWindow
    {
        [MenuItem("Tools/Color Menu Generator")]
        public static void ShowWindow()
        {
            var window = GetWindow<ColorMenuGenerator>("Color Menu Generator");
            window.minSize = new Vector2(640, 790);
        }

        private void OnDisable()
        {
            SessionState.SetString("MaterialPresetMenu_MenuName", menuName);
            SessionState.SetBool("MaterialPresetMenu_Saved", saved);
            SessionState.SetBool("MaterialPresetMenu_Synced", synced);
            SessionState.SetBool("MaterialPresetMenu_ChoiceNameOnlyNumber", choiceNameOnlyNumber);
            SessionState.SetBool("MaterialPresetMenu_AddMAMenuInstaller", addMAMenuInstaller);
            SessionState.SetInt("MaterialPresetMenu_DefaultChoiceIndex", defaultChoiceIndex);

            SessionState.SetBool("MaterialPresetMenu_UseCustomNameParse", useCustomNameParse);
            SessionState.SetString("MaterialPresetMenu_NameParsePattern", nameParsePattern);

            if (targetAvatar != null)
                SessionState.SetInt("MaterialPresetMenu_TargetAvatar", targetAvatar.GetInstanceID());
            if (basePrefab != null)
                SessionState.SetInt("MaterialPresetMenu_BasePrefab", basePrefab.GetInstanceID());
        }

        private void OnEnable()
        {
            menuName = SessionState.GetString("MaterialPresetMenu_MenuName", "色メニュー");
            saved = SessionState.GetBool("MaterialPresetMenu_Saved", true);
            synced = SessionState.GetBool("MaterialPresetMenu_Synced", true);
            choiceNameOnlyNumber = SessionState.GetBool("MaterialPresetMenu_ChoiceNameOnlyNumber", false);
            addMAMenuInstaller = SessionState.GetBool("MaterialPresetMenu_AddMAMenuInstaller", true);
            defaultChoiceIndex = SessionState.GetInt("MaterialPresetMenu_DefaultChoiceIndex", 0);

            useCustomNameParse = SessionState.GetBool("MaterialPresetMenu_UseCustomNameParse", false);
            nameParsePattern = SessionState.GetString("MaterialPresetMenu_NameParsePattern", "{1}");

            int avatarID = SessionState.GetInt("MaterialPresetMenu_TargetAvatar", 0);
            if (avatarID != 0)
                targetAvatar = EditorUtility.InstanceIDToObject(avatarID) as GameObject;

            int baseID = SessionState.GetInt("MaterialPresetMenu_BasePrefab", 0);
            if (baseID != 0)
                basePrefab = EditorUtility.InstanceIDToObject(baseID) as GameObject;
        }

        void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            GUILayout.Label("カラーメニュー ChooseMenu 生成", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "シーン内のベースオブジェクトと複数のバリエーションprefabから\n" +
                "マテリアル変更メニューを生成します。\n" +
                "※ベースオブジェクトは既にシーンに配置されている必要があります。",
                MessageType.Info);
            EditorGUILayout.Space(10);

            // 基本設定
            EditorGUILayout.LabelField("基本設定", EditorStyles.boldLabel);

            // シーン内にVRCAvatarDescriptorが1つのみの場合、自動設定
            if (targetAvatar == null)
            {
                var avatarDescriptors = FindObjectsOfType<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                if (avatarDescriptors.Length == 1)
                {
                    targetAvatar = avatarDescriptors[0].gameObject;
                }
            }

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

            EditorGUILayout.BeginHorizontal();
            GameObject previousBase = basePrefab;
            basePrefab = EditorGUILayout.ObjectField("ベースオブジェクト", basePrefab, typeof(GameObject), true) as GameObject;

            EditorGUI.BeginDisabledGroup(basePrefab == null);
            if (GUILayout.Button("フォルダ内を追加", GUILayout.Width(100)))
            {
                AddPrefabsFromSameFolder();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

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

                        foreach (var draggedObject in DragAndDrop.objectReferences)
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
                    EditorGUILayout.EndHorizontal();
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
            choiceNameOnlyNumber = EditorGUILayout.Toggle("選択肢名を数字のみにする", choiceNameOnlyNumber);

            DrawOptionSettings();

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

        private void DrawOptionSettings()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("オプション設定", EditorStyles.boldLabel);

            addMAMenuInstaller = EditorGUILayout.Toggle("MAMenuInstallerを追加する", addMAMenuInstaller);
            useCustomNameParse = EditorGUILayout.Toggle("カスタムパースを使用", useCustomNameParse);

            if (useCustomNameParse)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("パターン:", GUILayout.Width(60));
                nameParsePattern = EditorGUILayout.TextField(nameParsePattern);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(
                    "例: Avt_White_01 の場合\n" +
                    "{1} = Avt\n" +
                    "{2} = White\n" +
                    "{3} = 01\n" +
                    "{2}_{3} = White_01\n" +
                    "範囲外のインデックスは空文字になります",
                    MessageType.Info);

                // プレビュー表示
                if (basePrefab != null)
                {
                    string preview = ParseNameWithPattern(basePrefab.name, nameParsePattern);
                    EditorGUILayout.LabelField($"プレビュー: {basePrefab.name} → {preview}");
                }
            }
        }
    }
}
#endif