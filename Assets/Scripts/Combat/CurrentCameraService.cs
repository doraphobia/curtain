using UnityEngine;

namespace DuoCurtain.Combat
{
    public interface ICurrentCameraProvider
    {
        bool TryGetCurrentGameplayCamera(out Camera camera);
    }

    public static class CurrentCameraService
    {
        private sealed class EnabledCameraProvider : ICurrentCameraProvider
        {
            public bool TryGetCurrentGameplayCamera(out Camera camera)
            {
                camera = null;
                Camera main = Camera.main;
                if (main != null && main.isActiveAndEnabled)
                {
                    camera = main;
                    return true;
                }

                Camera[] cameras = Camera.allCameras;
                float bestDepth = float.MinValue;
                for (int i = 0; i < cameras.Length; i++)
                {
                    Camera candidate = cameras[i];
                    if (candidate == null || !candidate.isActiveAndEnabled || candidate.depth < bestDepth)
                        continue;
                    bestDepth = candidate.depth;
                    camera = candidate;
                }
                return camera != null;
            }
        }

        private static readonly ICurrentCameraProvider DefaultProvider = new EnabledCameraProvider();
        public static ICurrentCameraProvider Provider { get; set; } = DefaultProvider;

        public static bool TryGetCurrentGameplayCamera(out Camera camera)
        {
            ICurrentCameraProvider provider = Provider ?? DefaultProvider;
            return provider.TryGetCurrentGameplayCamera(out camera);
        }

        public static void RestoreDefaultProvider()
        {
            Provider = DefaultProvider;
        }
    }
}
