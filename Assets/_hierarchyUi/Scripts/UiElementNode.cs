using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuntimeHierarchy {
	public class TransformNode : UiElementNode<Transform> {
		protected HierarchyUi hierarchy;
		public RectTransform elementTransform;
		public TransformNode(HierarchyUi hierarchy, UiElementNode<Transform> parent, Transform target,
		float col, float row, bool expanded)
			: base(parent, target, col, row, expanded) {
			this.hierarchy = hierarchy;
		}

		public override bool IsTargetActive() => target.gameObject.activeInHierarchy;
		public override string GetTargetName() => target.name;
		public override int GetTargetChildCount() => target.childCount;

		public override float GetTargetHeight() {
			//if (Label != null) {
			//	RectTransform rt = Label.GetComponent<RectTransform>();
			//	return rt.sizeDelta.y;
			//}
			return hierarchy.ElementHeight;
		}
		public override float GetTargetWidth() {
			//if (Label != null) {
			//	RectTransform rt = Label.GetComponent<RectTransform>();
			//	return rt.sizeDelta.x;
			//}
			return hierarchy.ElementWidth;
		}
		public override float GetIndentWidth() => hierarchy.IndentWidth;
	}

	public abstract class UiElementNode<TARGET> {
		[HideInInspector]
		public string name;
		public float column, row, width, height;
		public UiElementNode<TARGET> parent;
		[HideInInspector]
		public TARGET target;
		protected Button _label, _expand;
		public List<UiElementNode<TARGET>> children = new List<UiElementNode<TARGET>>();

		private int _expectedTargetChildren;
		private float _expectedHeight;
		private float _expectedWidth;
		private Action<int> _onChildCountChanged;
		private Action<float> _onHeightChange;
		private Action<float> _onWidthChange;

		/// <summary>
		/// Used to determine if the target has been deleted/removed/cleaned-up
		/// </summary>
		[HideInInspector]
		public bool _markedAsUsed;
		protected bool _expanded;

		public abstract bool IsTargetActive();
		public abstract string GetTargetName();
		public abstract int GetTargetChildCount();
		public abstract float GetTargetHeight();
		public abstract float GetTargetWidth();
		public abstract float GetIndentWidth();

		public Button Label {
			get => _label;
			set {
				_label = value;
				UpdateLabelText();
			}
		}

		public Button Expand {
			get => _expand;
			set {
				_expand = value;
				UpateExpandIcon();
			}
		}

		public bool Expanded {
			get => _expanded;
			set {
				SetExpand(value, GetIndentWidth());
			}
		}


		public void ListenTargetChildCount(Action<int> countChanged) {
			_onChildCountChanged += countChanged;
			IsChildCountChanged();
		}

		public void ListenTargetHeight(Action<float> sizeChanged) {
			_onHeightChange += sizeChanged;
			IsHeightChanged();
		}

		public void ListenTargetWidth(Action<float> sizeChanged) {
			_onWidthChange += sizeChanged;
			IsWidthChanged();
		}

		public void UnlistenTargetChildCount(Action<int> countChanged) { _onChildCountChanged -= countChanged; }

		public void UnlistenTargetHeight(Action<float> sizeChanged) { _onHeightChange -= sizeChanged; }

		public void UnlistenTargetWidth(Action<float> sizeChanged) { _onWidthChange -= sizeChanged; }

		public bool IsChildCountChanged() {
			int count = 0;
			if (_expectedTargetChildren != (count = GetTargetChildCount())) {
				_onChildCountChanged?.Invoke(_expectedTargetChildren = count);
				return true;
			}
			return false;
		}

		public bool IsHeightChanged() {
			float size = 0;
			if (_expectedHeight != (size = GetTargetHeight())) {
				_onHeightChange?.Invoke(_expectedHeight = size);
				return true;
			}
			return false;
		}

		public bool IsWidthChanged() {
			float size = 0;
			if (_expectedWidth != (size = GetTargetWidth())) {
				_onWidthChange?.Invoke(_expectedWidth = size);
				return true;
			}
			return false;
		}

		public void SetExpand(bool value, float indentWidth) {
			if (value != _expanded) {
				RefreshHeight(indentWidth);
			}
			SetExpandedNoNotify(value);
			UpateExpandIcon();
		}

		public void SetExpandedNoNotify(bool value) {
			_expanded = value;
		}

		private void UpateExpandIcon() {
			if (_expand == null) { return; }
			TMP_Text txt = _expand.GetComponentInChildren<TMP_Text>();
			txt.text = _expanded ? "v" : ">";
		}

		public void UpdateLabelText() {
			if (_label == null) { return; }
			TMP_Text text = _label.GetComponentInChildren<TMP_Text>();
			text.text = name;
			Color textColor = text.color;
			textColor.a = target == null || IsTargetActive() ? 1 : 0.5f;
			text.color = textColor;
		}

		public UiElementNode(UiElementNode<TARGET> parent, TARGET target, float column, float row, bool expanded) {
			this.target = target;
			this.parent = parent;
			this.column = column;
			this.row = row;
			_expanded = expanded;
			_expectedTargetChildren = (target != null) ? GetTargetChildCount() : 0;
			RefreshName();
		}

		public void RefreshName() {
			name = (target != null) ? GetTargetName() : "";
		}

		public void RefreshHeight(float indentWidth) {
			int depth = 0;
			float width = 0;
			GetRoot().CalculateDimensions(0, ref depth, row, indentWidth, ref width);
		}

		private UiElementNode<TARGET> GetRoot() {
			UiElementNode<TARGET> root = this;
			int loopguard = 0;
			while (root.parent != null) {
				root = root.parent;
				if (++loopguard > 100000) {
					List<UiElementNode<TARGET>> path = new List<UiElementNode<TARGET>> ();
					bool recursed = FindParentRecursion(this, path);
					if (recursed) {
						throw new System.Exception($"{name} root max depth reached, recursion at depth {path.Count}");
					}
				}
			}
			return root;
		}

		private bool FindParentRecursion(UiElementNode<TARGET> cursor, List<UiElementNode<TARGET>> path) {
			if (path.Contains(cursor)) { return true; }
			path.Add(cursor);
			if (cursor.parent == null) { return false; }
			return FindParentRecursion(cursor.parent, path);
		}

		public float CalculateDimensions(int depth, ref int maxDepth, float maintainRowsBefore,
		float indentWidth, ref float maxWidth) {
			if (depth > maxDepth) {
				maxDepth = depth;
			}
			height = target != null || !string.IsNullOrEmpty(name) ? GetTargetHeight() : 0;
			if (children == null || children.Count == 0 || !_expanded) {
				return height;
			}
			float rowCursor = row + height;
			if (target != null) {
				depth += 1;
			}
			for (int i = 0; i < children.Count; i++) {
				UiElementNode<TARGET> node = children[i];
				node.row = rowCursor;
				node.column = depth;
				node.width = (indentWidth * (depth + 1)) + node.GetTargetWidth();
				if (node.width > maxWidth) {
					maxWidth = node.width;
				}
					float elementHeight = rowCursor < maintainRowsBefore
				? node.height
				: node.CalculateDimensions(depth, ref maxDepth, row, indentWidth, ref maxWidth);
				height += elementHeight;
				rowCursor += elementHeight;
			}
			return height;
		}
	}
}
