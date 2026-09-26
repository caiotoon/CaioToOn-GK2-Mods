using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using LazyBearTechnology;
using UnityEngine;

namespace QuickStash
{
    /// <summary>
    /// Discovery dump: BepInEx/QuickStash-dump.txt (latest, overwritten) + QuickStash-dump-&lt;zone&gt;-&lt;HHmmss&gt;.txt (kept).
    /// Always a dry run: nothing is moved.
    /// </summary>
    internal static class Diagnostics
    {
        public static string LatestDumpPath => Path.Combine(Paths.BepInExRootPath, "QuickStash-dump.txt");

        /// <summary>Writes the dump files and returns a one-line summary for the log.</summary>
        public static string WriteDump()
        {
            float fallbackRadius = Plugin.FallbackRadius.Value;
            bool includeConveyorChests = Plugin.IncludeConveyorChests.Value;

            var pd = MainGame.PlayerData;
            var sb = new StringBuilder();
            var summary = new StringBuilder();

            sb.AppendLine("QuickStash " + Plugin.PluginVersion + " dump  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("config: StashKey=" + Plugin.StashKey.Value + "  DiagnosticsKey=" + Plugin.DiagnosticsKey.Value
                          + "  DryRun=" + Plugin.DryRun.Value + "  PlaySound=" + Plugin.PlaySound.Value
                          + "  IncludeBagContents=" + Plugin.IncludeBagContents.Value
                          + "  ShowBubbles=" + Plugin.ShowBubbles.Value + "  BubbleSeconds=" + Plugin.BubbleSeconds.Value
                          + "  BubbleScale=" + Plugin.BubbleScale.Value + "  MaxBubbleItems=" + Plugin.MaxBubbleItems.Value
                          + "  ShowBubbleCount=" + Plugin.ShowBubbleCount.Value + "  BubbleColumns=" + Plugin.BubbleColumns.Value + "  BubbleSpacing=" + Plugin.BubbleSpacing.Value
                          + "  FallbackRadius=" + fallbackRadius + "  IncludeConveyorChests=" + includeConveyorChests);
            sb.AppendLine("legend: [Z]=listed by current zone  [P]=in zone.MultiInventoryWgoDatas  [G]=in zone.wgoDataList  [R]=within radius  [T]=stash target");
            sb.AppendLine();

            Vector3 pos = pd.position.Value;
            WorldZoneData zone = pd.CurrentWorldZoneData;

            // 1. player
            Section(sb, "1. PLAYER", () =>
            {
                sb.AppendLine("scene     : " + pd.currentGameSceneId);
                sb.AppendLine("position  : " + F(pos));
                if (zone == null)
                {
                    sb.AppendLine("zone      : null (player is between zones)");
                }
                else
                {
                    sb.AppendLine("zone      : id=" + zone.id + "  type=" + zone.worldZoneType + "  scene=" + zone.gameSceneId + "  IsContainer=" + zone.IsContainer);
                    sb.AppendLine("zone rect : " + zone.wholeZoneRect + "  center=" + F(zone.Center));
                    sb.AppendLine("zone lists: MultiInventoryWgoDatas=" + (zone.MultiInventoryWgoDatas == null ? "null" : zone.MultiInventoryWgoDatas.Count.ToString())
                                  + "  wgoDataList=" + (zone.wgoDataList == null ? "null" : zone.wgoDataList.Count.ToString()));
                }
                summary.Append("zone=" + (zone == null ? "null" : zone.id));
            });

            // 2. zone containers
            var prepared = ContainerFinder.ZoneWgosPrepared(zone);
            var fromGuids = ContainerFinder.ZoneWgosFromGuids(zone);
            var zoneSet = new Dictionary<string, WgoData>();
            var preparedIds = new HashSet<string>();
            var guidIds = new HashSet<string>();
            foreach (var w in prepared) { zoneSet[Key(w)] = w; preparedIds.Add(Key(w)); }
            foreach (var w in fromGuids) { zoneSet[Key(w)] = w; guidIds.Add(Key(w)); }

            var radius = ContainerFinder.RadiusWgos(pd.currentGameSceneId, pos, fallbackRadius);
            var radiusIds = new HashSet<string>();
            foreach (var w in radius) radiusIds.Add(Key(w));

            string targetSource;
            var targets = ContainerFinder.FindTargets(pd, fallbackRadius, includeConveyorChests, out targetSource);
            var targetIds = new HashSet<string>();
            foreach (var t in targets) targetIds.Add(Key(t.Wgo));

            Section(sb, "2. ZONE CONTAINERS (Definition.inventorySize != 0)", () =>
            {
                if (zone == null) { sb.AppendLine("(no zone)"); return; }
                sb.AppendLine("via MultiInventoryWgoDatas: " + prepared.Count + "   via wgoDataList: " + fromGuids.Count
                              + (preparedIds.SetEquals(guidIds) ? "   (same set)" : "   (DIFFERENT sets, see P/G flags)"));
                var list = new List<WgoData>(zoneSet.Values);
                list.Sort((a, b) => ContainerFinder.Distance(a, pos).CompareTo(ContainerFinder.Distance(b, pos)));
                foreach (var w in list)
                    AppendWgo(sb, w, pos, "[Z]" + Flag(preparedIds, w, "P") + Flag(guidIds, w, "G") + Flag(radiusIds, w, "R") + Flag(targetIds, w, "T"));
                summary.Append(" zoneContainers=" + list.Count);
            });

            // 3. radius scan
            Section(sb, "3. RADIUS SCAN (scene==player scene, !IsHidden, !isTempObject, inventorySize != 0, dist <= " + fallbackRadius + ")", () =>
            {
                radius.Sort((a, b) => ContainerFinder.Distance(a, pos).CompareTo(ContainerFinder.Distance(b, pos)));
                int alsoInZone = 0;
                foreach (var w in radius)
                {
                    bool inZone = zoneSet.ContainsKey(Key(w));
                    if (inZone) alsoInZone++;
                    AppendWgo(sb, w, pos, (inZone ? "[Z]" : "[ ]") + "[R]" + Flag(targetIds, w, "T"));
                }
                sb.AppendLine("total=" + radius.Count + "  alsoInZone=" + alsoInZone + "  onlyInRadius=" + (radius.Count - alsoInZone));
                summary.Append(" radiusContainers=" + radius.Count);
            });

            // 4. all container defs
            Section(sb, "4. ALL WGO DEFS WITH inventorySize != 0", () =>
            {
                var defs = GameBalance.Me.wgoDefs;
                var open = new List<WGODef>();
                var closed = new List<WGODef>();
                foreach (var d in defs)
                {
                    if (d == null || d.inventorySize == 0) continue;
                    (d.OpenInMultiInventory ? open : closed).Add(d);
                }
                open.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
                closed.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
                sb.AppendLine("-- OpenInMultiInventory = true (" + open.Count + ")");
                foreach (var d in open) AppendDef(sb, d);
                sb.AppendLine("-- OpenInMultiInventory = false (" + closed.Count + ")");
                foreach (var d in closed) AppendDef(sb, d);
            });

            // 5. player inventories
            Section(sb, "5. PLAYER BACKPACK (PlayerData.inventory) -- the only stash SOURCE", () =>
            {
                AppendPlayerInventory(sb, pd.inventory);
            });
            Section(sb, "5b. TOOLBELT (PlayerData.toolBeltInventory) -- equipped tools bar; NEVER a stash source", () =>
            {
                AppendPlayerInventory(sb, pd.toolBeltInventory);
            });
            Section(sb, "5c. OTHER (PlayerData.interactingItem)", () =>
            {
                var it = pd.interactingItem;
                sb.AppendLine(it == null || it.IsEmpty ? "none" : ItemLine(it));
            });

            // 6. keyboard bindings
            Section(sb, "6. KEYBOARD BINDINGS (LazyInput.GameBindings.keyBindings; Rewired is gamepad only)", () =>
            {
                AppendKeyBindings(sb, summary);
            });

            // 7. dry run
            Section(sb, "7. DRY-RUN SimilarOnly (nothing moved)  targets from: " + targetSource, () =>
            {
                sb.AppendLine("targets (" + targets.Count + ", sorted by distance):");
                foreach (var t in targets)
                    sb.AppendLine("  " + t.Id + "  uid=" + t.Uid + "  dist=" + t.Distance.ToString("0.0"));
                sb.AppendLine();

                bool bags = Plugin.IncludeBagContents.Value;
                var plan = Stasher.Plan(pd.inventory, targets, bags);
                if (plan.Moves.Count == 0)
                {
                    sb.AppendLine("no backpack stack would move");
                }
                else
                {
                    string cur = null;
                    foreach (var m in plan.Moves)
                    {
                        if (m.ContainerUid != cur)
                        {
                            cur = m.ContainerUid;
                            sb.AppendLine(m.ContainerId + " (" + m.ContainerUid + "):");
                        }
                        sb.AppendLine("    <- " + m.ItemId + " x " + m.Count + (m.FromBag ? "  [bag]" : ""));
                    }
                }
                sb.AppendLine("total: " + plan.TotalItems + " item(s) in " + plan.Moves.Count + " move(s) into " + plan.ContainersTouched + " container(s)  (IncludeBagContents=" + bags + ")");

                sb.AppendLine();
                sb.AppendLine("NOT MOVED:");
                var why = Stasher.ExplainUnmoved(pd.inventory, targets, plan, bags);
                if (why.Count == 0) sb.AppendLine("  (every stackable backpack stack is in the plan)");
                foreach (var line in why) sb.AppendLine("  " + line);

                summary.Append(" targets=" + targets.Count + " dryRunMoves=" + plan.Moves.Count + " dryRunItems=" + plan.TotalItems);
            });

            // 8. gates
            Section(sb, "8. STASH GATES (state at the moment of this dump)", () =>
            {
                string reason;
                bool ok = Plugin.CanStash(out reason);
                sb.AppendLine("CanStash: " + ok + (ok ? "" : "  blocked by: " + reason));
                var pc = MainGame.Instance != null ? MainGame.PlayerController : null;
                sb.AppendLine("  PlayerController.IsControlsEnabled = " + (pc != null ? pc.IsControlsEnabled.ToString() : "n/a"));
                sb.AppendLine("  MainGame.IsGamePaused              = " + MainGame.IsGamePaused);
                sb.AppendLine("  LazyInput.IsInputActive()          = " + LazyInput.IsInputActive());
                sb.AppendLine("  LazyNetwork.IsInitialized          = " + LazyNetwork.IsInitialized + "  coopClient=" + Plugin.IsCoopClient());
                sb.AppendLine("  StashBubbles: patched=" + StashBubbles.IsPatched + "  widgetPath=" + StashBubbles.ActiveMode + " (decided on first stash)"
                              + "  bubbleTemplate=" + StashBubbleTemplate.Status + "  liveEntries=" + StashBubbles.LiveCount);
                sb.AppendLine("  lastBubbleLayout: " + StashBubbleWidget.LastLayoutInfo);
                sb.AppendLine("  LazyWindowsStackController.ActiveWindow = " + (LazyWindowsStackController.ActiveWindow != null ? LazyWindowsStackController.ActiveWindow.GetType().Name : "null")
                              + "  HasAnyModalWindowOpened=" + LazyWindowsStackController.HasAnyModalWindowOpened);
                if (LazyUI.IsInitialized)
                {
                    sb.AppendLine("  windows (LazyUI.GetAllWindows): type  inStack  IsShown");
                    foreach (var w in LazyUI.GetAllWindows())
                    {
                        if (w == null) continue;
                        sb.AppendLine("    " + w.GetType().Name.PadRight(40) + LazyWindowsStackController.IsWindowOpened(w).ToString().PadRight(9) + Plugin.IsWindowShown(w));
                    }
                }
                summary.Append(" canStash=" + ok);
            });

            // 9. bubble icons
            Section(sb, "9. BUBBLE ICONS (stackable backpack items incl. bag contents; sprite drawn by UIItemCell via EasySpritesCollection.GetSprite(iconId))", () =>
            {
                if (pd.inventory == null || pd.inventory.Data == null) { sb.AppendLine("no backpack"); return; }
                var seen = new HashSet<string>();
                foreach (var it in pd.inventory.Data.Inventory)
                {
                    if (it == null || it.IsEmpty || it.Definition == null) continue;
                    if (it.Definition.stackCount > 1 && seen.Add(it.id))
                        sb.AppendLine("  " + it.id.PadRight(28) + StashBubbles.DescribeIcon(it.id));
                    if (it.IsBag && it.Inventory != null)
                        foreach (var inner in it.Inventory)
                            if (inner != null && !inner.IsEmpty && inner.Definition != null && inner.Definition.stackCount > 1 && seen.Add(inner.id))
                                sb.AppendLine("  " + inner.id.PadRight(28) + StashBubbles.DescribeIcon(inner.id) + "  (in " + it.id + ")");
                }
                if (seen.Count == 0) sb.AppendLine("  (no stackable items in backpack)");
            });

            // write: latest + timestamped copy
            string text = sb.ToString();
            string zoneTag = zone == null ? "nozone" : Sanitize(zone.id);
            string stamped = Path.Combine(Paths.BepInExRootPath, "QuickStash-dump-" + zoneTag + "-" + DateTime.Now.ToString("HHmmss") + ".txt");
            File.WriteAllText(LatestDumpPath, text);
            File.WriteAllText(stamped, text);
            return "dump written to " + LatestDumpPath + " and " + Path.GetFileName(stamped) + "  |  " + summary;
        }

        // ---------------------------------------------------------------- key bindings

        private static void AppendKeyBindings(StringBuilder sb, StringBuilder summary)
        {
            if (!LazyInput.IsInitialized) { sb.AppendLine("LazyInput not initialized"); return; }
            var gb = LazyInput.GameBindings;
            if (gb == null || gb.keyBindings == null) { sb.AppendLine("GameBindings.keyBindings is null"); return; }

            var used = new HashSet<KeyCode>();
            var byKey = new Dictionary<KeyCode, List<string>>();
            foreach (var b in gb.keyBindings)
            {
                if (b == null) continue;
                string name = GameKeyName(b.gameKey);
                string combo = b.keyCode.ToString();
                if (b.additionalKeyCodes != null)
                    foreach (var k in b.additionalKeyCodes) combo += " + " + k;   // all additional keys must be held (KeyboardController.Update)
                sb.AppendLine("  " + name.PadRight(34) + " <- " + combo.PadRight(28) + (string.IsNullOrEmpty(b.localeId) ? "" : "  locale=" + b.localeId));

                used.Add(b.keyCode);
                if (b.additionalKeyCodes != null) foreach (var k in b.additionalKeyCodes) used.Add(k);
                List<string> l;
                if (!byKey.TryGetValue(b.keyCode, out l)) byKey[b.keyCode] = l = new List<string>();
                l.Add(name + (b.additionalKeyCodes != null && b.additionalKeyCodes.Length > 0 ? "(+" + b.additionalKeyCodes.Length + ")" : ""));
            }
            sb.AppendLine("total bindings: " + gb.keyBindings.Count);

            sb.AppendLine();
            AppendCollision(sb, summary, "StashKey", Plugin.StashKey.Value, byKey);
            AppendCollision(sb, summary, "DiagnosticsKey", Plugin.DiagnosticsKey.Value, byKey);

            var free = new List<string>();
            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++) if (!used.Contains(k)) free.Add(k.ToString());
            sb.AppendLine("letters not used by any binding: " + (free.Count == 0 ? "(none)" : string.Join(" ", free)));
        }

        private static void AppendCollision(StringBuilder sb, StringBuilder summary, string label, KeyboardShortcut shortcut, Dictionary<KeyCode, List<string>> byKey)
        {
            List<string> hits;
            bool collides = byKey.TryGetValue(shortcut.MainKey, out hits) && hits.Count > 0;
            sb.AppendLine(label + " = " + shortcut + (collides ? "  COLLIDES with game action(s): " + string.Join(", ", hits) : "  no game action bound to this key"));
            summary.Append(" " + label + "Collision=" + (collides ? "YES" : "no"));
        }

        private static string GameKeyName(GameKey key)
        {
            if (key == null) return "null";
            string n = Enumeration.GetNameOfStaticField<GameKey>(key.value);
            return string.IsNullOrEmpty(n) ? "GameKey#" + key.value : n + "(" + key.value + ")";
        }

        // ---------------------------------------------------------------- helpers

        private static void Section(StringBuilder sb, string title, Action body)
        {
            sb.AppendLine("==== " + title);
            try { body(); }
            catch (Exception e) { sb.AppendLine("!! section failed: " + e); }
            sb.AppendLine();
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "nozone";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ' ') chars[i] = '_';
            string r = new string(chars);
            return r.Length > 40 ? r.Substring(0, 40) : r;
        }

        private static string Key(WgoData w) => w.UniqueId != null ? w.UniqueId.ToString() : w.id + "@" + w.Position;

        private static string Flag(HashSet<string> set, WgoData w, string letter) => set.Contains(Key(w)) ? "[" + letter + "]" : "[ ]";

        private static string F(Vector3 v) => "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";

        private static void AppendWgo(StringBuilder sb, WgoData w, Vector3 pos, string flags)
        {
            var d = w.Definition;
            var inv = w.Inventory;
            var data = inv != null ? inv.Data : null;
            string refInfo = d.hasRefToOtherWgoInventory ? d.refToOtherWgoInventory : "-";
            string used = data != null ? data.InventoryFillSize + "/" + data.InventorySize : "?";

            sb.AppendLine(flags + " dist=" + ContainerFinder.Distance(w, pos).ToString("0.0").PadLeft(5)
                          + "  id=" + w.id
                          + "  uid=" + (w.UniqueId != null ? w.UniqueId.ToString() : "-")
                          + "  pos=" + F(w.Position));
            sb.AppendLine("        size=" + d.inventorySize + " used=" + used
                          + "  open=" + d.OpenInMultiInventory
                          + "  conveyor=" + d.conveyorType
                          + "  refInv=" + refInfo
                          + "  hidden=" + w.IsHidden
                          + "  interact=" + w.IsInteractable
                          + "  temp=" + w.isTempObject
                          + "  wl=" + Filter(d.inventoryWhiteList)
                          + "  bl=" + Filter(d.inventoryBlackList));
            if (data == null || data.Inventory == null)
                sb.AppendLine("        items: (no inventory data)");
            else
                sb.AppendLine("        items(" + data.Inventory.Count + "): " + ItemList(data.Inventory));
        }

        private static void AppendDef(StringBuilder sb, WGODef d)
        {
            sb.AppendLine("  " + d.id.PadRight(40) + " size=" + d.inventorySize.ToString().PadLeft(3)
                          + "  conveyor=" + d.conveyorType
                          + (d.hasRefToOtherWgoInventory ? "  refInv=" + d.refToOtherWgoInventory : "")
                          + (d.inventoryWhiteList != null && !d.inventoryWhiteList.IsEmpty ? "  wl=" + Filter(d.inventoryWhiteList) : "")
                          + (d.inventoryBlackList != null && !d.inventoryBlackList.IsEmpty ? "  bl=" + Filter(d.inventoryBlackList) : ""));
        }

        private static string Filter(ItemFilter f)
        {
            if (f == null || f.IsEmpty) return "empty";
            var sb = new StringBuilder();
            if (f.itemsIds != null && f.itemsIds.Count > 0) sb.Append("ids[" + string.Join(",", f.itemsIds) + "]");
            if (f.groupsIds != null && f.groupsIds.Count > 0) sb.Append("groups[" + string.Join(",", f.groupsIds) + "]");
            return sb.Length == 0 ? "empty" : sb.ToString();
        }

        private static string ItemList(List<Item> items)
        {
            var parts = new List<string>();
            foreach (var it in items)
            {
                if (it == null) continue;
                string s = it.id + " x " + it.Count;
                if (it.IsBag && it.Inventory != null) s += " {bag: " + ItemList(it.Inventory) + "}";
                parts.Add(s);
            }
            return parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
        }

        private static void AppendPlayerInventory(StringBuilder sb, Inventory inv)
        {
            if (inv == null || inv.Data == null) { sb.AppendLine("null"); return; }
            var data = inv.Data;
            sb.AppendLine("size " + data.InventoryFillSize + "/" + data.InventorySize + "  viewId=" + inv.ViewId);
            foreach (var it in data.Inventory)
            {
                if (it == null) continue;
                sb.AppendLine("  " + ItemLine(it));
                if (it.IsBag && it.Inventory != null)
                    foreach (var inner in it.Inventory)
                        sb.AppendLine("      (in bag) " + ItemLine(inner));
            }
        }

        private static string ItemLine(Item it)
        {
            var d = it.Definition;
            return (it.id + " x " + it.Count).PadRight(32)
                   + " stack=" + (d != null ? d.stackCount.ToString() : "?")
                   + " bag=" + it.IsBag
                   + " tool=" + (d != null && d.isTool)
                   + " weapon=" + (d != null && d.isWeapon)
                   + " quest=" + (d != null && d.isQuestItem);
        }
    }
}
