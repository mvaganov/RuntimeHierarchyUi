using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RuntimeHierarchy {
	/// <summary>
	/// creates efficient UI for a hierarchy at runtime
	/// </summary>
	public abstract class UiHierarchy<TARGET> : MonoBehaviour where TARGET : class {
		[System.Serializable] public class UnityEvent_ : UnityEvent<TARGET> { }
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
		public UnityEvent_ onElementSelect;
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
		private UiElementNode<TARGET> _root;
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
		/// cached nodes, by hierarchy <see cref="TARGET"/>. used to reuse state/allocations
		/// </summary>
		private Dictionary<TARGET, UiElementNode<TARGET>> elementStates = new Dictionary<TARGET, UiElementNode<TARGET>>();
		/// <summary>
		/// cached value of how many elements are in each scene. used to quickly determine if new objects were added/removed
		/// TODO move this to TransformHierarchyUi.
		/// </summary>
		protected int[] expectedElementsAtSceneRoot;
		/// <summary>
		/// can force recalculation/redraw
		/// </summary>
		private bool _dirty = true;

		public float IndentWidth => _indentWidth;
		public float ElementWidthDefault => _elementWidth;
		public float ElementHeightDefault => _elementHeight;

		protected abstract UiElementNode<TARGET> CreateNode(UiElementNode<TARGET> parent, TARGET target, float col, float row, bool expanded);
		protected abstract UiElementNode<TARGET> GenerateGraph(bool expanded);

		protected void Start() {
			ClearUi();
		}

		protected void Update() {
#if UNITY_EDITOR
			if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null) {
				return;
			}
#endif
			if (_root == null) {
				RefreshHierarchyData(true);
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

		public virtual void ClearUi() {
			Transform uiElement = contentPanel.transform;
			for (int i = uiElement.childCount - 1; i >= 0; --i) {
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
		public virtual void RebuildHierarchy() {
			RefreshHierarchyData(true);
			CalcCullBox();
			RefreshUiElements();
		}

		/// <summary>
		/// determines which elements of the hierarchy should exist
		/// </summary>
		/// <returns></returns>
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

		private void RefreshHierarchyData(bool expanded) {
			UiElementNode<TARGET> oldRoot = _root;
			MarkCurrentElementsAsUnusedUntilTheyAreFoundByGetElementStateEntry();
			_root = GenerateGraph(expanded);
			RemoveUnusedElementsNotFoundByGetElementStateEntry();
		}

		private void MarkCurrentElementsAsUnusedUntilTheyAreFoundByGetElementStateEntry() {
			foreach (var item in elementStates) {
				item.Value._markedAsUsed = false;
			}
		}
		private void RemoveUnusedElementsNotFoundByGetElementStateEntry() {
			List<TARGET> removedTransforms = new List<TARGET>();
			foreach (var item in elementStates) {
				if (!item.Value._markedAsUsed) {
					removedTransforms.Add(item.Key);
				}
			}
			removedTransforms.ForEach(t => elementStates.Remove(t));
		}

		protected UiElementNode<TARGET> GetElementStateEntry(UiElementNode<TARGET> parent, TARGET target, float column, float row, bool expanded) {
			if (!elementStates.TryGetValue(target, out UiElementNode<TARGET> value)) {
				value = CreateNode(parent, target, column, row, expanded);// new TransformNode(this, parent, target, column, row, expanded);
				elementStates[target] = value;
			} else {
				value.Assign(parent, target, column, row, expanded);
			}
			value._markedAsUsed = true;
			return value;
		}

		protected virtual void RefreshUiElements() {
			if (contentPanel == null || prefabElement == null || prefabExpand == null) { return; }
			DetectHierarchyDataChangeAndRecalculateIfNeeded();
			CalculateHierarchySize();
			FreeCurrentUiElements();
			_contentPanelTransform = contentPanel.transform; // cache content transform
			CreateAllChildren(_root);
			_usedCullBox = _cullBox;
		}

		private void DetectHierarchyDataChangeAndRecalculateIfNeeded() {
			bool mustRefreshData = _dirty || IsSceneCountChanged() || IsRootElementCountChanged()
				|| IsElementMissingOrChildElementCountChanged();
			if (mustRefreshData) {
				RefreshHierarchyData(true);
				_dirty = false;
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
				UiElementNode<TARGET> es = item.Value;
				if (item.Key == null) {
					//Debug.Log($"missing {es.name}");
					return true;
				}
				//if (es.target.childCount != es._expectedTargetChildren) {
				if (es.IsChildCountChanged()) {
					//Debug.Log($"child count {es.name}: {es.target.childCount} vs {es._expectedTargetChildren}");
					return true;
				}
			}
			return false;
		}

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
			_contentSize.y = _root.height;
			_contentSize.x = maxWidth;
			RectTransform rt = contentPanel.GetComponent<RectTransform>();
			rt.sizeDelta = _contentSize;
		}

		private void FreeCurrentUiElements() {
			for (int i = 0; i < elementPool.used.Count; i++) {
				Button btn = elementPool.used[i];
				if (btn == null) {
					continue;
				}
			}
			elementPool.FreeAllElementFromPools();
			expandPool.FreeAllElementFromPools();
		}

		private void CreateAllChildren(UiElementNode<TARGET> es) {
			if (!es.Expanded) { return; }
			for (int i = 0; i < es.children.Count; i++) {
				UiElementNode<TARGET> child = es.children[i];
				//Debug.Log($"{i} creating {child.name}");
				if (!CreateElement(child, true)) {
					continue;
				}
				CreateAllChildren(child);
			}
		}

		private bool CreateElement(UiElementNode<TARGET> es, bool cullOffScreen) {
			if (es.target is Transform t && t.GetComponent<HierarchyIgnore>() != null) {
				return false;
			}
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
			return true;
		}

		private bool CreateExpandButton(UiElementNode<TARGET> es, Vector2 anchoredPosition) {
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

		private RectTransform CreateElementButton(UiElementNode<TARGET> es, Vector2 elementPosition) {
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
			img.enabled = (es.target != null || !es.Expanded);
			return rt;
		}

		private void AddSelectElement(UiElementNode<TARGET> es, Button element) {
			element.onClick.RemoveAllListeners();
			element.onClick.AddListener(() => SelectElement(es));
		}

		private void AddToggleExpand(UiElementNode<TARGET> es, Button expand) {
			expand.onClick.RemoveAllListeners();
			expand.onClick.AddListener(() => ToggleExpand(es));
		}

		private void ToggleExpand(UiElementNode<TARGET> es) {
			es.Expanded = !es.Expanded;
			RefreshUiElements();
		}

		private void SelectElement(UiElementNode<TARGET> es) {
			Debug.Log($"selected {es.name} ({es.target})");
			onElementSelect.Invoke(es.target);
		}
	}
}
