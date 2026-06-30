/// <summary>
/// Optional hook for future surface/room-dependent footprint visuals.
/// </summary>
public interface IFootprintSurfaceModifier
{
    void ModifyFootprint(ref FootprintSpawnData data);
}
