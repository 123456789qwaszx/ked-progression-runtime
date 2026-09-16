using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ked.Progression.Debugging.UI
{
    // UIPresentationFlow의 UI_EventHandler에서 click 경계만 가져온다.
    public sealed class UI_EventHandler : MonoBehaviour,
        IPointerClickHandler
    {
        public Action<PointerEventData> OnClickHandler;

        public void OnPointerClick(PointerEventData eventData)
        {
            Button button = GetComponent<Button>();
            if (button != null && !button.IsInteractable())
                return;

            OnClickHandler?.Invoke(eventData);
        }
    }
}
