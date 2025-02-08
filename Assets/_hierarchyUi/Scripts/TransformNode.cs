using UnityEngine;

namespace RuntimeHierarchy {
	public class TransformNode : UiElementNode<Transform> {
		protected UiHierarchy<Transform> hierarchy;
		public RectTransform elementTransform;
		public TransformNode(UiHierarchy<Transform> hierarchy, UiElementNode<Transform> parent, Transform target,
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
			return hierarchy.ElementHeightDefault;
		}
		public override float GetTargetWidth() {
			//if (Label != null) {
			//	RectTransform rt = Label.GetComponent<RectTransform>();
			//	return rt.sizeDelta.x;
			//}
			return hierarchy.ElementWidthDefault;
		}
		public override float GetIndentWidth() => hierarchy.IndentWidth;
	}
}