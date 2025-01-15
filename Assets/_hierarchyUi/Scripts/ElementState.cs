using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuntimeHierarchy {
	public class ElementState {
		[HideInInspector]
		public string name;
		public int column, row, height;
		[HideInInspector]
		public int _expectedTargetChildren;
		public ElementState parent;
		[HideInInspector]
		public Transform target;
		private Button _label, _expand;
		public List<ElementState> children = new List<ElementState>();
		private bool _expanded;
		[HideInInspector]
		public bool _markedAsUsed;

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
				if (value != _expanded) {
					RefreshHeight();
				}
				SetExpandedNoNotify(value);
				UpateExpandIcon();
			}
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
			textColor.a = target == null || target.gameObject.activeInHierarchy ? 1 : 0.5f;
			text.color = textColor;
		}

		public ElementState(ElementState parent, Transform target, int column, int row, bool expanded) {
			this.target = target;
			this.parent = parent;
			this.column = column;
			this.row = row;
			_expanded = expanded;
			_expectedTargetChildren = (target != null) ? target.childCount : 0;
			RefreshName();
		}

		public void RefreshName() {
			name = (target != null) ? target.name : "";
		}

		public void RefreshHeight() {
			int depth = 0;
			GetRoot().CalculateHeight(0, ref depth, row);
		}

		private ElementState GetRoot() {
			ElementState root = this;
			int loopguard = 0;
			while (root.parent != null) {
				root = root.parent;
				if (++loopguard > 100000) {
					throw new System.Exception("max depth reached. find recursion?");
				}
			}
			return root;
		}

		public int CalculateHeight(int depth, ref int maxDepth, int maintainRowsBefore) {
			if (depth > maxDepth) {
				maxDepth = depth;
			}
			height = target != null || !string.IsNullOrEmpty(name) ? 1 : 0;
			if (children == null || children.Count == 0 || !_expanded) {
				return height;
			}
			int rowCursor = row + height;
			if (target != null) {
				depth += 1;
			}
			for (int i = 0; i < children.Count; i++) {
				children[i].row = rowCursor;
				children[i].column = depth;
				int elementHeight = rowCursor < maintainRowsBefore ? children[i].height :
					children[i].CalculateHeight(depth, ref maxDepth, row);
				height += elementHeight;
				rowCursor += elementHeight;
			}
			return height;
		}
	}
}
