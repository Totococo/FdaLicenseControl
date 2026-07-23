using System.Collections.Generic;

namespace FdaLicenseControl.Models
{
    public sealed class FdaUiState
    {
        public HashSet<string> HighlightedRowKeys { get; set; } = new HashSet<string>();
    }
}
