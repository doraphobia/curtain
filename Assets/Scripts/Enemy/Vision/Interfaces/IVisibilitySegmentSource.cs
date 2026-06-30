using System.Collections.Generic;

namespace DuoCurtain.Vision
{
    public interface IVisibilitySegmentSource
    {
        void CollectVisibilitySegments(List<VisibilitySegment> results);
    }
}
