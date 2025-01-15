	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.UI;

namespace RuntimeHierarchy {
	[System.Serializable] public class ButtonPool : UiElementPool<Button> { }

	public class UiElementPool<TYPE> where TYPE : Component {
		public List<TYPE> used = new List<TYPE>();
		public HashSet<TYPE> free = new HashSet<TYPE>();

		public TYPE GetFreeFromPools(TYPE prefab, TYPE preferred) {
			TYPE element = null;
			if (preferred != null && free.Contains(preferred)) {
				element = preferred;
				free.Remove(preferred);
			}
			if (element == null) {
				using (HashSet<TYPE>.Enumerator enumerator = free.GetEnumerator()) {
					while (enumerator.MoveNext() && element == null) {
						element = enumerator.Current;
					}
				}
				if (element != null) {
					free.Remove(element);
				}
			}
			if (element == null) {
				element = GameObject.Instantiate(prefab.gameObject).GetComponent<TYPE>();
			}
			used.Add(element);
			element.gameObject.SetActive(true);
			return element;
		}

		public void FreeWithPools(TYPE element) {
			if (!used.Remove(element)) {
				throw new System.Exception("freeing unused element");
			}
			element.gameObject.SetActive(false);
			free.Add(element);
		}

		public void FreeAllElementFromPools() {
			used.ForEach(b => {
				if (b != null) {
					b.gameObject.SetActive(false);
				}
			});
			used.ForEach(e => free.Add(e));
			used.Clear();
		}
	}
}
