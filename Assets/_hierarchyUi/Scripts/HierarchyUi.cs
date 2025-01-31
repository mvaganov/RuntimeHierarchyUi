using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RuntimeHierarchy {
	/// <summary>
	/// creates efficient UI for a hierarchy at runtime
	/// </summary>
	[ExecuteInEditMode]
	public class HierarchyUi : MonoBehaviour {
		[System.Serializable] public class UnityEvent_Transform : UnityEvent<Transform> { }
		/// <summary>
		/// Where the hierarchy UI will be generated dynamically
		/// </summary>
		public ContentSizeFitter contentPanel;
		/// <summary>
		/// cached transform, for faster parenting of hierarchy components
		/// </summary>
		private Transform _contentPanelTransform;
		/// <summary>
		/// size to set for the content area
		/// </summary>
		private Vector2 _contentSize;
		/// <summary>
		/// how a hierarchy element should look/act
		/// </summary>
		public Button prefabElement;
		/// <summary>
		/// how the expand/collapse button should look. expected to have a text element, which will be set to ">" or "v"
		/// </summary>
		public Button prefabExpand;
		/// <summary>
		/// callback when a hierarchy element is selected
		/// </summary>
		public UnityEvent_Transform onElementSelect;
		/// <summary>
		/// optimization for element button UI
		/// </summary>
		[SerializeField, HideInInspector]
		private ButtonPool elementPool = new ButtonPool();
		/// <summary>
		/// optimization for expand/collapse button UI
		/// </summary>
		[SerializeField, HideInInspector]
		private ButtonPool expandPool = new ButtonPool();
		/// <summary>
		/// scroll view expected for the hierarchy UI
		/// </summary>
		private ScrollRect _scrollView;
		/// <summary>
		/// element state tree
		/// </summary>
		private TransformNode _root;
		/// <summary>
		/// calculated area where hierarchy elements and expand/collapse UI should be generated
		/// </summary>
		private Rect _cullBox;
		/// <summary>
		/// value of the <see cref="_cullBox"/> used to calculate element values in <see cref="_root"/>
		/// </summary>
		private Rect _usedCullBox;
		/// <summary>
		/// how tall each clickable hierarchy element is
		/// </summary>
		private float _elementHeight;
		/// <summary>
		/// how wide each clickable hierarchy element is
		/// </summary>
		private float _elementWidth;
		/// <summary>
		/// how wide each clickable expand/collapse element is (height is the same as <see cref="_elementHeight"/>
		/// </summary>
		private float _indentWidth;
		/// <summary>
		/// cached <see cref="TransformNode"/>s by hierarchy <see cref="Transform"/>. used to reuse allocations
		/// </summary>
		private Dictionary<Transform, TransformNode> elementStates = new Dictionary<Transform, TransformNode>();
		/// <summary>
		/// cached value of how many elements are in each scene. used to quickly determine if new objects were added/removed
		/// </summary>
		private int[] expectedElementsAtSceneRoot;

		public float IndentWidth => _indentWidth;
		public float ElementWidth => _elementWidth;
		public float ElementHeight => _elementHeight;

		protected void Update() {
			if (_root == null) {
				RefreshHierarchyState(true);
			}
			CalcCullBox();
			if (_usedCullBox != _cullBox) {
				RefreshUiElements();
			}
		}

		/// <summary>
		/// will force refresh next frame
		/// </summary>
		public void RequestRefresh() {
			_usedCullBox = new Rect(Vector2.zero, -Vector2.one);
		}

		[ContextMenu(nameof(ClearUi))]
		public void ClearUi() {
			Transform uiElement = contentPanel.transform;
			for (int i = uiElement.childCount-1; i >= 0; --i) {
#if UNITY_EDITOR
				if (!Application.isPlaying) {
					DestroyImmediate(uiElement.GetChild(i).gameObject);
				} else
#endif
				{
					Destroy(uiElement.GetChild(i).gameObject);
				}
			}
#if UNITY_EDITOR
			if (!Application.isPlaying) {
				UnityEditor.EditorUtility.SetDirty(this);
			}
#endif
		}

		/// <summary>
		/// totally rebuilds the hierarchy UI, including refreshing data state
		/// </summary>
		[ContextMenu(nameof(RebuildHierarchy))]
		public void RebuildHierarchy() {
			RefreshHierarchyState(true);
			CalcCullBox();
			RefreshUiElements();
		}

		/// <summary>
		/// determines which elements of the hierarchy should exist
		/// </summary>
		/// <returns></returns>
		[ContextMenu(nameof(CalcCullBox))]
		protected Rect CalcCullBox() {
			if (_scrollView == null) {
				_scrollView = GetComponentInChildren<ScrollRect>();
			}
			const float bevel = -10;
			Vector2 viewSize = _scrollView.viewport.rect.size;
			Vector2 contentSize = _scrollView.content.sizeDelta;
			Vector2 offset = _scrollView.normalizedPosition;
			offset.y = 1 - offset.y;
			offset.x *= (contentSize.x - viewSize.x);
			offset.y *= (contentSize.y - viewSize.y);
			offset += new Vector2(bevel, bevel);
			_cullBox = new Rect(offset, viewSize - new Vector2(bevel * 2, bevel * 2));
			return _cullBox;
		}

		private void RefreshHierarchyState(bool expanded) {
			TransformNode oldRoot = _root;
			List<Transform>[] objectsPerScene = GetAllRootElementsByScene(out string[] sceneNames);
			expectedElementsAtSceneRoot = new int[objectsPerScene.Length];
			_root = new TransformNode(this, null, null, 0, 0, expanded);
			MarkCurrentElementsAsUnusedUntilTheyAreFoundByGetElementStateEntry();
			for (int sceneIndex = 0; sceneIndex < objectsPerScene.Length; ++sceneIndex) {
				List<Transform> list = objectsPerScene[sceneIndex];
				if (sceneIndex < SceneManager.sceneCount) {
					expectedElementsAtSceneRoot[sceneIndex] = SceneManager.GetSceneAt(sceneIndex).rootCount;
				}
				TransformNode sceneStateNode = new TransformNode(this, _root, null, 0, 0, expanded);
				sceneStateNode.name = sceneNames[sceneIndex];
				_root.children.Add(sceneStateNode);
				for (int i = 0; i < list.Count; ++i) {
					TransformNode es = GetElementStateEntry(sceneStateNode, list[i], 0, i, expanded);
					sceneStateNode.children.Add(es);
					AddChildrenStates(es, expanded);
				}
			}
			RemoveUnusedElementsNotFoundByGetElementStateEntry();
		}

		private void MarkCurrentElementsAsUnusedUntilTheyAreFoundByGetElementStateEntry() {
			foreach (var item in elementStates) {
				item.Value._markedAsUsed = false;
			}
		}

		private void RemoveUnusedElementsNotFoundByGetElementStateEntry() {
			List<Transform> removedTransforms = new List<Transform>();
			foreach (var item in elementStates) {
				if (!item.Value._markedAsUsed) {
					removedTransforms.Add(item.Key);
				}
			}
			removedTransforms.ForEach(t => elementStates.Remove(t));
		}

		private TransformNode GetElementStateEntry(TransformNode parent, Transform target, float column, float row, bool expanded) {
			if (!elementStates.TryGetValue(target, out TransformNode value)) {
				value = new TransformNode(this, parent, target, column, row, expanded);
				elementStates[target] = value;
			} else {
				value.parent = parent;
				value.column = column;
				value.row = row;
				value.children.Clear();
				value.RefreshName();
				value._expectedTargetChildren = (target != null) ? target.childCount : 0;
			}
			value._markedAsUsed = true;
			return value;
		}

		private void AddChildrenStates(TransformNode self, bool expanded) {
			float c = self.column + 1;
			float r = self.row + 1;
			for (int i = 0; i < self.target.childCount; ++i) {
				Transform t = self.target.GetChild(i);
				if (t == null || t.GetComponent<HierarchyIgnore>() != null) {
					continue;
				}
				TransformNode es = GetElementStateEntry(self, t, c, r, expanded);
				self.children.Add(es);
				AddChildrenStates(es, expanded);
			}
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

		/// <summary>
		/// refreshes UI elements without refreshing the underlying hierarchy data
		/// </summary>
		[ContextMenu(nameof(RefreshUiElements))]
		private void RefreshUiElements() {
			if (contentPanel == null || prefabElement == null || prefabExpand == null) { return; }
			DetectHierarchyChangeAndRecalculateIfNeeded();
			CalculateHierarchySize();
			FreeCurrentUiElements();
			_contentPanelTransform = contentPanel.transform; // cache content transform
			CreateAllChildren(_root);
			_usedCullBox = _cullBox;
		}

		private void DetectHierarchyChangeAndRecalculateIfNeeded() {
			bool mustRefreshData = IsSceneCountChanged() || IsRootElementCountChanged()
				|| IsElementMissingOrChildElementCountChanged();
			if (mustRefreshData) {
				RefreshHierarchyState(true);
			}
		}

		private bool IsSceneCountChanged() {
			if (expectedElementsAtSceneRoot == null || SceneManager.sceneCount > expectedElementsAtSceneRoot.Length) {
				//Debug.Log($"scene count changed! Expected {expectedElementsAtSceneRoot.Length}, have {SceneManager.sceneCount}");
				return true;
			}
			return false;
		}

		private bool IsRootElementCountChanged() {
			for (int i = 0; i < SceneManager.sceneCount; ++i) {
				int expectedElements = SceneManager.GetSceneAt(i).rootCount;
				if (expectedElements != expectedElementsAtSceneRoot[i]) {
					//Debug.Log($"root element count change in {SceneManager.GetSceneAt(i).name}");
					return true;
				}
			}
			return false;
		}

		private bool IsElementMissingOrChildElementCountChanged() {
			foreach (var item in elementStates) {
				TransformNode es = item.Value;
				if (item.Key == null) {
					//Debug.Log($"missing {es.name}");
					return true;
				}
				if (es.target.childCount != es._expectedTargetChildren) {
					//Debug.Log($"child count {es.name}: {es.target.childCount} vs {es._expectedTargetChildren}");
					return true;
				}
			}
			return false;
		}

		[ContextMenu(nameof(CalculateHierarchySize))]
		private void CalculateHierarchySize() {
			if (prefabElement == null || prefabExpand == null) {
				return;
			}
			_elementHeight = prefabElement.GetComponent<RectTransform>().sizeDelta.y;
			_elementWidth = prefabElement.GetComponent<RectTransform>().sizeDelta.x;
			_indentWidth = prefabExpand.GetComponent<RectTransform>().sizeDelta.x;
			int depth = 0;
			float maxWidth = 0;
			_root.CalculateDimensions(0, ref depth, 0, IndentWidth, ref maxWidth);
			_contentSize.y = _root.height;// * _elementHeight;
			_contentSize.x = maxWidth;// (depth + 1) * indentWidth + elementWidth;
			RectTransform rt = contentPanel.GetComponent<RectTransform>();
			rt.sizeDelta = _contentSize;
		}

		private void FreeCurrentUiElements() {
			elementPool.FreeAllElementFromPools();
			expandPool.FreeAllElementFromPools();
		}

		private void CreateAllChildren(UiElementNode<Transform> es) {
			if (!es.Expanded) { return; }
			for (int i = 0; i < es.children.Count; i++) {
				UiElementNode<Transform> child = es.children[i];
				if (child.target != null && child.target.GetComponent<HierarchyIgnore>() != null) {
					continue;
				}
				//Debug.Log($"{i} creating {child.name}");
				CreateElement(child, true);
				CreateAllChildren(child);
			}
		}

		private void CreateElement(UiElementNode<Transform> es, bool cullOffScreen) {
			bool isARealElement = es.target != null;
			Vector2 cursor = new Vector2(_indentWidth * es.column, es.row);
			Vector2 anchoredPosition = new Vector2(cursor.x, -cursor.y);
			Vector2 elementPosition = anchoredPosition + (isARealElement ? Vector2.right * _indentWidth : Vector2.zero);
			Rect expandRect = new Rect(cursor, new Vector2(_indentWidth, _indentWidth));
			Vector2 elementSize = new Vector2(es.GetTargetWidth(), es.GetTargetHeight());
			//Debug.Log($"{es.name}: {cursor}  {elementSize}");
			Rect elementRect = new Rect(cursor + Vector2.right * _indentWidth, elementSize);
			bool createdExpandButton = false;
			if (isARealElement && (!cullOffScreen || _cullBox.Overlaps(expandRect))) {
				createdExpandButton = CreateExpandButton(es, anchoredPosition);
			}
			if (!createdExpandButton) {
				es.Expand = null;
			}
			if (!cullOffScreen || _cullBox.Overlaps(elementRect)) {
				CreateElementButton(es, elementPosition);
			} else {
				es.Label = null;
			}
		}

		private bool CreateExpandButton(UiElementNode<Transform> es, Vector2 anchoredPosition) {
			if (es.children.Count == 0) {
				return false;
			}
			Button expand = expandPool.GetFreeFromPools(prefabExpand, es.Expand);
			RectTransform rt = expand.GetComponent<RectTransform>();
			rt.SetParent(_contentPanelTransform, false);
			rt.anchoredPosition = anchoredPosition;
			rt.name = $"> {es.name}";
			AddToggleExpand(es, expand);
			es.Expand = expand;
			return true;
		}

		private void AddToggleExpand(UiElementNode<Transform> es, Button expand) {
			expand.onClick.RemoveAllListeners();
			expand.onClick.AddListener(() => ToggleExpand(es));
		}

		private void CreateElementButton(UiElementNode<Transform> es, Vector2 elementPosition) {
			Button element = elementPool.GetFreeFromPools(prefabElement, es.Label);
			RectTransform rt = element.GetComponent<RectTransform>();
			rt.SetParent(_contentPanelTransform, false);
			rt.anchoredPosition = elementPosition;
			rt.name = $"({es.name})";
			es.UpdateLabelText();
			if (es.target != null) {
				AddSelectElement(es, element);
			} else {
				AddToggleExpand(es, element);
			}
			es.Label = element;
			Image img = element.GetComponent<Image>();
			img.enabled = (es.target != null);
		}

		private void AddSelectElement(UiElementNode<Transform> es, Button element) {
			element.onClick.RemoveAllListeners();
			element.onClick.AddListener(() => SelectElement(es));
		}

		private void ToggleExpand(UiElementNode<Transform> es) {
			//Debug.Log($"toggle {es.name}");
			es.Expanded = !es.Expanded;
			RefreshUiElements();
		}

		private void SelectElement(UiElementNode<Transform> es) {
			Debug.Log($"selected {es.name} ({es.target})");
			onElementSelect.Invoke(es.target);
		}
	}
}
