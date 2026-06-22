using System;
using UnityEngine;

namespace AE2Unity
{
    public sealed class Ae2ShaderClip : ScriptableObject
    {
        [SerializeField] private Ae2ShaderDocument document = new Ae2ShaderDocument();
        [SerializeField, TextArea(4, 16)] private string sourceJson = string.Empty;
        [SerializeField] private string[] importWarnings = Array.Empty<string>();

        public Ae2ShaderDocument Document => document;
        public string SourceJson => sourceJson;
        public string[] ImportWarnings => importWarnings;

        public int Width => document?.comp?.width ?? 0;
        public int Height => document?.comp?.height ?? 0;
        public float Duration => document?.comp?.duration ?? 0f;
        public float FrameRate => document?.comp?.frameRate ?? 0f;

        public void Initialize(Ae2ShaderDocument importedDocument, string json, string[] warnings)
        {
            document = importedDocument ?? new Ae2ShaderDocument();
            sourceJson = json ?? string.Empty;
            importWarnings = warnings ?? Array.Empty<string>();
        }
    }
}
