using UnityEngine;

namespace RuntimeHierarchy {
	public class DontDestroyOnLoad : MonoBehaviour {
		private void Awake() {
			DontDestroyOnLoad(this);
		}
	}
}
