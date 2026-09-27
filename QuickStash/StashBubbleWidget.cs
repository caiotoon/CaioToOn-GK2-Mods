using System;
using System.Collections.Generic;
using System.Reflection;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

namespace QuickStash
{
    /// <summary>One stashed item: id and moved count (the star badge comes from the item definition).</summary>
    public sealed class StashBubbleCell
    {
        public string ItemId;
        public int Count;

        public StashBubbleCell(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }
    }

    /// <summary>Everything one container received (already capped and sorted by count desc).</summary>
    public sealed class StashBubbleWidgetData : LazyWidgetDataBase
    {
        public readonly List<StashBubbleCell> Cells;

        public StashBubbleWidgetData(List<StashBubbleCell> cells)
        {
            Cells = cells ?? new List<StashBubbleCell>();
        }
    }

    /// <summary>
    /// One widget per container: a bare grid root (no background) holding one framed craft-hint cell per item.
    /// Each grid child is an empty "slot" sized by the grid (cellSize = frame size x scale); inside it the cloned
    /// craft-hint frame keeps its native size and is shrunk with localScale from its top-left corner, so the
    /// GridLayoutGroup never squeezes the frame's content. Spacing is scaled too, which the bubble's own vertical
    /// layout could not do when every cell was a separate widget.
    /// </summary>
    public sealed class StashBubbleWidget : LazyWidget<StashBubbleWidgetData>, IBubbleLayoutAlwaysActive
    {
        /// <summary>Name given to the UIItemCell GameObject inside the frame template, so instances can find it without a type scan.</summary>
        public const string CellObjectName = "QuickStash.Cell";

        /// <summary>Shared inactive frame template under the holder (outside this hierarchy, so it is not cloned with the widget).</summary>
        public GameObject cellFrameTemplate;
        public GridLayoutGroup grid;
        public LayoutElement layoutElement;
        public RectTransform rectTransform;
        /// <summary>UICraftHintWidget.defaultLayoutSize of the prefab.</summary>
        public Vector2 frameSize;

        private readonly List<RectTransform> slots = new List<RectTransform>();
        private readonly List<RectTransform> frames = new List<RectTransform>();
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
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i] != null) slots[i].gameObject.SetActive(false);
            }
        }

        private void RedrawUnsafe()
        {
            if (data == null || grid == null || cellFrameTemplate == null) return;
            List<StashBubbleCell> items = data.Cells;
            int n = items.Count;

            float scale = Plugin.BubbleScale != null ? Plugin.BubbleScale.Value : 1f;
            if (scale <= 0f) scale = 1f;
            int columns = Plugin.BubbleColumns != null ? Plugin.BubbleColumns.Value : 0;
            if (columns < 1) columns = scale <= 0.5f ? 2 : 1;
            if (columns > Math.Max(1, n)) columns = Math.Max(1, n);      // never reserve empty columns
            float spacingCfg = Plugin.BubbleSpacing != null ? Plugin.BubbleSpacing.Value : -1f;
            float spacingUnscaled = spacingCfg >= 0f ? spacingCfg : ParentSpacing();
            float spacing = spacingUnscaled * scale;
            bool showCount = Plugin.ShowBubbleCount != null && Plugin.ShowBubbleCount.Value;

            Vector2 cellSize = frameSize * scale;
            grid.padding = new RectOffset(0, 0, 0, 0);
            grid.cellSize = cellSize;
            grid.spacing = new Vector2(spacing, spacing);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;

            EnsureSlots(n);
            for (int i = 0; i < slots.Count; i++)
            {
                bool active = i < n;
                slots[i].gameObject.SetActive(active);
                if (!active) continue;

                RectTransform frame = frames[i];
                if (frame != null)
                {
                    frame.anchorMin = new Vector2(0f, 1f);
                    frame.anchorMax = new Vector2(0f, 1f);
                    frame.pivot = new Vector2(0f, 1f);
                    frame.anchoredPosition = Vector2.zero;
                    frame.sizeDelta = frameSize;                       // native size: the cell lays out exactly like the craft hint
                    frame.localScale = new Vector3(scale, scale, 1f);  // uniform shrink from the corner the grid positions
                }
                DrawCell(cells[i], items[i], showCount);
            }

            int rows = (n + columns - 1) / columns;
            if (rows < 1) rows = 1;
            float width = columns * cellSize.x + (columns - 1) * spacing;
            float height = rows * cellSize.y + (rows - 1) * spacing;
            if (layoutElement != null)
            {
                layoutElement.minWidth = width;
                layoutElement.minHeight = height;
                layoutElement.preferredWidth = width;
                layoutElement.preferredHeight = height;
            }
            RectTransform rt = rectTransform != null ? rectTransform : transform as RectTransform;
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(width, height);
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            }
        }

        /// <summary>The bubble's own vertical layout spacing (our parent), else 2.</summary>
        private float ParentSpacing()
        {
            try
            {
                Transform p = transform.parent;
                var group = p != null ? p.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
                if (group != null) return group.spacing;
            }
            catch { }
            return 2f;
        }

        private void EnsureSlots(int n)
        {
            while (slots.Count < n)
            {
                var slotGo = new GameObject("Slot" + slots.Count, typeof(RectTransform));
                var slot = slotGo.GetComponent<RectTransform>();
                slot.SetParent(transform, false);

                GameObject frameGo = UnityEngine.Object.Instantiate(cellFrameTemplate, slot, false);
                frameGo.name = "CellFrame";
                frameGo.SetActive(true);

                Transform cellTf = FindByName(frameGo.transform, CellObjectName);
                slots.Add(slot);
                frames.Add(frameGo.GetComponent<RectTransform>());
                cells.Add(cellTf != null ? cellTf.GetComponent<UIItemCell>() : null);
            }
        }

        internal static Transform FindByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindByName(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Same look as UICraftHintWidget: Default state (white background, no non-interactable overlay), icon via
        /// EasySpritesCollection, star badge from the item definition, no status icon, no selection frames; the counter
        /// is controlled by drawCounter.
        /// </summary>
        private static void DrawCell(UIItemCell cell, StashBubbleCell c, bool showCount)
        {
            if (cell == null || c == null) return;
            cell.gameObject.SetActive(true);
            var item = new Item(c.ItemId, Math.Max(1, c.Count));
            cell.Draw(item, isNeedItem: false, hasItemCount: -1, isCraftResult: true, multiplier: 1, drawAsNonInteractable: false,
                      price: 0, drawCounter: showCount, forceNonEmpty: false, forceDrawCounter: false,
                      customState: ItemRelatedWidgetState.NotSet, noSelectionFrames: true);
            cell.SetNativeSizeForIcon();
        }

        protected override void TestDraw()
        {
        }
    }

    /// <summary>
    /// Builds, once and in-game, (1) the cell-frame template: a clone of the registered UICraftHintWidget prefab with its
    /// progress parts disabled and the UICraftHintWidget component removed, and (2) the registered widget: a bare grid root
    /// with <see cref="StashBubbleWidget"/>. Both live under a persistent inactive holder; the root is registered in
    /// LazyWidgetPrefabContainer.widgets under <see cref="StashBubbleWidgetData"/>.
    /// </summary>
    internal static class StashBubbleTemplate
    {
        internal enum BuildState { NotBuilt, Built, Failed }

        private static GameObject holder;
        private static StashBubbleWidget template;

        public static BuildState State { get; private set; } = BuildState.NotBuilt;

        /// <summary>true when the template is registered. Never touches singletons before the bubble manager exists.</summary>
        public static bool EnsureBuilt()
        {
            if (State == BuildState.Built) return true;
            if (State == BuildState.Failed) return false;               // decided; do not retry every stash
            if (UIObjectBubbleManager.Instance == null) return false;   // UI not up yet; try again later

            try
            {
                Build();
                State = BuildState.Built;
                return true;
            }
            catch (Exception e)
            {
                State = BuildState.Failed;
                if (holder != null) { UnityEngine.Object.Destroy(holder); holder = null; }
                Plugin.Log.LogWarning("Bubble template could not be built, using a plain fallback bubble: " + e.Message);
                return false;
            }
        }

        /// <summary>The game's widget prefab table (private LazyWidgetPrefabContainer.widgets). Only call once the UI exists.</summary>
        public static bool TryGetRegisteredWidgets(out Dictionary<Type, LazyWidgetBase> widgets)
        {
            widgets = null;
            try
            {
                var field = typeof(LazyWidgetPrefabContainer).GetField("widgets", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) return false;
                widgets = field.GetValue(LazySingleton<LazyWidgetPrefabContainer>.Instance) as Dictionary<Type, LazyWidgetBase>;
                return widgets != null;
            }
            catch
            {
                return false;
            }
        }

        private static void Build()
        {
            LazyWidgetBase prefab = LazyWidgetPrefabContainer.GetPrefabFromDataType(typeof(UICraftHintWidgetData));
            if (prefab == null) throw new Exception("UICraftHintWidgetData prefab is null");
            var hintOnPrefab = prefab as UICraftHintWidget ?? prefab.GetComponent<UICraftHintWidget>();
            if (hintOnPrefab == null) throw new Exception("registered prefab is " + prefab.GetType().Name + ", not UICraftHintWidget");

            holder = new GameObject("QuickStash.BubbleTemplates");
            UnityEngine.Object.DontDestroyOnLoad(holder);
            holder.SetActive(false);

            // (1) cell-frame template
            GameObject frame = UnityEngine.Object.Instantiate(hintOnPrefab.gameObject, holder.transform, false);
            frame.name = "StashCellFrame";
            var hint = frame.GetComponent<UICraftHintWidget>();
            if (hint == null) throw new Exception("clone has no UICraftHintWidget");

            var cell = Field<UIItemCell>(hint, "craftResultItem");
            var canvasGroup = Field<CanvasGroup>(hint, "canvasGroup");
            var defaultSize = (Vector2)FieldValue(hint, "defaultLayoutSize");
            if (cell == null) throw new Exception("craftResultItem is null on the prefab");
            if (defaultSize == Vector2.zero) throw new Exception("defaultLayoutSize is zero on the prefab");
            cell.gameObject.name = StashBubbleWidget.CellObjectName;   // instances find the cell by this name

            Deactivate(Field<Component>(hint, "progessCellContainer"));
            Deactivate(Field<Component>(hint, "progressBarWidget"));
            var zombieBar = Field<Component>(hint, "zombieProgressBar");
            if (zombieBar != null && zombieBar.transform.parent != null) zombieBar.transform.parent.gameObject.SetActive(false);
            Deactivate(Field<Component>(hint, "completionProgressCellsParent"));
            if (canvasGroup != null) canvasGroup.alpha = 1f;

            // The clone never received Draw(); UICraftHintWidget.OnDisable only unsubscribes guarded handlers and kills empty tweens,
            // and the holder is inactive so it does not even run. Remove it so each cell frame only carries the visuals.
            UnityEngine.Object.DestroyImmediate(hint);

            var frameRt = frame.GetComponent<RectTransform>();
            if (frameRt != null)
            {
                frameRt.anchorMin = new Vector2(0f, 1f);
                frameRt.anchorMax = new Vector2(0f, 1f);
                frameRt.pivot = new Vector2(0f, 1f);
                frameRt.sizeDelta = defaultSize;
            }

            // (2) registered widget: bare grid root, no Image of its own
            var root = new GameObject("QuickStashBubble", typeof(RectTransform));
            root.transform.SetParent(holder.transform, false);
            var rootRt = root.GetComponent<RectTransform>();
            var grid = root.AddComponent<GridLayoutGroup>();
            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layoutElement = root.AddComponent<LayoutElement>();
            var widget = root.AddComponent<StashBubbleWidget>();
            widget.cellFrameTemplate = frame;
            widget.grid = grid;
            widget.layoutElement = layoutElement;
            widget.rectTransform = rootRt;
            widget.frameSize = defaultSize;

            Dictionary<Type, LazyWidgetBase> widgets;
            if (!TryGetRegisteredWidgets(out widgets)) throw new Exception("LazyWidgetPrefabContainer.widgets unavailable (container not initialized)");
            widgets[typeof(StashBubbleWidgetData)] = widget;

            template = widget;
        }

        private static void Deactivate(Component c)
        {
            if (c != null && c.gameObject != null) c.gameObject.SetActive(false);
        }

        private static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();

        private static object FieldValue(UICraftHintWidget hint, string name)
        {
            FieldInfo f;
            if (!fields.TryGetValue(name, out f))
            {
                f = typeof(UICraftHintWidget).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f == null) throw new Exception("UICraftHintWidget." + name + " not found");
                fields[name] = f;
            }
            return f.GetValue(hint);
        }

        private static T Field<T>(UICraftHintWidget hint, string name) where T : class
        {
            return FieldValue(hint, name) as T;
        }
    }
}
