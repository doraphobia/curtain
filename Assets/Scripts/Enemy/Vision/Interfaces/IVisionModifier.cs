namespace DuoCurtain.Vision
{
    public interface IVisionModifier
    {
        void Modify(ref VisionRaySample sample);
    }
}
