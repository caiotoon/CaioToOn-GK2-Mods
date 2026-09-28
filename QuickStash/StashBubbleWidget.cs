using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

namespace QuickStash
{
    /// <summary>Everything one container received (already capped and sorted by count desc).</summary>
    public sealed class StashBubbleWidgetData : LazyWidgetDataBase
    {
        public List<Item> Items;
        /// <summary>On <see cref="Time.unscaledTime"/>.</summary>
        public float ExpiresAt;
    }

    /// <summary>
    /// One widget per container: a bare grid root (no background) holding one framed craft-hint cell per item.
    /// Each grid child is an empty "slot" sized by the grid (cellSize = frame size x scale); inside it the cloned
    /// craft-hint frame keeps its native size and is shrunk with localScale from its top-left corner, so the
    /// GridLayoutGroup never squeezes the frame's content. The root sizes itself (GridLayoutGroup + ContentSizeFitter);
    /// the bubble refreshes layouts after Draw because the widget is IBubbleLayoutAlwaysActive.
    /// </summary>
    public sealed class StashBubbleWidget : LazyWidget<StashBubbleWidgetData>, IBubbleLayoutAlwaysActive
    {
        /// <summary>Name given to the UIItemCell GameObject inside the frame template, so instances can find it.</summary>
        public const string CellObjectName = "QuickStash.Cell";

        /// <summary>Shared inactive frame template under the holder (outside this hierarchy, so it is not cloned with the widget).</summary>
        public GameObject cellFrameTemplate;
        public GridLayoutGroup grid;
        /// <summary>UICraftHintWidget.defaultLayoutSize of the prefab.</summary>
        public Vector2 frameSize;

        private readonly List<GameObject> slots = new List<GameObject>();
        private readonly List<Transform> frames = new List<Transform>();
        private readonly List<UIItemCell> cells = new List<UIItemCell>();

        /// <summary>
        /// Runs inside the game's bubble flush (UIObjectBubbleManager.FlushPendingDisplays), so it must never throw:
        /// on failure the widget is left empty.
        /// </summary>
        public override void Redraw()
        {
            try
            {
                RedrawUnsafe();
            }
            catch
            {
                foreach (GameObject slot in slots) slot.SetActive(false);
            }
        }

        private void RedrawUnsafe()
        {
            List<Item> items = data.Items;

            float scale = Plugin.BubbleScale.Value > 0f ? Plugin.BubbleScale.Value : 1f;
            int columns = Plugin.BubbleColumns.Value;
            if (columns < 1) columns = scale <= 0.5f ? 2 : 1;
            float spacing = (Plugin.BubbleSpacing.Value >= 0f ? Plugin.BubbleSpacing.Value : ParentSpacing()) * scale;

            grid.cellSize = frameSize * scale;
            grid.spacing = new Vector2(spacing, spacing);
            grid.constraintCount = Math.Min(columns, items.Count);   // never reserve empty columns

            while (slots.Count < items.Count) AddSlot();
            for (int i = 0; i < slots.Count; i++)
            {
                slots[i].SetActive(i < items.Count);
                if (i >= items.Count) continue;

                frames[i].localScale = new Vector3(scale, scale, 1f);   // uniform shrink from the top-left corner the grid positions
                // Same look as UICraftHintWidget: default state, icon via EasySpritesCollection, star badge from the item definition
                cells[i].gameObject.SetActive(true);
                cells[i].Draw(items[i], isCraftResult: true, drawCounter: Plugin.ShowBubbleCount.Value, noSelectionFrames: true);
                cells[i].SetNativeSizeForIcon();
            }
        }

        /// <summary>The bubble's own vertical layout spacing (our parent), else 2.</summary>
        private float ParentSpacing()
        {
            var group = transform.parent != null ? transform.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
            return group != null ? group.spacing : 2f;
        }

        private void AddSlot()
        {
            var slot = new GameObject("Slot" + slots.Count, typeof(RectTransform));
            slot.transform.SetParent(transform, false);

            GameObject frame = Instantiate(cellFrameTemplate, slot.transform, false);
            frame.SetActive(true);

            slots.Add(slot);
            frames.Add(frame.transform);
            cells.Add(frame.GetComponentsInChildren<UIItemCell>(true).First(c => c.name == CellObjectName));
        }

        protected override void TestDraw() { }
    }

    /// <summary>
    /// Builds, once and in-game, (1) the cell-frame template: a clone of the registered UICraftHintWidget prefab with its
    /// progress parts disabled and the UICraftHintWidget component removed, and (2) the registered widget: a bare grid root
    /// with <see cref="StashBubbleWidget"/>. Both live under a persistent inactive holder; the root is registered in
    /// LazyWidgetPrefabContainer.widgets under <see cref="StashBubbleWidgetData"/>.
    /// </summary>
    internal static class StashBubbleTemplate
    {
        private enum BuildState { NotBuilt, Built, Failed }

        private static BuildState state;
        private static GameObject holder;

        /// <summary>
        /// true when the template is registered. Never touches singletons before the bubble manager exists: LazyWidgetPrefabContainer
        /// is a LazySingleton whose table is filled by its own Init(); touching it early would create an empty instance and break every game bubble.
        /// </summary>
        public static bool EnsureBuilt()
        {
            if (state == BuildState.Built) return true;
            if (state == BuildState.Failed) return false;               // decided; do not retry every stash
            if (UIObjectBubbleManager.Instance == null) return false;   // UI not up yet; try again later

            try
            {
                Build();
                state = BuildState.Built;
                return true;
            }
            catch (Exception e)
            {
                state = BuildState.Failed;
                if (holder != null) UnityEngine.Object.Destroy(holder);
                Plugin.Log.LogWarning("Bubble template could not be built, stash bubbles disabled: " + e.Message);
                return false;
            }
        }

        private static void Build()
        {
            var hintPrefab = (UICraftHintWidget)LazyWidgetPrefabContainer.GetPrefabFromDataType<UICraftHintWidgetData>();

            holder = new GameObject("QuickStash.BubbleTemplates");
            UnityEngine.Object.DontDestroyOnLoad(holder);
            holder.SetActive(false);

            // (1) cell-frame template
            UICraftHintWidget hint = UnityEngine.Object.Instantiate(hintPrefab, holder.transform, false);
            GameObject frame = hint.gameObject;
            frame.name = "StashCellFrame";

            var cell = PrivateField<UIItemCell>(hint, "craftResultItem");
            var canvasGroup = PrivateField<CanvasGroup>(hint, "canvasGroup");
            var defaultSize = PrivateField<Vector2>(hint, "defaultLayoutSize");
            if (cell == null) throw new Exception("craftResultItem is null on the prefab");
            if (defaultSize == Vector2.zero) throw new Exception("defaultLayoutSize is zero on the prefab");
            cell.gameObject.name = StashBubbleWidget.CellObjectName;

            Deactivate(PrivateField<Component>(hint, "progessCellContainer"));
            Deactivate(PrivateField<Component>(hint, "progressBarWidget"));
            var zombieBar = PrivateField<Component>(hint, "zombieProgressBar");
            if (zombieBar != null && zombieBar.transform.parent != null) zombieBar.transform.parent.gameObject.SetActive(false);
            Deactivate(PrivateField<Component>(hint, "completionProgressCellsParent"));
            if (canvasGroup != null) canvasGroup.alpha = 1f;

            // The clone never received Draw(); UICraftHintWidget.OnDisable only unsubscribes guarded handlers and kills empty tweens,
            // and the holder is inactive so it does not even run. Remove it so each cell frame only carries the visuals.
            UnityEngine.Object.DestroyImmediate(hint);

            // top-left anchored at native size: instances only change localScale
            var frameRt = (RectTransform)frame.transform;
            frameRt.anchorMin = new Vector2(0f, 1f);
            frameRt.anchorMax = new Vector2(0f, 1f);
            frameRt.pivot = new Vector2(0f, 1f);
            frameRt.anchoredPosition = Vector2.zero;
            frameRt.sizeDelta = defaultSize;

            // (2) registered widget: bare grid root, no Image of its own
            var root = new GameObject("QuickStashBubble", typeof(RectTransform));
            root.transform.SetParent(holder.transform, false);
            var grid = root.AddComponent<GridLayoutGroup>();
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var widget = root.AddComponent<StashBubbleWidget>();
            widget.cellFrameTemplate = frame;
            widget.grid = grid;
            widget.frameSize = defaultSize;

            var widgets = PrivateField<Dictionary<Type, LazyWidgetBase>>(LazySingleton<LazyWidgetPrefabContainer>.Instance, "widgets");
            widgets[typeof(StashBubbleWidgetData)] = widget;
        }

        private static void Deactivate(Component c)
        {
            if (c != null) c.gameObject.SetActive(false);
        }

        private static T PrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new Exception(target.GetType().Name + "." + name + " not found");
            return (T)field.GetValue(target);
        }
    }
}
