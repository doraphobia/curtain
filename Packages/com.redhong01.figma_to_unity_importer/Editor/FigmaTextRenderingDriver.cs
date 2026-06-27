using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FigmaImporter.Editor
{
    internal readonly struct FigmaTextRenderContext
    {
        public readonly float scale;
        public readonly FigmaImporterSettings settings;

        public FigmaTextRenderContext(float scale, FigmaImporterSettings settings)
        {
            this.scale = scale;
            this.settings = settings;
        }
    }

    internal interface IFigmaTextRenderModule
    {
        string ModuleId { get; }
        bool IsEnabled(FigmaTextRenderContext context);
        void Apply(TextMeshProUGUI tmp, Node node, FigmaTextRenderContext context);
    }

    internal sealed class FigmaTextRenderingDriver
    {
        private readonly List<IFigmaTextRenderModule> _modules;

        private FigmaTextRenderingDriver(List<IFigmaTextRenderModule> modules)
        {
            _modules = modules ?? new List<IFigmaTextRenderModule>();
        }

        public static FigmaTextRenderingDriver CreateDefault()
        {
            return new FigmaTextRenderingDriver(new List<IFigmaTextRenderModule>
            {
                new FigmaToTmpTypographyModule()
            });
        }

        public void RegisterModule(IFigmaTextRenderModule module)
        {
            if (module == null)
            {
                return;
            }

            for (var i = 0; i < _modules.Count; i++)
            {
                if (_modules[i] == null)
                {
                    continue;
                }

                if (string.Equals(_modules[i].ModuleId, module.ModuleId, StringComparison.Ordinal))
                {
                    _modules[i] = module;
                    return;
                }
            }

            _modules.Add(module);
        }

        public void UnregisterModule(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                return;
            }

            for (var i = _modules.Count - 1; i >= 0; i--)
            {
                var module = _modules[i];
                if (module == null || !string.Equals(module.ModuleId, moduleId, StringComparison.Ordinal))
                {
                    continue;
                }

                _modules.RemoveAt(i);
            }
        }

        public void Apply(TextMeshProUGUI tmp, Node node, FigmaTextRenderContext context)
        {
            if (tmp == null || node == null || _modules == null || _modules.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _modules.Count; i++)
            {
                var module = _modules[i];
                if (module == null || !module.IsEnabled(context))
                {
                    continue;
                }

                try
                {
                    module.Apply(tmp, node, context);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FigmaImporter] Text render module '{module.ModuleId}' failed: {e.Message}");
                }
            }
        }
    }

    internal sealed class FigmaToTmpTypographyModule : IFigmaTextRenderModule
    {
        private readonly FigmaToTMPAdapter _adapter = new FigmaToTMPAdapter();

        public string ModuleId => "figma_to_tmp_adapter";

        public bool IsEnabled(FigmaTextRenderContext context)
        {
            return context.settings == null || context.settings.EnableTypographyAdapter;
        }

        public void Apply(TextMeshProUGUI tmp, Node node, FigmaTextRenderContext context)
        {
            if (tmp == null || node == null || node.style == null)
            {
                return;
            }

            var settings = context.settings;
            _adapter.enableScaleCorrection = settings == null || settings.EnableTypographyScaleCorrection;
            _adapter.escapeInputText = settings == null || settings.EscapeTypographyInputText;
            _adapter.enableDebugLog = settings != null && settings.EnableTypographyDebugLog;

            var figmaStyle = FigmaTextStyle.FromNode(node, context.scale);
            _adapter.Apply(tmp, figmaStyle, node.characters);
        }
    }
}
