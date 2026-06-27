using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FigmaImporter.Editor
{
    internal sealed class FigmaFrameSyncDiffResult
    {
        public readonly List<global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry> Changes =
            new List<global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry>();

        public int AddedCount { get; set; }
        public int ChangedCount { get; set; }
        public int RemovedCount { get; set; }

        public string Summary =>
            $"{AddedCount} added, {ChangedCount} changed, {RemovedCount} removed";
    }

    internal static class FigmaFrameSyncDiffUtility
    {
        public static List<global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry> BuildSnapshot(Node rootNode)
        {
            var result = new List<global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry>();
            if (rootNode == null)
            {
                return result;
            }

            CollectNodeSnapshot(rootNode, null, result);
            return result;
        }

        public static FigmaFrameSyncDiffResult ComputeDiff(
            IList<global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry> baseline,
            IList<global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry> latest)
        {
            var result = new FigmaFrameSyncDiffResult();
            var baselineById = ToSnapshotMap(baseline);
            var latestById = ToSnapshotMap(latest);

            foreach (var latestEntry in latestById)
            {
                if (!baselineById.TryGetValue(latestEntry.Key, out var baselineEntry))
                {
                    result.Changes.Add(CreateChangeEntry("Added", latestEntry.Value, "Node added in Figma."));
                    result.AddedCount++;
                    continue;
                }

                if (!string.Equals(baselineEntry.signature, latestEntry.Value.signature, StringComparison.Ordinal))
                {
                    result.Changes.Add(CreateChangeEntry("Changed", latestEntry.Value, "Node content changed in Figma."));
                    result.ChangedCount++;
                }
            }

            foreach (var baselineEntry in baselineById)
            {
                if (latestById.ContainsKey(baselineEntry.Key))
                {
                    continue;
                }

                result.Changes.Add(CreateChangeEntry("Removed", baselineEntry.Value, "Node removed from Figma."));
                result.RemovedCount++;
            }

            result.Changes.Sort(CompareChanges);
            return result;
        }

        private static void CollectNodeSnapshot(
            Node node,
            string parentNodeId,
            ICollection<global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry> output)
        {
            if (node == null)
            {
                return;
            }

            output.Add(new global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry
            {
                nodeId = node.id ?? string.Empty,
                parentNodeId = parentNodeId ?? string.Empty,
                nodeName = node.name ?? string.Empty,
                nodeType = node.type ?? string.Empty,
                signature = ComputeNodeSignature(node)
            });

            if (node.children == null)
            {
                return;
            }

            for (var i = 0; i < node.children.Length; i++)
            {
                CollectNodeSnapshot(node.children[i], node.id, output);
            }
        }

        private static Dictionary<string, global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry> ToSnapshotMap(
            IList<global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry> snapshot)
        {
            var map = new Dictionary<string, global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry>(
                StringComparer.OrdinalIgnoreCase);
            if (snapshot == null)
            {
                return map;
            }

            for (var i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.nodeId))
                {
                    continue;
                }

                map[entry.nodeId] = entry;
            }

            return map;
        }

        private static global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry CreateChangeEntry(
            string changeType,
            global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry source,
            string summary)
        {
            return new global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry
            {
                selected = true,
                changeType = changeType ?? "Changed",
                nodeId = source != null ? source.nodeId : string.Empty,
                nodeName = source != null ? source.nodeName : string.Empty,
                summary = summary ?? string.Empty
            };
        }

        private static int CompareChanges(
            global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry a,
            global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry b)
        {
            var leftOrder = GetChangeOrder(a != null ? a.changeType : string.Empty);
            var rightOrder = GetChangeOrder(b != null ? b.changeType : string.Empty);
            var orderCompare = leftOrder.CompareTo(rightOrder);
            if (orderCompare != 0)
            {
                return orderCompare;
            }

            var leftName = a != null ? a.nodeName : string.Empty;
            var rightName = b != null ? b.nodeName : string.Empty;
            return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetChangeOrder(string changeType)
        {
            if (string.Equals(changeType, "Added", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(changeType, "Changed", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(changeType, "Removed", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 3;
        }

        private static string ComputeNodeSignature(Node node)
        {
            var sb = new StringBuilder(512);
            AppendValue(sb, node.id);
            AppendValue(sb, node.name);
            AppendValue(sb, node.type);
            AppendValue(sb, node.characters);
            AppendFloat(sb, node.rotation);
            AppendFloat(sb, node.itemSpacing);
            AppendFloat(sb, node.counterAxisSpacing);
            AppendFloat(sb, node.paddingLeft);
            AppendFloat(sb, node.paddingRight);
            AppendFloat(sb, node.paddingTop);
            AppendFloat(sb, node.paddingBottom);
            AppendFloat(sb, node.minWidth);
            AppendFloat(sb, node.maxWidth);
            AppendFloat(sb, node.minHeight);
            AppendFloat(sb, node.maxHeight);
            AppendValue(sb, node.layoutMode);
            AppendValue(sb, node.layoutWrap);
            AppendValue(sb, node.layoutPositioning);
            AppendValue(sb, node.primaryAxisSizingMode);
            AppendValue(sb, node.counterAxisSizingMode);
            AppendValue(sb, node.primaryAxisAlignItems);
            AppendValue(sb, node.counterAxisAlignItems);
            AppendValue(sb, node.layoutAlign);
            AppendValue(sb, node.layoutSizingHorizontal);
            AppendValue(sb, node.layoutSizingVertical);
            AppendBool(sb, node.clipsContent);
            AppendBool(sb, node.isMask);
            AppendBool(sb, node.isMaskOutline);
            AppendValue(sb, node.maskType);

            AppendAbsoluteBoundingBox(sb, node.absoluteBoundingBox);
            AppendStyle(sb, node.style);
            AppendFillArray(sb, node.fills);
            AppendFillArray(sb, node.background);
            AppendFillArray(sb, node.strokes);
            AppendEffects(sb, node.effects);
            AppendChildrenToken(sb, node.children);

            return HashString(sb.ToString());
        }

        private static void AppendChildrenToken(StringBuilder sb, Node[] children)
        {
            if (children == null || children.Length == 0)
            {
                sb.Append("|children:none");
                return;
            }

            sb.Append("|children:");
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child == null)
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append(child.id);
                    sb.Append('#');
                    sb.Append(child.type);
                }

                if (i < children.Length - 1)
                {
                    sb.Append(',');
                }
            }
        }

        private static void AppendAbsoluteBoundingBox(StringBuilder sb, AbsoluteBoundingBox box)
        {
            if (box == null)
            {
                sb.Append("|bbox:none");
                return;
            }

            sb.Append("|bbox:");
            AppendFloat(sb, box.x);
            AppendFloat(sb, box.y);
            AppendFloat(sb, box.width);
            AppendFloat(sb, box.height);
        }

        private static void AppendStyle(StringBuilder sb, Style style)
        {
            if (style == null)
            {
                sb.Append("|style:none");
                return;
            }

            sb.Append("|style:");
            AppendValue(sb, style.fontFamily);
            AppendValue(sb, style.fontPostScriptName);
            AppendValue(sb, style.fontStyle);
            AppendValue(sb, style.textAutoResize);
            AppendValue(sb, style.textAlignHorizontal);
            AppendValue(sb, style.textAlignVertical);
            AppendValue(sb, style.textCase);
            AppendValue(sb, style.textDecoration);
            AppendValue(sb, style.leadingTrim);
            AppendFloat(sb, style.fontSize);
            AppendFloat(sb, style.letterSpacing);
            AppendFloat(sb, style.wordSpacing);
            AppendFloat(sb, style.paragraphSpacing);
            AppendFloat(sb, style.paragraphIndent);
            AppendFloat(sb, style.lineHeightPx);
            AppendFloat(sb, style.lineHeightPercent);
            AppendFloat(sb, style.lineHeightPercentFontSize);
            AppendValue(sb, style.lineHeightUnit);
            sb.Append(style.fontWeight);
            sb.Append('|');
        }

        private static void AppendFillArray(StringBuilder sb, Fill[] fills)
        {
            if (fills == null || fills.Length == 0)
            {
                sb.Append("|fills:none");
                return;
            }

            sb.Append("|fills:");
            for (var i = 0; i < fills.Length; i++)
            {
                var fill = fills[i];
                if (fill == null)
                {
                    sb.Append("null");
                }
                else
                {
                    AppendValue(sb, fill.type);
                    AppendValue(sb, fill.visible);
                    AppendValue(sb, fill.blendMode);
                    AppendColor(sb, fill.color);
                    AppendValue(sb, fill.imageRef);
                }

                if (i < fills.Length - 1)
                {
                    sb.Append(',');
                }
            }
        }

        private static void AppendEffects(StringBuilder sb, Effect[] effects)
        {
            if (effects == null || effects.Length == 0)
            {
                sb.Append("|effects:none");
                return;
            }

            sb.Append("|effects:");
            for (var i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (effect == null)
                {
                    sb.Append("null");
                }
                else
                {
                    AppendValue(sb, effect.type);
                    AppendValue(sb, effect.blendMode);
                    sb.Append(effect.visible ? "1" : "0");
                    sb.Append('|');
                    AppendFloat(sb, effect.radius);
                    AppendColor(sb, effect.color);
                }

                if (i < effects.Length - 1)
                {
                    sb.Append(',');
                }
            }
        }

        private static void AppendColor(StringBuilder sb, Color color)
        {
            if (color == null)
            {
                sb.Append("null");
                return;
            }

            AppendFloat(sb, color.r);
            AppendFloat(sb, color.g);
            AppendFloat(sb, color.b);
            AppendFloat(sb, color.a);
        }

        private static void AppendValue(StringBuilder sb, string value)
        {
            sb.Append(value ?? string.Empty);
            sb.Append('|');
        }

        private static void AppendFloat(StringBuilder sb, float value)
        {
            if (float.IsNaN(value))
            {
                sb.Append("NaN|");
                return;
            }

            if (float.IsInfinity(value))
            {
                sb.Append("INF|");
                return;
            }

            sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append('|');
        }

        private static void AppendBool(StringBuilder sb, bool value)
        {
            sb.Append(value ? "1|" : "0|");
        }

        private static string HashString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}
