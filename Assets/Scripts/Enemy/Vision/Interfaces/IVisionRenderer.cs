namespace DuoCurtain.Vision
{
    public interface IVisionRenderer
    {
        void Initialize(VisionRendererContext context);
        void Render(VisionSnapshot snapshot, VisionRenderParameters parameters);
        void Hide();
        void Dispose();
    }
}
