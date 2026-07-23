using System;
using System.IO;
using System.Text.Json;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public sealed class FdaUiStateService
    {
        private readonly string _stateFilePath;

        public FdaUiStateService()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FdaLicenseControl", "State");
            Directory.CreateDirectory(folder);
            _stateFilePath = Path.Combine(folder, "ui-state.json");
        }

        public FdaUiState Load()
        {
            try
            {
                if (!File.Exists(_stateFilePath))
                    return new FdaUiState();

                var text = File.ReadAllText(_stateFilePath);
                var opts = new JsonSerializerOptions();
                var state = JsonSerializer.Deserialize<FdaUiState>(text, opts);
                if (state == null)
                    return new FdaUiState();
                if (state.HighlightedRowKeys == null)
                    state.HighlightedRowKeys = new System.Collections.Generic.HashSet<string>();
                return state;
            }
            catch
            {
                return new FdaUiState();
            }
        }

        public void Save(FdaUiState state)
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var tempPath = _stateFilePath + ".tmp";
            try
            {
                var json = JsonSerializer.Serialize(state, opts);
                File.WriteAllText(tempPath, json);
                // Replace atomically
                if (File.Exists(_stateFilePath))
                    File.Delete(_stateFilePath);
                File.Move(tempPath, _stateFilePath);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }
    }
}
