#if UNITY_EDITOR
using UnityEngine;

namespace Curtain.Settings
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class CurtainSettingsRuntimeProvider : MonoBehaviour
    {
        [SerializeField] private CurtainSettingsBundle bundle;

        public CurtainSettingsBundle Bundle => bundle;

        private void Awake()
        {
            if (bundle != null)
                CurtainSettingsLocator.RegisterBundle(bundle);
        }

        private void OnDestroy()
        {
            CurtainSettingsLocator.UnregisterBundle(bundle);
        }
    }
}

#endif
