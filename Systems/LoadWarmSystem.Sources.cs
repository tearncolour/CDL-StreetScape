using CDLC.TS.Geo;
using CDLC.TS.Render;

namespace CDLC.TS
{
    /// <summary>The sources of first-time work the load's hold waits for, and how each is read (every one is a count of what is still to be made; a source that cannot say yet holds the gate).</summary>
    internal sealed partial class LoadWarmSystem
    {
        private const string kPictures = "pictures", kKerbs = "kerbs", kClear = "clear";
        private double m_FitsMsBefore;

        partial void AddSources(LoadGate gate)
        {
            gate.Add(kTiles, 3f);
            gate.Add(kFits, 2f);
            gate.Add(kMaterials, 2f);
            gate.Add(kPictures, 1f);
            gate.Add(kKerbs, 1f);
            gate.Add(kClear, 1f);
            gate.Add(kPlants, 1f);
            AreaFitSystem fits = AreaFitSystem.Instance;
            m_FitsMsBefore = fits != null ? fits.LoadMs : 0.0;
        }

        partial void ReportSources(LoadGate gate)
        {
            ChunkSystem chunks = ChunkSystem.Instance;
            if (chunks != null)
            {
                int tiles = chunks.FirstUseWaiting(out bool known);
                gate.Report(kTiles, tiles, known);
                gate.Report(kPictures, FacePainter.Waiting + FacePainter.Pending, known);
                gate.Report(kClear, ClearMarkingsSystem.Instance != null ? ClearMarkingsSystem.Instance.FirstUseWaiting(chunks) : 0, known);
            }
            else
            {
                // No renderer: nothing of the tiles, the pictures or the clearing is coming.
                gate.Report(kTiles, 0);
                gate.Report(kPictures, 0);
                gate.Report(kClear, 0);
            }

            gate.Report(kMaterials, TsMaterials.WarmWaiting);
            AreaFitSystem fits = AreaFitSystem.Instance;
            gate.Report(kFits, fits != null ? fits.FirstUseWaiting : 0, chunks == null || chunks.Synced);
            if (fits != null)
                gate.AddMs(kFits, fits.LoadMs - m_FitsMsBefore);
            m_FitsMsBefore = fits != null ? fits.LoadMs : 0.0;
            KerbSystem kerbs = World.GetExistingSystemManaged<KerbSystem>();
            gate.Report(kKerbs, kerbs != null ? kerbs.FirstUseWaiting : 0);
            PlantSystem plants = World.GetExistingSystemManaged<PlantSystem>();
            gate.Report(kPlants, plants != null ? plants.FirstUseWaiting : 0);
        }
    }
}
