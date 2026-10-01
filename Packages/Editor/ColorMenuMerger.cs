using nadena.dev.modular_avatar.core;
using net.narazaka.avatarmenucreator.components;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace AvatarMenuCreatorGenerator
{
    public partial class ColorMenuGenerator
    {
        private enum MergeMode { Append, ByIndex }
        private enum SourceAction { Keep, Disable, Delete }

        private readonly List<AvatarChooseMenuCreator> mergeSources = new();
        private GameObject mergeAvatar;
        private string mergedMenuName = "色メニュー";
        private MergeMode mergeMode = MergeMode.ByIndex;
        private SourceAction mergeSourceAction = SourceAction.Disable;
        private bool mergeFillMissing = true;
        private bool mergeAddMAMenuInstaller = true;

        private class MergeMenuData
        {
            public bool saved, synced;
            public int defaultValue, count;
            public Dictionary<(string path, int slot), Dictionary<int, Object>> materials = new();
            public Dictionary<int, string> names = new();
            public Dictionary<int, Object> icons = new();
        }

        // ───────────────────────── GUI ─────────────────────────

        private void DrawMergerTab()
        {
            GUILayout.Label("カラーメニュー Merger", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "シーン内の複数のAvatarChooseMenuCreatorを1つに統合します。\n" +
                "統合元はドラッグ&ドロップ、または下のボタンで追加してください。",
                MessageType.Info);
            EditorGUILayout.Space(10);

            // 統合元
            EditorGUILayout.LabelField("統合元メニュー", EditorStyles.boldLabel);

            Rect dropArea = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "メニューのGameObjectをここにドラッグ&ドロップ", EditorStyles.helpBox);
            HandleMergeDrop(dropArea);

            int remove = -1, up = -1, down = -1;
            for (int i = 0; i < mergeSources.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                mergeSources[i] = EditorGUILayout.ObjectField($"メニュー {i + 1}", mergeSources[i],
                    typeof(AvatarChooseMenuCreator), true) as AvatarChooseMenuCreator;
                EditorGUILayout.LabelField($"{GetChooseCount(mergeSources[i])}択", GUILayout.Width(40));
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("▲", GUILayout.Width(25))) up = i;
                using (new EditorGUI.DisabledScope(i == mergeSources.Count - 1))
                    if (GUILayout.Button("▼", GUILayout.Width(25))) down = i;
                if (GUILayout.Button("-", GUILayout.Width(25))) remove = i;
                EditorGUILayout.EndHorizontal();
            }
            if (remove >= 0) mergeSources.RemoveAt(remove);
            else if (up >= 0) (mergeSources[up - 1], mergeSources[up]) = (mergeSources[up], mergeSources[up - 1]);
            else if (down >= 0) (mergeSources[down + 1], mergeSources[down]) = (mergeSources[down], mergeSources[down + 1]);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 空の枠を追加")) mergeSources.Add(null);
            if (GUILayout.Button("選択中のオブジェクトから追加"))
            {
                foreach (var go in Selection.gameObjects) AddMergeSource(go);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("アバター内の全メニューを検出"))
            {
                var root = mergeAvatar != null ? mergeAvatar : targetAvatar;
                if (root != null)
                {
                    foreach (var c in root.GetComponentsInChildren<AvatarChooseMenuCreator>(true))
                        AddMergeSource(c);
                }
            }
            if (mergeSources.Count > 0 && GUILayout.Button("全てクリア", GUILayout.Width(100)))
                mergeSources.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 出力設定
            EditorGUILayout.LabelField("出力設定", EditorStyles.boldLabel);

            // アバター自動検出
            if (mergeAvatar == null)
            {
                var first = mergeSources.FirstOrDefault(s => s != null);
                if (first != null)
                {
                    var desc = first.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                    if (desc != null) mergeAvatar = desc.gameObject;
                }
            }
            mergeAvatar = EditorGUILayout.ObjectField("対象アバター", mergeAvatar, typeof(GameObject), true) as GameObject;
            mergedMenuName = EditorGUILayout.TextField("統合後のメニュー名", mergedMenuName);
            mergeMode = (MergeMode)EditorGUILayout.EnumPopup("統合モード", mergeMode);

            var valid = mergeSources.Where(s => s != null).ToList();
            int total = valid.Count == 0 ? 0 :
                mergeMode == MergeMode.Append ? valid.Sum(GetChooseCount) : valid.Max(GetChooseCount);
            EditorGUILayout.HelpBox(
                mergeMode == MergeMode.Append
                    ? "【連結】各メニューの選択肢を順番に並べます。(例: 3択 + 4択 → 7択)"
                    : "【番号で統合】同じ番号の選択肢を1つにまとめます。(例: 5択 + 5択 → 5択、1回の選択で全パーツが変わる)\n名前・アイコンは上にあるメニューが優先されます。",
                MessageType.None);
            EditorGUILayout.LabelField($"統合後の選択肢数: {total}");

            mergeFillMissing = EditorGUILayout.Toggle("未定義の選択肢を現在のマテリアルで補完", mergeFillMissing);
            mergeAddMAMenuInstaller = EditorGUILayout.Toggle("MAMenuInstallerを追加する", mergeAddMAMenuInstaller);
            mergeSourceAction = (SourceAction)EditorGUILayout.EnumPopup("統合元メニューの扱い", mergeSourceAction);

            EditorGUILayout.Space(15);

            using (new EditorGUI.DisabledScope(valid.Count < 2 || mergeAvatar == null))
            {
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("マージ実行", GUILayout.Height(40)))
                    ExecuteMerge();
                GUI.backgroundColor = Color.white;
            }
        }

        private void HandleMergeDrop(Rect dropArea)
        {
            Event evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            if (!dropArea.Contains(evt.mousePosition)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                    AddMergeSource(obj);
            }
            evt.Use();
        }

        private void AddMergeSource(UnityEngine.Object obj)
        {
            IEnumerable<AvatarChooseMenuCreator> found = obj switch
            {
                AvatarChooseMenuCreator c => new[] { c },
                GameObject go => go.GetComponents<AvatarChooseMenuCreator>(),
                _ => System.Array.Empty<AvatarChooseMenuCreator>()
            };
            foreach (var c in found)
                if (!mergeSources.Contains(c)) mergeSources.Add(c);
        }

        private static int GetChooseCount(AvatarChooseMenuCreator src)
        {
            if (src == null) return 0;
            var p = new SerializedObject(src).FindProperty("AvatarChooseMenu.ChooseCount");
            return p != null ? p.intValue : 0;
        }

        // ───────────────────────── マージ処理 ─────────────────────────

        private void ExecuteMerge()
        {
            var sources = mergeSources.Where(s => s != null).Distinct().ToList();

            if (mergeAvatar == null)
            {
                EditorUtility.DisplayDialog("エラー", "対象アバターを指定してください", "OK");
                return;
            }
            if (sources.Count < 2)
            {
                EditorUtility.DisplayDialog("エラー", "統合元メニューを2つ以上指定してください", "OK");
                return;
            }
            if (sources.Any(s => !s.transform.IsChildOf(mergeAvatar.transform)))
            {
                EditorUtility.DisplayDialog("エラー",
                    "対象アバター外のメニューが含まれています。\nRendererパスはアバターからの相対パスなので、同じアバター内のメニューのみ統合できます。", "OK");
                return;
            }
            if (mergeSourceAction == SourceAction.Delete &&
                !EditorUtility.DisplayDialog("確認", "統合元メニューのGameObjectを削除します。よろしいですか?(Undo可)", "はい", "いいえ"))
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Merge AvatarChooseMenuCreator");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                var datas = sources.Select(ReadMenu).ToList();

                var mats = new Dictionary<(string path, int slot), Dictionary<int, UnityEngine.Object>>();
                var names = new Dictionary<int, string>();
                var icons = new Dictionary<int, UnityEngine.Object>();
                int total, conflicts = 0;

                if (mergeMode == MergeMode.Append)
                {
                    int offset = 0;
                    foreach (var d in datas)
                    {
                        foreach (var kv in d.materials)
                        {
                            if (!mats.TryGetValue(kv.Key, out var dict))
                                mats[kv.Key] = dict = new Dictionary<int, UnityEngine.Object>();
                            foreach (var m in kv.Value) dict[offset + m.Key] = m.Value;
                        }
                        foreach (var n in d.names) names[offset + n.Key] = n.Value;
                        foreach (var ic in d.icons) icons[offset + ic.Key] = ic.Value;
                        offset += d.count;
                    }
                    total = offset;
                }
                else
                {
                    total = datas.Max(d => d.count);
                    foreach (var d in datas)
                    {
                        foreach (var kv in d.materials)
                        {
                            if (!mats.TryGetValue(kv.Key, out var dict))
                                mats[kv.Key] = dict = new Dictionary<int, UnityEngine.Object>();
                            foreach (var m in kv.Value)
                            {
                                if (dict.TryGetValue(m.Key, out var existing) && existing != m.Value) conflicts++;
                                dict[m.Key] = m.Value;
                            }
                        }
                        foreach (var n in d.names) if (!names.ContainsKey(n.Key)) names[n.Key] = n.Value;
                        foreach (var ic in d.icons) if (!icons.ContainsKey(ic.Key) && ic.Value != null) icons[ic.Key] = ic.Value;
                    }
                }

                // 名前の整形 (欠けている名前の補完・数字のみなら振り直し・重複回避)
                for (int i = 0; i < total; i++)
                    if (!names.ContainsKey(i)) names[i] = (i + 1).ToString();

                if (names.Values.All(n => int.TryParse(n, out _)))
                {
                    for (int i = 0; i < total; i++) names[i] = (i + 1).ToString();
                }
                else
                {
                    var used = new HashSet<string>();
                    for (int i = 0; i < total; i++)
                    {
                        string original = names[i], unique = original;
                        int c = 2;
                        while (!used.Add(unique)) unique = $"{original}{c++}";
                        names[i] = unique;
                    }
                }

                // 未定義の選択肢を現在のマテリアルで補完
                if (mergeFillMissing)
                {
                    foreach (var kv in mats)
                    {
                        var t = mergeAvatar.transform.Find(kv.Key.path);
                        var r = t != null ? t.GetComponent<Renderer>() : null;
                        if (r == null) continue;
                        var current = r.sharedMaterials;
                        if (kv.Key.slot >= current.Length) continue;
                        for (int i = 0; i < total; i++)
                            if (!kv.Value.ContainsKey(i)) kv.Value[i] = current[kv.Key.slot];
                    }
                }

                // メニューオブジェクト作成
                GameObject menuObject = new(mergedMenuName);
                Undo.RegisterCreatedObjectUndo(menuObject, "Create Merged Menu Object");
                menuObject.transform.SetParent(mergeAvatar.transform);
                menuObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                menuObject.transform.localScale = Vector3.one;

                if (mergeAddMAMenuInstaller)
                    VRC.Core.ExtensionMethods.GetOrAddComponent<ModularAvatarMenuInstaller>(menuObject);

                var creator = Undo.AddComponent<AvatarChooseMenuCreator>(menuObject);
                var so = new SerializedObject(creator);
                var menuProp = so.FindProperty("AvatarChooseMenu")
                    ?? throw new System.Exception("AvatarChooseMenuプロパティが見つかりません。");

                menuProp.FindPropertyRelative("Saved").boolValue = datas[0].saved;
                menuProp.FindPropertyRelative("Synced").boolValue = datas[0].synced;
                menuProp.FindPropertyRelative("ChooseDefaultValue").intValue = Mathf.Clamp(datas[0].defaultValue, 0, total - 1);
                menuProp.FindPropertyRelative("ChooseCount").intValue = total;

                // ChooseMaterials
                var matProp = menuProp.FindPropertyRelative("ChooseMaterials")
                    ?? throw new System.Exception("ChooseMaterialsプロパティが見つかりません。");
                var k1 = matProp.FindPropertyRelative("keys1");
                var k2 = matProp.FindPropertyRelative("keys2");
                var vals = matProp.FindPropertyRelative("values");
                k1.ClearArray(); k2.ClearArray(); vals.ClearArray();

                int idx = 0;
                foreach (var kv in mats)
                {
                    k1.InsertArrayElementAtIndex(idx);
                    k2.InsertArrayElementAtIndex(idx);
                    vals.InsertArrayElementAtIndex(idx);
                    k1.GetArrayElementAtIndex(idx).stringValue = kv.Key.path;
                    k2.GetArrayElementAtIndex(idx).intValue = kv.Key.slot;
                    WriteIntDict(vals.GetArrayElementAtIndex(idx), kv.Value,
                        (p, o) => p.objectReferenceValue = o);
                    idx++;
                }

                WriteIntDict(menuProp.FindPropertyRelative("ChooseNames"), names,
                    (p, s) => p.stringValue = s);
                WriteIntDict(menuProp.FindPropertyRelative("ChooseIcons"), icons,
                    (p, o) => p.objectReferenceValue = o);

                so.ApplyModifiedProperties();

                // 統合元の後処理
                var sourceObjects = sources.Select(s => s.gameObject).Distinct().ToList();
                foreach (var go in sourceObjects)
                {
                    if (mergeSourceAction == SourceAction.Disable)
                    {
                        Undo.RecordObject(go, "Disable Source Menu");
                        go.SetActive(false);
                    }
                    else if (mergeSourceAction == SourceAction.Delete)
                    {
                        Undo.DestroyObjectImmediate(go);
                    }
                }

                Selection.activeGameObject = menuObject;
                EditorGUIUtility.PingObject(menuObject);
                Undo.CollapseUndoOperations(undoGroup);

                EditorUtility.DisplayDialog("成功",
                    $"{mergedMenuName} を作成しました!\n\n" +
                    $"統合元: {sources.Count}個\n" +
                    $"選択肢数: {total}\n" +
                    $"対象Renderer/スロット数: {mats.Count}\n" +
                    (conflicts > 0 ? $"\n※ 同じ番号・同じスロットで {conflicts}件 競合しました(下のメニューで上書き)" : ""),
                    "OK");
            }
            catch (System.Exception e)
            {
                Undo.RevertAllInCurrentGroup();
                EditorUtility.DisplayDialog("エラー", $"マージ中にエラーが発生しました:\n\n{e.Message}", "OK");
                Debug.LogError($"Error merging ChooseMenu: {e}");
            }
        }

        // ───────────────────────── 読み書きヘルパー ─────────────────────────

        private static MergeMenuData ReadMenu(AvatarChooseMenuCreator src)
        {
            var so = new SerializedObject(src);
            var menu = so.FindProperty("AvatarChooseMenu")
                ?? throw new System.Exception($"{src.name}: AvatarChooseMenuプロパティが見つかりません。");

            var data = new MergeMenuData
            {
                saved = menu.FindPropertyRelative("Saved").boolValue,
                synced = menu.FindPropertyRelative("Synced").boolValue,
                defaultValue = menu.FindPropertyRelative("ChooseDefaultValue").intValue,
                count = menu.FindPropertyRelative("ChooseCount").intValue,
            };

            var matProp = menu.FindPropertyRelative("ChooseMaterials");
            var k1 = matProp.FindPropertyRelative("keys1");
            var k2 = matProp.FindPropertyRelative("keys2");
            var vals = matProp.FindPropertyRelative("values");
            for (int i = 0; i < k1.arraySize; i++)
            {
                var dict = ReadIntDict(vals.GetArrayElementAtIndex(i), p => p.objectReferenceValue);
                data.materials[(k1.GetArrayElementAtIndex(i).stringValue, k2.GetArrayElementAtIndex(i).intValue)] = dict;
            }

            data.names = ReadIntDict(menu.FindPropertyRelative("ChooseNames"), p => p.stringValue);
            data.icons = ReadIntDict(menu.FindPropertyRelative("ChooseIcons"), p => p.objectReferenceValue);
            return data;
        }

        private static Dictionary<int, T> ReadIntDict<T>(SerializedProperty dict, System.Func<SerializedProperty, T> getValue)
        {
            var result = new Dictionary<int, T>();
            var keys = dict.FindPropertyRelative("keys");
            var values = dict.FindPropertyRelative("values");
            if (keys == null || values == null) return result;
            for (int i = 0; i < keys.arraySize; i++)
                result[keys.GetArrayElementAtIndex(i).intValue] = getValue(values.GetArrayElementAtIndex(i));
            return result;
        }

        private static void WriteIntDict<T>(SerializedProperty dict, IEnumerable<KeyValuePair<int, T>> items,
            System.Action<SerializedProperty, T> setValue)
        {
            var keys = dict.FindPropertyRelative("keys");
            var values = dict.FindPropertyRelative("values");
            if (keys == null || values == null)
                throw new System.Exception("辞書プロパティ(keys/values)が見つかりません。");

            keys.ClearArray();
            values.ClearArray();
            foreach (var kv in items.OrderBy(x => x.Key))
            {
                int i = keys.arraySize;
                keys.InsertArrayElementAtIndex(i);
                values.InsertArrayElementAtIndex(i);
                keys.GetArrayElementAtIndex(i).intValue = kv.Key;
                setValue(values.GetArrayElementAtIndex(i), kv.Value);
            }
        }
    }
}
#endif