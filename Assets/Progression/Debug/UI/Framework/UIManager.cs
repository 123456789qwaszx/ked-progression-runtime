using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ked.Progression.Debugging.UI
{
    // UIPresentationFlow UIManager의 register/switch root 흐름만 디버그용으로 사용한다.
    public sealed class UIManager
    {
        private readonly Dictionary<Type, UIBase> _views = new Dictionary<Type, UIBase>();
        private readonly Transform _rootLayer;

        public UIBase CurrentRoot { get; private set; }

        public UIManager(Transform rootLayer)
        {
            _rootLayer = rootLayer != null
                ? rootLayer
                : throw new ArgumentNullException(nameof(rootLayer));
        }

        public void Register(UIBase view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            Type type = view.GetType();

            if (_views.TryGetValue(type, out UIBase existing) &&
                existing != view)
            {
                throw new InvalidOperationException(
                    $"[UIManager] Duplicate View type '{type.Name}'.");
            }

            view.EnsureInitialized();
            _views[type] = view;
        }

        public T SwitchRoot<T>(
            Action<T> afterPresented = null,
            Action<UIBase> afterClosed = null)
            where T : UIBase, IUIRoot
        {
            T root = Require<T>();
            bool sameRoot = CurrentRoot == root;

            if (CurrentRoot != null && !sameRoot)
            {
                UIBase previous = CurrentRoot;
                SetVisible(previous, false);
                afterClosed?.Invoke(previous);
            }

            CurrentRoot = root;
            Mount(root, _rootLayer);
            SetVisible(root, true);
            afterPresented?.Invoke(root);

            return root;
        }

        private T Require<T>()
            where T : UIBase
        {
            if (_views.TryGetValue(typeof(T), out UIBase view))
                return (T)view;

            throw new InvalidOperationException(
                $"[UIManager] View '{typeof(T).Name}' is not registered.");
        }

        private static void SetVisible(
            UIBase view,
            bool visible)
        {
            if (view.gameObject.activeSelf != visible)
                view.gameObject.SetActive(visible);
        }

        private static void Mount(
            UIBase view,
            Transform layer)
        {
            if (view.transform.parent != layer)
                view.transform.SetParent(layer, worldPositionStays: false);

            view.transform.SetAsLastSibling();
        }
    }
}
