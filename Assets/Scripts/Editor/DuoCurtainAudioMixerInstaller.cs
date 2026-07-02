#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace DuoCurtain.Editor
{
    [InitializeOnLoad]
    public static class DuoCurtainAudioMixerInstaller
    {
        private const string MixerAssetPath = "Assets/Resources/DuoCurtainMaster.mixer";
        private static bool loggedMissingMixer;

        static DuoCurtainAudioMixerInstaller()
        {
            EditorApplication.delayCall += TryEnsureMixer;
        }

        [MenuItem("Tools/Duo Curtain/Audio/Ensure Master Mixer")]
        private static void TryEnsureMixerMenu()
        {
            TryEnsureMixer();
        }

        private static void TryEnsureMixer()
        {
            try
            {
                EnsureFolder("Assets/Resources");

                AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerAssetPath);
                if (mixer == null)
                {
                    if (!loggedMissingMixer)
                    {
                        loggedMissingMixer = true;
                        Debug.LogFormat(
                            LogType.Warning,
                            LogOption.NoStacktrace,
                            null,
                            "[DuoCurtainAudioMixerInstaller] Missing AudioMixer at {0}. Create one manually (Create > Audio Mixer) and expose a float named '{1}'. Otherwise volume will fall back to AudioListener.volume.",
                            MixerAssetPath,
                            DuoCurtainSettingsManager.MasterVolumeExposedParam);
                    }
                    return;
                }

                bool hasParam = mixer.SetFloat(DuoCurtainSettingsManager.MasterVolumeExposedParam, 0f);
                if (!hasParam)
                {
                    Debug.LogFormat(
                        LogType.Warning,
                        LogOption.NoStacktrace,
                        null,
                        "[DuoCurtainAudioMixerInstaller] Mixer exists but does not expose '{0}'.",
                        DuoCurtainSettingsManager.MasterVolumeExposedParam);
                }

                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[DuoCurtainAudioMixerInstaller] Master mixer ready at {0}", MixerAssetPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DuoCurtainAudioMixerInstaller] Failed: " + ex.Message);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
            string name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                return;

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif

