using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FigmaImporter.Editor
{
    public class FigmaParser
    {
        public List<Node> ParseResult(string s, Action<int, int, string> onProgress = null)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return new List<Node>();
            }

            try
            {
                var root = JObject.Parse(s);
                var nodesToken = root["nodes"] as JObject;
                if (nodesToken != null)
                {
                    return ParseNodes(nodesToken, onProgress);
                }

                var documentToken = root["document"];
                if (documentToken != null && documentToken.Type != JTokenType.Null)
                {
                    return ParseFile(documentToken, onProgress);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FigmaImporter] Failed to parse Figma response: {e.Message}");
            }

            Debug.LogWarning("[FigmaImporter] No parsable node data found in response.");
            return new List<Node>();
        }

        private List<Node> ParseFile(JToken documentToken, Action<int, int, string> onProgress)
        {
            onProgress?.Invoke(0, 1, "Parsing root node...");
            var node = ParseSingleNode(documentToken);
            if (node == null)
            {
                return new List<Node>();
            }

            onProgress?.Invoke(1, 1, "Node data loaded.");
            return new List<Node> { node };
        }

        private List<Node> ParseNodes(JObject nodesObject, Action<int, int, string> onProgress)
        {
            var result = new List<Node>();
            var totalCount = nodesObject.Count;
            var loadedCount = 0;
            onProgress?.Invoke(loadedCount, totalCount, "Preparing node list...");

            foreach (var property in nodesObject.Properties())
            {
                var nodeToken = property.Value?["document"];
                if (nodeToken == null || nodeToken.Type == JTokenType.Null)
                {
                    loadedCount++;
                    onProgress?.Invoke(loadedCount, totalCount, $"Skipped {property.Name} (no document).");
                    continue;
                }

                var node = ParseSingleNode(nodeToken);
                if (node != null)
                {
                    result.Add(node);
                }
                loadedCount++;
                var label = node?.name ?? property.Name;
                onProgress?.Invoke(loadedCount, totalCount, $"Loaded {label}");
            }

            Debug.Log($"[FigmaImporter] Nodes parsed: {result.Count}");
            return result;
        }

        private Node ParseSingleNode(JToken nodeToken)
        {
            try
            {
                return nodeToken.ToObject<Node>(JsonSerializer.CreateDefault());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaImporter] Failed to parse a node: {e.Message}");
                return null;
            }
        }
    }
}
