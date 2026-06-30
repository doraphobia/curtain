using UnityEngine;

/// <summary>
/// Prefab-based <see cref="IFootprintRenderer"/> implementation.
/// </summary>
[DisallowMultipleComponent]
public class PrefabFootprintRenderer : MonoBehaviour, IFootprintRenderer
{
    [SerializeField] private GameObject leftFootprintPrefab;
    [SerializeField] private GameObject rightFootprintPrefab;
    [SerializeField] private FootprintVisualProfile visualProfile = new FootprintVisualProfile();

    public FootprintVisualProfile VisualProfile => visualProfile;

    public void Configure(GameObject leftPrefab, GameObject rightPrefab, FootprintVisualProfile profile)
    {
        leftFootprintPrefab = leftPrefab;
        rightFootprintPrefab = rightPrefab;
        if (profile != null)
            visualProfile = profile;
    }

    public FootprintInstance SpawnFootprint(FootprintSpawnData data)
    {
        GameObject prefab = data.side == FootprintSide.Left ? leftFootprintPrefab : rightFootprintPrefab;
        if (prefab == null)
            return null;

        GameObject instanceObject = Instantiate(prefab, data.position, data.rotation, data.parent);
        instanceObject.SetActive(true);
        FootprintInstance instance = instanceObject.GetComponent<FootprintInstance>();
        if (instance == null)
            instance = instanceObject.AddComponent<FootprintInstance>();

        instance.Initialize(data.side, visualProfile);
        return instance;
    }
}
