using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    public class CoroutineHost : MonoBehaviour
    {
        private static CoroutineHost _instance;

        public static CoroutineHost Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("BeatSurgeon_CoroutineHost");
                    Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CoroutineHost>();
                }
                return _instance;
            }
        }
    }
}
