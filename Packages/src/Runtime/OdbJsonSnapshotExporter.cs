using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace oojjrs.odb
{
    public sealed class OdbJsonSnapshotExporter : OdbExporterInterface
    {
        Task OdbExporterInterface.ExportAsync(OdbExportSession session, Stream destination, CancellationToken cancellationToken)
        {
            return OdbJsonSnapshot.ExportAsync(session, destination, cancellationToken);
        }
    }
}
