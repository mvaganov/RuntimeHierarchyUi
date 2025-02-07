using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuntimeHierarchy {
	/// <summary>
	/// creates efficient UI for a Transform hierarchy
	/// </summary>
	[ExecuteInEditMode]
	public class TransformHierarchyUi : UiHierarchy<Transform> {

		protected override UiElementNode<Transform> CreateNode(UiElementNode<Transform> parent, Transform target, 
		float col, float row, bool expanded) => new TransformNode(this, parent, target, col, row, expanded);

		protected override UiElementNode<Transform> GenerateGraph(bool expanded) {
			List<Transform>[] objectsPerScene = GetAllRootElementsByScene(out string[] sceneNames);
			expectedElementsAtSceneRoot = new int[objectsPerScene.Length];
			UiElementNode<Transform> _root = CreateNode(null, null, 0, 0, expanded);//new TransformNode(this, null, null, 0, 0, expanded);
			for (int sceneIndex = 0; sceneIndex < objectsPerScene.Length; ++sceneIndex) {
				List<Transform> list = objectsPerScene[sceneIndex];
				if (sceneIndex < SceneManager.sceneCount) {
					expectedElementsAtSceneRoot[sceneIndex] = SceneManager.GetSceneAt(sceneIndex).rootCount;
				}
				UiElementNode<Transform> sceneStateNode = CreateNode(_root, null, 0, 0, expanded);//new TransformNode(this, _root, null, 0, 0, expanded);
				sceneStateNode.name = sceneNames[sceneIndex];
				_root.children.Add(sceneStateNode);
				for (int i = 0; i < list.Count; ++i) {
					UiElementNode<Transform> es = GetElementStateEntry(sceneStateNode, list[i], 0, i, expanded);
					sceneStateNode.children.Add(es);
					AddChildrenStates(es, expanded);
				}
			}
			return _root;
		}
		private static List<Transform>[] GetAllRootElementsByScene(out string[] sceneNames) {
			Transform[] allObjects = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			// initialize list of scenes and list of objects per scene
			Dictionary<string, int> sceneIndexByName = new Dictionary<string, int>();
			Dictionary<string, List<Transform>> objectsPerScene = new Dictionary<string, List<Transform>>();
			for (int i = 0; i < SceneManager.sceneCount; ++i) {
				string sceneName = SceneManager.GetSceneAt(i).name;
				sceneIndexByName[sceneName] = i;
				objectsPerScene[sceneName] = new List<Transform>();
			}
			// find root objects for each scene
			for (int i = 0; i < allObjects.Length; ++i) {
				Transform t = allObjects[i];
				if (t != null && t.parent == null && t.GetComponent<HierarchyIgnore>() == null) {
					string sceneName = t.gameObject.scene.name;
					if (sceneName == null) {
						Debug.Log($"should not have found a pure prefab: {t.name}");
						continue;
					}
					if (!sceneIndexByName.TryGetValue(sceneName, out int sceneIndex)) {
						sceneIndexByName[sceneName] = sceneIndex = sceneIndexByName.Count;
					}
					if (!objectsPerScene.TryGetValue(sceneName, out List<Transform> list)) {
						list = new List<Transform>();
						objectsPerScene[sceneName] = list;
					}
					list.Add(t);
				}
			}
			// collapse scene dictionary into an array
			List<Transform>[] resultListOfObjectsByScene = new List<Transform>[objectsPerScene.Count];
			sceneNames = new string[objectsPerScene.Count];
			foreach (var kvp in sceneIndexByName) {
				sceneNames[kvp.Value] = kvp.Key;
			}
			// collapse dictionary of lists of objects into array of lists of objects.
			foreach (var kvp in objectsPerScene) {
				List<Transform> list = kvp.Value;
				list.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
				if (sceneIndexByName.TryGetValue(kvp.Key, out int index)) {
					resultListOfObjectsByScene[index] = list;
				} else {
					Debug.Log($"fail: {kvp.Key} missing from scene list");
				}
			}
			return resultListOfObjectsByScene;
		}

		private void AddChildrenStates(UiElementNode<Transform> self, bool expanded) {
			float c = self.column + 1;
			float r = self.row + 1;
			for (int i = 0; i < self.target.childCount; ++i) {
				Transform t = self.target.GetChild(i);
				if (t == null || t.GetComponent<HierarchyIgnore>() != null) {
					continue;
				}
				UiElementNode<Transform> es = GetElementStateEntry(self, t, c, r, expanded);
				self.children.Add(es);
				AddChildrenStates(es, expanded);
			}
		}

		[ContextMenu(nameof(ClearUi))]
		public override void ClearUi() => base.ClearUi();

		/// <summary>
		/// refreshes UI elements without refreshing the underlying hierarchy data
		/// </summary>
		[ContextMenu(nameof(RefreshUiElements))]
		protected override void RefreshUiElements() => base.RefreshUiElements();

		/// <summary>
		/// totally rebuilds the hierarchy UI, including refreshing data state
		/// </summary>
		[ContextMenu(nameof(RebuildHierarchy))]
		public override void RebuildHierarchy() => base.RebuildHierarchy();
	}
}
