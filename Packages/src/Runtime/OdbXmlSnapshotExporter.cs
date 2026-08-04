using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace oojjrs.odb
{
    public sealed class OdbXmlSnapshotExporter : OdbExporterInterface
    {
        Task OdbExporterInterface.ExportAsync(OdbExportSession session, Stream destination, CancellationToken cancellationToken)
        {
            return OdbXmlSnapshot.ExportAsync(session, destination, cancellationToken);
        }
    }
}
