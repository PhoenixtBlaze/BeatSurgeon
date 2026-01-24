using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.FloatingScreen;
using SaberSurgeon.Gameplay;
using SaberSurgeon.UI.Controllers;
using UnityEngine;

namespace SaberSurgeon.UI
{
    public class FloatingChatOverlay : MonoBehaviour
    {
        private FloatingScreen _screen;

        public static FloatingChatOverlay Create()
        {
            var go = new GameObject("SaberSurgeon_FloatingChatOverlay");
            DontDestroyOnLoad(go);
            return go.AddComponent<FloatingChatOverlay>();
        }

        private void Start()
        {
            // Size & position are examples; tweak as desired
            var size = new Vector2(60f, 40f);

            // Create floating screen WITH handle so it can be moved
            _screen = FloatingScreen.CreateFloatingScreen(
                size,
                true,                                  // show handle to drag in space
                new Vector3(0f, 1.4f, 2.0f),          // in front of player
                Quaternion.Euler(0f, 0f, 0f)
            );

            // Use our chat overlay controller so BSML: FloatingChat.bsml is loaded
            var vc = BeatSaberUI.CreateViewController<ChatOverlayViewController>();
            _screen.SetRootViewController(vc, HMUI.ViewController.AnimationType.None);

            // Optional: always face the player
            _screen.gameObject.AddComponent<LookAtCamera>();
        }

    }
}
