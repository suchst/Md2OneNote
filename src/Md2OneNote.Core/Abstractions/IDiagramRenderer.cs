using System.Threading;
using System.Threading.Tasks;

namespace Md2OneNote.Core
{
    /// <summary>
    /// Pass 2 for diagrams. Implementations must not throw for ordinary rendering failures —
    /// a failure is a <see cref="DiagramOutcome"/>, because the import always completes (FR-14).
    /// </summary>
    public interface IDiagramRenderer
    {
        bool CanRender(string language);

        Task<DiagramOutcome> RenderAsync(DiagramRequest request, CancellationToken ct);
    }
}
