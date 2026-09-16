using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ked.Progression.Debugging.UI
{
    // UIPresentationFlow의 UIBase<TRefs> 패턴을 디버그 화면에 필요한 범위로 가져온다.
    // enum 이름과 같은 자식 오브젝트를 찾아 Component를 캐시한다.
    public abstract class UIBase : MonoBehaviour
    {
        private bool _initialized;

        protected virtual void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            PreInitialize();
            OnInitialize();
        }

        protected virtual void PreInitialize() { }

        protected virtual void OnInitialize() { }

        protected static void BindEvent(
            Button button,
            Action<PointerEventData> action)
        {
            if (button == null || action == null)
                return;

            UI_EventHandler eventHandler =
                GetOrAddComponent<UI_EventHandler>(button.gameObject);

            eventHandler.OnClickHandler -= action;
            eventHandler.OnClickHandler += action;
        }

        private static T GetOrAddComponent<T>(GameObject target)
            where T : Component
        {
            T component = target.GetComponent<T>();

            if (component == null)
                component = target.AddComponent<T>();

            return component;
        }
    }

    public abstract class UIBase<TRefs> : UIBase
        where TRefs : struct, Enum
    {
        protected sealed class RefView
        {
            private readonly UIBase<TRefs> _ui;

            internal RefView(UIBase<TRefs> ui)
            {
                _ui = ui;
            }

            public RectTransform Rect(TRefs key) =>
                _ui.GetCached<RectTransform>(key);

            public Text Text(TRefs key) =>
                _ui.GetCached<Text>(key);

            public Image Image(TRefs key) =>
                _ui.GetCached<Image>(key);

            public Button Button(TRefs key) =>
                _ui.GetCached<Button>(key);
        }

        private GameObject[] _objects;
        private readonly Dictionary<(int index, Type type), Component>
            _componentCache = new Dictionary<(int index, Type type), Component>();

        protected RefView View { get; private set; }

        protected override void PreInitialize()
        {
            string[] refNames = Enum.GetNames(typeof(TRefs));
            _objects = new GameObject[refNames.Length];

            for (int i = 0; i < refNames.Length; i++)
                _objects[i] = FindChildGameObjectRecursive(gameObject, refNames[i]);

            View = new RefView(this);
        }

        protected virtual void OnDestroy()
        {
            _componentCache.Clear();
        }

        private T GetCached<T>(TRefs key)
            where T : Component
        {
            int index = Convert.ToInt32(key);
            var cacheKey = (index, typeof(T));

            if (_componentCache.TryGetValue(
                    cacheKey,
                    out Component cached) &&
                cached != null)
            {
                return (T)cached;
            }

            if (_objects == null ||
                (uint)index >= (uint)_objects.Length)
            {
                return null;
            }

            GameObject target = _objects[index];

            if (target == null ||
                !target.TryGetComponent(out T component))
            {
                return null;
            }

            _componentCache[cacheKey] = component;
            return component;
        }

        private static GameObject FindChildGameObjectRecursive(
            GameObject root,
            string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(
                         includeInactive: true))
            {
                if (child.name == name)
                    return child.gameObject;
            }

            return null;
        }
    }
}
