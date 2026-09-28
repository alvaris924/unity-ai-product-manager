using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Alvaris.AiProductManager
{
    /// <summary>
    /// One synthetic finger delivered straight to the uGUI event system: press, move and release, reaching the same
    /// handlers a touch reaches (enter, down, potential drag, begin drag, drag, up, click, drop, end drag, exit). It works
    /// with either input backend because it never touches an input device, only the EventSystem's raycasters and
    /// ExecuteEvents. Game code that polls an input device directly does not see it.
    /// </summary>
    public sealed class SimulatedPointer
    {
        readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
        PointerEventData data;

        public SimulatedPointer(int pointerId)
        {
            PointerId = pointerId;
        }

        public int PointerId { get; }
        public bool IsPressed { get; private set; }

        /// <summary>The topmost raycast hit where the finger went down (null when it landed on nothing).</summary>
        public GameObject PressHit { get; private set; }

        /// <summary>The object that took the down event (a button, a joystick area), or null when nothing did.</summary>
        public GameObject PressHandler { get; private set; }

        /// <summary>The object that received a click on release, or null when the release was not a click.</summary>
        public GameObject Clicked { get; private set; }

        /// <summary>The object that received the drag events, or null when nothing was dragged.</summary>
        public GameObject Dragged { get; private set; }

        public Vector2 Position => data != null ? data.position : Vector2.zero;

        /// <summary>Puts the finger down at a screen point (pixels, origin bottom-left). False when there is no EventSystem.</summary>
        public bool Press(Vector2 screenPos)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            if (IsPressed) Release(Position);

            data = new PointerEventData(eventSystem)
            {
                pointerId = PointerId,
                position = screenPos,
                pressPosition = screenPos,
                delta = Vector2.zero,
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                clickTime = Time.unscaledTime,
                eligibleForClick = true,
                useDragThreshold = true,
            };
            var hit = Raycast(eventSystem, screenPos);
            data.pointerCurrentRaycast = hit;
            data.pointerPressRaycast = hit;
            var over = hit.gameObject;
            SetHovered(over);

            // The down event goes to the nearest handler up the hierarchy; a click-only control still counts as pressed.
            var pressed = ExecuteEvents.ExecuteHierarchy(over, data, ExecuteEvents.pointerDownHandler);
            if (pressed == null) pressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
            data.pointerPress = pressed;
            data.rawPointerPress = over;
            data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(over);
            if (data.pointerDrag != null) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);

            PressHit = over;
            PressHandler = pressed;
            Clicked = null;
            Dragged = null;
            IsPressed = true;
            return true;
        }

        /// <summary>Moves a pressed finger. The drag starts once it leaves the EventSystem's drag threshold, as a touch does.</summary>
        public void Move(Vector2 screenPos)
        {
            if (!IsPressed || data == null) return;
            var eventSystem = EventSystem.current;
            data.delta = screenPos - data.position;
            data.position = screenPos;
            if (eventSystem != null)
            {
                var hit = Raycast(eventSystem, screenPos);
                data.pointerCurrentRaycast = hit;
                SetHovered(hit.gameObject);
            }
            if (data.pointerDrag == null) return;

            if (!data.dragging)
            {
                float threshold = eventSystem != null ? eventSystem.pixelDragThreshold : 10f;
                if (data.useDragThreshold && (data.pressPosition - screenPos).sqrMagnitude < threshold * threshold) return;
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
                data.dragging = true;
                Dragged = data.pointerDrag;
                // A drag owned by another object than the pressed one cancels that press, like a finger sliding off a button.
                if (data.pointerPress != null && data.pointerPress != data.pointerDrag)
                {
                    ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
                    data.eligibleForClick = false;
                    data.pointerPress = null;
                    data.rawPointerPress = null;
                }
            }
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
        }

        /// <summary>Lifts the finger. A release over the pressed control that did not turn into a drag is a click.</summary>
        public void Release(Vector2 screenPos)
        {
            if (!IsPressed || data == null) return;
            var eventSystem = EventSystem.current;
            data.delta = screenPos - data.position;
            data.position = screenPos;
            GameObject over = null;
            if (eventSystem != null)
            {
                var hit = Raycast(eventSystem, screenPos);
                data.pointerCurrentRaycast = hit;
                over = hit.gameObject;
            }

            if (data.pointerPress != null) ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
            if (data.pointerPress != null && data.pointerPress == clickHandler && data.eligibleForClick)
            {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerClickHandler);
                Clicked = data.pointerPress;
            }
            if (data.pointerDrag != null && data.dragging)
            {
                if (over != null) ExecuteEvents.ExecuteHierarchy(over, data, ExecuteEvents.dropHandler);
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
            }

            data.eligibleForClick = false;
            data.pointerPress = null;
            data.rawPointerPress = null;
            data.pointerDrag = null;
            data.dragging = false;
            SetHovered(null);
            IsPressed = false;
        }

        void SetHovered(GameObject over)
        {
            if (data.pointerEnter == over) return;
            if (data.pointerEnter != null) ExecuteEvents.ExecuteHierarchy(data.pointerEnter, data, ExecuteEvents.pointerExitHandler);
            data.pointerEnter = over;
            if (over != null) ExecuteEvents.ExecuteHierarchy(over, data, ExecuteEvents.pointerEnterHandler);
        }

        RaycastResult Raycast(EventSystem eventSystem, Vector2 screenPos)
        {
            raycastResults.Clear();
            var probe = new PointerEventData(eventSystem) { pointerId = PointerId, position = screenPos };
            try { eventSystem.RaycastAll(probe, raycastResults); }
            catch { raycastResults.Clear(); }
            foreach (var result in raycastResults)
                if (result.gameObject != null) return result;
            return default;
        }
    }
}
