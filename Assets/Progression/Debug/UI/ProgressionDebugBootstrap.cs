using UnityEngine;

namespace Ked.Progression.Debugging.UI
{
    // 디버그 전용 조립 지점.
    // Host는 Progression contract만, UIRoot는 화면만, Bindings는 둘의 연결만 담당한다.
    public sealed class ProgressionDebugBootstrap : MonoBehaviour
    {
        private UIManager _ui;
        private ProgressionDebugBindings _bindings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isBatchMode)
                return;

            if (FindFirstObjectByType<ProgressionDebugBootstrap>() != null)
                return;

            GameObject root = new GameObject("Progression Debug");
            DontDestroyOnLoad(root);
            root.AddComponent<ProgressionDebugBootstrap>();
        }

        private void Awake()
        {
            ProgressionDebugHost host =
                GetComponent<ProgressionDebugHost>();

            if (host == null)
                host = gameObject.AddComponent<ProgressionDebugHost>();

            ProgressionDebugUIRoot view =
                ProgressionDebugUIFactory.Create(
                    transform,
                    out RectTransform rootLayer);

            _ui = new UIManager(rootLayer);
            _ui.Register(view);
            _ui.SwitchRoot<ProgressionDebugUIRoot>();

            _bindings = new ProgressionDebugBindings(host, view);
            _bindings.Bind();
        }

        private void OnDestroy()
        {
            _bindings?.Dispose();
        }
    }
}
