using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
	// TODO hide?
	public ButtonPool elementPool = new ButtonPool();
	// TODO hide?
	public ButtonPool expandPool = new ButtonPool();
	/// <summary>
	/// scroll view expected for the hierarchy UI
	/// </summary>
	private ScrollRect _scrollView;
	/// <summary>
	/// element state tree
	/// </summary>
	private ElementState _root;
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
	private float elementWidth;
	/// <summary>
	/// how wide each clickable expand/collapse element is (height is the same as <see cref="_elementHeight"/>
	/// </summary>
	private float indentWidth;
	/// <summary>
	/// cached <see cref="ElementState"/>s by hierarchy <see cref="Transform"/>. used to reuse allocations
	/// </summary>
	private Dictionary<Transform,ElementState> elementStates = new Dictionary<Transform, ElementState>();
	/// <summary>
	/// cached value of how many elements are in each scene. used to quickly determine if new objects were added/removed
	/// </summary>
	private int[] expectedElementsAtSceneRoot;

	protected void Update() {
		if (_root == null) {
			RefreshHierarchyState(true);
		}
		CalcCullBox();
		if (_usedCullBox != _cullBox) {
			RefreshUiElements();
		}
	}

	protected void OnValidate() {
		if (contentPanel == null) { return; }
		RectTransform rt = contentPanel.GetComponent<RectTransform>();
		rt.sizeDelta = _contentSize;
	}

	[ContextMenu(nameof(RebuildHierarchy))]
	public void RebuildHierarchy() {
		RefreshHierarchyState(true);
		CalcCullBox();
		RefreshUiElements();
	}

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
		ElementState oldRoot = _root;
		List<Transform>[] objectsPerScene = GetAllRootElementsByScene(out string[] sceneNames);
		expectedElementsAtSceneRoot = new int[objectsPerScene.Length];
		_root = new ElementState(null, null, 0, 0, expanded);
		MarkCurrentElementsAsUnusedUntilTheyAreFoundByGetElementStateEntry();
		for (int sceneIndex = 0; sceneIndex < objectsPerScene.Length; ++sceneIndex) {
			List<Transform> list = objectsPerScene[sceneIndex];
			if (sceneIndex < SceneManager.sceneCount) {
				expectedElementsAtSceneRoot[sceneIndex] = SceneManager.GetSceneAt(sceneIndex).rootCount;
			}
			ElementState sceneStateNode = new ElementState(_root, null, 0, 0, expanded);
			sceneStateNode.name = sceneNames[sceneIndex];
			for (int i = 0; i < list.Count; ++i) {
				ElementState es = GetElementStateEntry(sceneStateNode, list[i], 0, i, expanded);
				_root.children.Add(es);
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

	private ElementState GetElementStateEntry(ElementState parent, Transform target, int column, int row, bool expanded) {
		if (!elementStates.TryGetValue(target, out ElementState value)) {
			value = new ElementState(parent, target, column, row, expanded);
			elementStates[target] = value;
		} else {
			value.parent = parent;
			value.children.Clear();
			value.RefreshName();
			value._expectedTargetChildren = (target != null) ? target.childCount : 0;
		}
		value._markedAsUsed = true;
		return value;
	}

	public void AddChildrenStates(ElementState self, bool expanded) {
		int c = self.column + 1;
		int r = self.row + 1;
		for (int i = 0; i < self.target.childCount; ++i) {
			Transform t = self.target.GetChild(i);
			if (t == null || t.GetComponent<HierarchyIgnore>() != null) {
				continue;
			}
			ElementState es = GetElementStateEntry(self, t, c, r, expanded);
			self.children.Add(es);
			AddChildrenStates(es, expanded);
		}
	}

	public static List<Transform>[] GetAllRootElementsByScene(out string[] sceneNames) {
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
		foreach(var kvp in sceneIndexByName) {
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
		if (SceneManager.sceneCount > expectedElementsAtSceneRoot.Length) {
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
			ElementState es = item.Value;
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
		elementWidth = prefabElement.GetComponent<RectTransform>().sizeDelta.x;
		indentWidth = prefabExpand.GetComponent<RectTransform>().sizeDelta.x;
		int depth = 0;
		_root.CalculateHeight(0, ref depth, 0);
		_contentSize.y = _root.height * _elementHeight;
		_contentSize.x = (depth + 1) * indentWidth + elementWidth;
		RectTransform rt = contentPanel.GetComponent<RectTransform>();
		rt.sizeDelta = _contentSize;
	}

	private void FreeCurrentUiElements() {
		elementPool.FreeAllElementFromPools();
		expandPool.FreeAllElementFromPools();
	}

	private void CreateAllChildren(ElementState es) {
		if (!es.Expanded) { return; }
		for (int i = 0; i < es.children.Count; i++) {
			ElementState child = es.children[i];
			if (child.target != null && child.target.GetComponent<HierarchyIgnore>() != null) {
				continue;
			}
			//Debug.Log($"{i} creating {child.name}");
			CreateElement(child, true);
			CreateAllChildren(child);
		}
	}

	private void CreateElement(ElementState es, bool cullOffScreen) {
		Vector2 cursor = new Vector2(indentWidth * es.column, _elementHeight * es.row);
		Vector2 anchoredPosition = new Vector2(cursor.x, -cursor.y);
		Vector2 elementPosition = anchoredPosition + Vector2.right * indentWidth;
		RectTransform rt;
		Rect expandRect = new Rect(cursor, new Vector2(indentWidth, _elementHeight));
		Rect elementRect = new Rect(cursor + Vector2.right * indentWidth, new Vector2(elementWidth, _elementHeight));
		if (!cullOffScreen || _cullBox.Overlaps(expandRect)) {
			if (es.children.Count > 0) {
				Button expand = expandPool.GetFreeFromPools(prefabExpand, es.Expand);
				rt = expand.GetComponent<RectTransform>();
				rt.SetParent(_contentPanelTransform, false);
				rt.anchoredPosition = anchoredPosition;
				rt.name = $"> {es.name}";
				expand.onClick.RemoveAllListeners();
				expand.onClick.AddListener(() => ToggleExpand(es));
				es.Expand = expand;
			} else {
				es.Expand = null;
			}
		}
		if (!cullOffScreen || _cullBox.Overlaps(elementRect)) {
			Button element = elementPool.GetFreeFromPools(prefabElement, es.Label);
			rt = element.GetComponent<RectTransform>();
			rt.SetParent(_contentPanelTransform, false);
			rt.anchoredPosition = elementPosition;
			rt.name = $"({es.name})";
			element.onClick.RemoveAllListeners();
			element.onClick.AddListener(() => SelectElement(es));
			es.Label = element;
		} else {
			es.Label = null;
		}
	}

	private void ToggleExpand(ElementState es) {
		//Debug.Log($"toggle {es.name}");
		es.Expanded = !es.Expanded;
		RefreshUiElements();
	}

	private void SelectElement(ElementState es) {
		Debug.Log($"selected {es.name}");
		onElementSelect.Invoke(es.target);
	}
}
