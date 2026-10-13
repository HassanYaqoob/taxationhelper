using System.Text;
using ClosedXML.Excel;
using Kira.Application.Common;
using Kira.Domain.Common;

namespace Kira.Infrastructure.Files;

/// <summary>Turns an uploaded statement into text (CSV, Excel, text) or an attachment the AI reads directly (PDF, image).</summary>
public sealed class StatementReader : IStatementReader
{
    public StatementContent Read(FileUpload file)
    {
        var type = FileRules.Require(file, FileRules.Pdf, "image/*", FileRules.Xlsx, "text/csv", "text/plain");
        if (type == FileRules.Pdf || FileRules.IsImage(type)) return new StatementContent(null, [new AiAttachment(file.Data, type)]);
        if (type == FileRules.Xlsx)
        {
            using var wb = new XLWorkbook(new MemoryStream(file.Data));
            var sb = new StringBuilder();
            foreach (var ws in wb.Worksheets)
                foreach (var row in ws.RowsUsed())
                    sb.AppendLine(string.Join(" | ", row.CellsUsed().Select(c => c.GetFormattedString())));
            return new StatementContent(sb.ToString(), []);
        }
        var text = Encoding.UTF8.GetString(file.Data).TrimStart('﻿');
        if (text.Trim().Length == 0) throw new DomainException("The file is empty.");
        return new StatementContent(text, []);
    }
}
