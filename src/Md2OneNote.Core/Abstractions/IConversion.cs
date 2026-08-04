namespace Md2OneNote.Core
{
    /// <summary>
    /// Pass 1 of the pipeline. Pure: same input, same output, no I/O (NFR-21).
    /// </summary>
    public interface IMarkdownDocumentParser
    {
        ParsedDocument Parse(string markdown, ParseOptions options);
    }

    /// <summary>
    /// Pass 3 of the pipeline. Pure, and required to be total: every failure is already encoded
    /// in the outcomes it is handed, so it renders a fallback rather than throwing (FR-13, FR-14).
    /// </summary>
    public interface IOneNoteXmlRenderer
    {
        string Render(ParsedDocument document, RenderInputs inputs);
    }
}
