using UnityEditor;
using UnityEngine;

namespace Nib.ProcTree.Editor
{
    /// <summary>
    /// Hierarchy right-click entry (PACKAGE_STANDARDS §4): GameObject ▸ Nib ▸ Proc Tree ▸ Oak.
    /// Places the fully wired Oak prefab, so the tree starts growing in immediately.
    /// </summary>
    public static class ProcTreeMenu
    {
        [MenuItem("GameObject/Nib/Proc Tree/Oak", false, 10)]
        static void CreateOakMenu(MenuCommand cmd) => CreateOak(cmd.context as GameObject);

        /// <summary>Instantiates the Oak prefab under <paramref name="parent"/> (or the scene root).</summary>
        public static GameObject CreateOak(GameObject parent = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProcTreeSetup.OakPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[ProcTree] Oak prefab not found at '{ProcTreeSetup.OakPrefabPath}'. Run Tools ▸ Proc Tree ▸ Setup Project.");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObjectUtility.SetParentAndAlign(go, parent);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, "Create Oak");
            Selection.activeObject = go;
            return go;
        }
    }
}
